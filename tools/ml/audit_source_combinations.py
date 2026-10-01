"""Compare existing forecasts with frozen ML and AI components on matched fixtures."""
import argparse
import collections
import datetime as datetime
import hashlib
import json
from pathlib import Path

from audit_markets import metrics, outcome, probability, timestamp


def verified(directory, name, provenance):
    content = (directory / f"{name}.json").read_bytes()
    entry = provenance["Files"][name]
    if hashlib.sha256(content).hexdigest() != entry["Sha256"]:
        raise ValueError(f"Checksum mismatch: {name}")
    rows = json.loads(content)
    if len(rows) != entry["Rows"]:
        raise ValueError(f"Row count mismatch: {name}")
    return rows


def compare(rows, baseline):
    results = {}
    for candidate in rows[0]["probabilities"] if rows else []:
        scored = [(row["probabilities"][candidate], row["actual"]) for row in rows]
        result = metrics(scored)
        result["improved_calls"] = sum((row["probabilities"][candidate] >= .5) == row["actual"]
                                       and (row["probabilities"][baseline] >= .5) != row["actual"] for row in rows)
        result["worsened_calls"] = sum((row["probabilities"][candidate] >= .5) != row["actual"]
                                       and (row["probabilities"][baseline] >= .5) == row["actual"] for row in rows)
        result["brier_difference"] = sum((row["probabilities"][candidate] - row["actual"]) ** 2
                                         - (row["probabilities"][baseline] - row["actual"]) ** 2
                                         for row in rows) / len(rows) if rows else None
        results[candidate] = result
    return results


def run(directory, goals_path, start, until):
    provenance = json.loads((directory / "export-provenance.json").read_text())
    fixtures = {row["Id"]: row for row in verified(directory, "fixtures", provenance)}
    snapshots = verified(directory, "prediction-snapshots", provenance)
    forecasts = verified(directory, "model-forecasts", provenance)
    providers = verified(directory, "provider-predictions", provenance)
    generations = verified(directory, "goal-rate-generations", provenance)
    goals_bytes = goals_path.read_bytes()
    goal_file = json.loads(goals_bytes)
    if goal_file["InputSha256"] != provenance["Files"]["fixtures"]["Sha256"]:
        raise ValueError("ML forecasts use a different fixture export.")
    goal_report = goal_file["Report"]
    if timestamp(goal_report["CalibrationThroughUtc"]).replace(tzinfo=datetime.timezone.utc) >= start:
        raise ValueError("ML calibration crosses the evaluation boundary.")
    goal_rows = {row["FixtureId"]: row for row in goal_report["Predictions"]}
    finished = {identifier: row for identifier, row in fixtures.items() if row["Status"] == "FT"
                and start <= timestamp(row["Date"]) < until}
    by_fixture = collections.defaultdict(list)
    for row in snapshots:
        fixture = finished.get(row["FixtureId"])
        if fixture and timestamp(row["KickoffUtc"]) == timestamp(fixture["Date"]) and (
                timestamp(row["CapturedAtUtc"]) <= timestamp(fixture["Date"]) - datetime.timedelta(hours=1)):
            by_fixture[fixture["Id"]].append(row)
    latest = {identifier: sorted(rows, key=lambda row: (-timestamp(row["CapturedAtUtc"]).timestamp(), row["Id"]))[0]
              for identifier, rows in by_fixture.items()}
    provider_finished = [row for row in providers if row["FixtureId"] in finished]
    provider_eligible = {row["FixtureId"]: row for row in provider_finished
                         if timestamp(row["CapturedAtUtc"]) <= timestamp(finished[row["FixtureId"]]["Date"])
                         - datetime.timedelta(hours=1)}
    model_forecasts = collections.defaultdict(list)
    for row in forecasts:
        fixture = finished.get(row["FixtureId"])
        if not fixture:
            continue
        if (timestamp(row["KickoffUtc"]) == timestamp(fixture["Date"])
                and timestamp(row["PredictedAtUtc"]) <= timestamp(fixture["Date"]) - datetime.timedelta(hours=1)
                and all(probability(row[field], strict=True) for field in ("SystemBttsProbability", "SystemOver25Probability"))
                and all(probability(row[field]) for field in ("BttsProbability", "Over25Probability"))):
            model_forecasts[row["Model"]].append(row)
    ai_ids = {row["FixtureId"] for rows in model_forecasts.values() for row in rows}
    component_comparison = {}
    app_comparison = {}
    cohort_ids = {}
    for market, column in (("btts", "Btts"), ("over25", "Over25")):
        component_rows = []
        app_rows = []
        for identifier, fixture in finished.items():
            goals = goal_rows.get(identifier)
            if not goals or not goals["Historical"]:
                continue
            source_values = {"historical": goals["Historical"][column], "ml": goals["Ml"][column],
                             "historical_ml_fitted": goals["HistoricalMl"][column]}
            source_values["historical_ml_equal"] = (source_values["historical"] + source_values["ml"]) / 2
            record = {"fixture_id": identifier, "actual": outcome(fixture, market), "probabilities": source_values}
            component_rows.append(record)
            snapshot = latest.get(identifier)
            if (not snapshot or not probability(snapshot[column])
                    or timestamp(snapshot["CapturedAtUtc"]).date() != timestamp(fixture["Date"]).date()):
                continue
            app_rows.append({**record, "probabilities": {**source_values, "current_app": snapshot[column],
                                                         "current_app_ml_equal": (snapshot[column] + source_values["ml"]) / 2}})
        component_comparison[market] = compare(component_rows, "historical")
        app_comparison[market] = compare(app_rows, "current_app")
        cohort_ids[market] = {"components": [row["fixture_id"] for row in component_rows],
                             "app": [row["fixture_id"] for row in app_rows]}
    ai_comparison = []
    for model, candidate_rows in sorted(model_forecasts.items()):
        counts = collections.Counter(row["FixtureId"] for row in candidate_rows)
        candidates = [row for row in candidate_rows if counts[row["FixtureId"]] == 1]
        entry = {"model": model, "rows": len(candidates), "markets": {}, "three_source_markets": {},
                 "ai_boundary_probabilities": {}, "fixture_ids": [row["FixtureId"] for row in candidates]}
        for market, column in (("btts", "Btts"), ("over25", "Over25")):
            paired = []
            three_source = []
            for row in candidates:
                identifier = row["FixtureId"]
                fixture = finished[identifier]
                system = row["System" + column + "Probability"]
                ai = row[column + "Probability"]
                values = {"current_at_ai_capture": system, "ai_only": ai,
                          "current90_ai10": .9 * system + .1 * ai,
                          "current75_ai25": .75 * system + .25 * ai,
                          "current50_ai50": .5 * system + .5 * ai}
                paired.append({"fixture_id": identifier, "actual": outcome(fixture, market), "probabilities": values})
                goals = goal_rows.get(identifier)
                if (goals and goals["Historical"] and
                        timestamp(row["PredictedAtUtc"]).date() == timestamp(fixture["Date"]).date()):
                    three_source.append({"fixture_id": identifier, "actual": outcome(fixture, market), "probabilities": {
                        "current_at_ai_capture": system,
                        "historical_ml_fitted": goals["HistoricalMl"][column],
                        "historical_ml_ai_equal": (goals["Historical"][column] + goals["Ml"][column] + ai) / 3,
                        "historical_ml90_ai10": .9 * goals["HistoricalMl"][column] + .1 * ai,
                    }})
            entry["markets"][market] = compare(paired, "current_at_ai_capture")
            entry["three_source_markets"][market] = compare(three_source, "current_at_ai_capture")
            entry["three_source_fixture_ids"] = [row["fixture_id"] for row in three_source]
            entry["ai_boundary_probabilities"][market] = sum(row[column + "Probability"] in (0, 1) for row in candidates)
        ai_comparison.append(entry)
    return {
        "generated_at_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "from": start.isoformat(), "until_exclusive": until.isoformat(), "input_provenance": provenance,
        "ml_output_sha256": hashlib.sha256(goals_bytes).hexdigest(),
        "ml_training": {key: value for key, value in goal_report.items() if key != "Predictions"},
        "availability": {"finished_matches": len(finished), "recorded_app_matches": len(latest),
                         "stored_ml_generations": len(generations), "stored_provider_rows": len(providers),
                         "finished_provider_rows": len(provider_finished), "pre_match_provider_rows": len(provider_eligible),
                         "four_source_finished_matches": len(set(provider_eligible) & ai_ids & set(goal_rows) & set(latest))},
        "historical_ml_comparison": component_comparison, "current_app_comparison": app_comparison,
        "ai_combinations": ai_comparison, "cohort_fixture_ids": cohort_ids,
        "limitations": [
            "No four-source historical accuracy is reported without a pre-match provider record.",
            "ML is a newly reconstructed offline experiment using native LightGBM, not a historical production ML snapshot.",
            "ML trees, goal-rate scaling and historical/ML weights are fitted before the evaluation period. No model is published.",
            "All bookmaker features are masked in reconstructed components. Other historical features are reconstructed from the current export, not immutable as-of records.",
            "For comparisons against recorded forecasts, reconstructed components are included only when the recorded forecast was captured on the same UTC day as kickoff; reconstructed history freezes at day start.",
            "Fixed 10%, 25%, 50% AI blends and equal three-source weighting are sensitivity tests, not tuned production weights or an independent untouched benchmark.",
            "AI models have different fixture cohorts; compare candidates within each table, not model names across tables.",
            "Stored NVIDIA probabilities include many exact 1.0 values. The old parser clamped invalid values, and original raw responses were not retained, so model error and ingestion error cannot be separated.",
            "No betting profitability is inferred from classification accuracy."
        ]
    }


def markdown(report):
    lines = ["# Source-combination experiment", "",
             "Evaluation: 25 August–28 September 2026. Same fixtures within every comparison.", "",
             "**A historical four-source score is unavailable:** " + str(report["availability"]["pre_match_provider_rows"])
             + " finished fixtures have a provider record captured at least one hour before kickoff.", "",
             "## Native ML experiment", "",
             f"Trainer: {report['ml_training']['Trainer']}. Fitting: {report['ml_training']['TrainingRows']} matches. "
             f"Separate calibration: {report['ml_training']['CalibrationRows']} matches. "
             f"Calibration ends {report['ml_training']['CalibrationThroughUtc']}. "
             f"Learned ML weight inside the historical/ML mixture: {report['ml_training']['Calibration']['MlWeight']:.3f}.", ""]
    def table(title, markets):
        lines.extend(["### " + title, "", "| Market | Method | Matches | Accuracy | Brier | Improved / worsened calls |",
                      "|---|---|---:|---:|---:|---:|"])
        for market, candidates in markets.items():
            for name, result in candidates.items():
                lines.append(f"| {market} | {name} | {result['matches']} | {result['accuracy']:.1%} | {result['brier']:.4f} | {result['improved_calls']} / {result['worsened_calls']} |")
        lines.append("")
    table("Historical data versus ML, same reconstructed input timing", report["historical_ml_comparison"])
    table("Comparison with the current app's saved probabilities", report["current_app_comparison"])
    for comparison in report["ai_combinations"]:
        table(comparison["model"] + " — current app plus recorded AI", comparison["markets"])
        table(comparison["model"] + " — three-source experiment (provider unavailable)", comparison["three_source_markets"])
    lines += ["## Interpretation limits", ""] + ["- " + item for item in report["limitations"]]
    lines += ["", "Machine-readable results include all evaluated fixture IDs, source hashes, confidence intervals and selection counts.", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--goals", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    report = run(arguments.input_dir, arguments.goals, timestamp("2026-08-25T00:00:00+02:00"), timestamp("2026-09-29T00:00:00+02:00"))
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.with_suffix(".json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    arguments.output.with_suffix(".md").write_text(markdown(report))
    print(json.dumps({"availability": report["availability"], "current_app_comparison": report["current_app_comparison"]}, indent=2))


if __name__ == "__main__":
    main()
