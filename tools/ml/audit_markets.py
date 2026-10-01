"""Score frozen pre-match records without training, provider calls, or database writes."""
import argparse
import collections
import datetime as datetime
import hashlib
import json
import math
from pathlib import Path
from zoneinfo import ZoneInfo


def timestamp(value):
    if not value or value in ("-infinity", "infinity"):
        return None
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))


def probability(value, strict=False):
    return isinstance(value, (int, float)) and math.isfinite(value) and (
        0 < value < 1 if strict else 0 <= value <= 1
    )


def interval(hits, count):
    if not count:
        return None
    quantile = 1.959963984540054
    rate = hits / count
    denominator = 1 + quantile ** 2 / count
    center = (rate + quantile ** 2 / (2 * count)) / denominator
    half = quantile * math.sqrt(rate * (1 - rate) / count + quantile ** 2 / (4 * count ** 2)) / denominator
    return [max(0, center - half), min(1, center + half)]


def picks(outcomes):
    count = len(outcomes)
    hits = sum(outcomes)
    return {"picks": count, "wins": hits, "losses": count - hits,
            "hit_rate": hits / count if count else None, "wilson95": interval(hits, count)}


def metrics(rows):
    count = len(rows)
    correct = sum((estimate >= .5) == actual for estimate, actual in rows)
    return {
        "matches": count, "correct": correct,
        "accuracy": correct / count if count else None,
        "accuracy_wilson95": interval(correct, count),
        "brier": sum((estimate - actual) ** 2 for estimate, actual in rows) / count if count else None,
        "log_loss": sum(-math.log(max(1e-15, min(1, estimate if actual else 1 - estimate)))
                        for estimate, actual in rows) / count if count else None,
        "mean_probability": sum(estimate for estimate, _ in rows) / count if count else None,
        "actual_yes_rate": sum(actual for _, actual in rows) / count if count else None,
        "yes_predictions": picks([actual for estimate, actual in rows if estimate >= .5]),
        "no_predictions": picks([not actual for estimate, actual in rows if estimate < .5]),
    }


def outcome(fixture, market):
    if market == "btts":
        return fixture["HomeGoal"] > 0 and fixture["AwayGoal"] > 0
    return fixture["HomeGoal"] + fixture["AwayGoal"] > 2


def load_export(directory):
    provenance = json.loads((directory / "export-provenance.json").read_text())
    loaded = {}
    for name in ("fixtures", "prediction-snapshots", "model-forecasts", "sync-state"):
        entry = provenance["Files"].get(name)
        if entry is None:
            raise ValueError(f"Missing export provenance: {name}")
        payload = (directory / f"{name}.json").read_bytes()
        if hashlib.sha256(payload).hexdigest() != entry["Sha256"]:
            raise ValueError(f"Checksum mismatch: {name}")
        loaded[name] = json.loads(payload)
        if len(loaded[name]) != entry["Rows"]:
            raise ValueError(f"Row count mismatch: {name}")
    return provenance, loaded


def build_report(directory, end_date, weeks, timezone):
    provenance, data = load_export(directory)
    until = datetime.datetime.combine(end_date, datetime.time(), ZoneInfo(timezone))
    start = until - datetime.timedelta(weeks=weeks)
    fixtures = {row["Id"]: row for row in data["fixtures"] if start <= timestamp(row["Date"]) < until}
    finished = {key: row for key, row in fixtures.items() if row["Status"] == "FT"
                and row["HomeGoal"] is not None and row["AwayGoal"] is not None
                and row["HomeGoal"] >= 0 and row["AwayGoal"] >= 0}
    records = collections.defaultdict(list)
    for row in data["prediction-snapshots"]:
        fixture = finished.get(row["FixtureId"])
        if fixture is None:
            continue
        kickoff = timestamp(fixture["Date"])
        captured = timestamp(row["CapturedAtUtc"])
        if (captured and timestamp(row["KickoffUtc"]) == kickoff
                and captured <= kickoff - datetime.timedelta(hours=1)):
            records[fixture["Id"]].append(row)
    pairs = []
    invalid = 0
    for fixture_id, candidates in records.items():
        snapshot = sorted(candidates, key=lambda row: (-timestamp(row["CapturedAtUtc"]).timestamp(), row["Id"]))[0]
        if not all(probability(snapshot[field]) for field in ("Btts", "Over25")):
            invalid += 1
            continue
        pairs.append((finished[fixture_id], snapshot))
    pairs.sort(key=lambda pair: (timestamp(pair[0]["Date"]), pair[0]["Id"]))

    def market_summary(subset, market):
        column = "Btts" if market == "btts" else "Over25"
        result = metrics([(snapshot[column], outcome(fixture, market)) for fixture, snapshot in subset])
        qualified = []
        priced = []
        audited = 0
        ai_verified = 0
        for fixture, snapshot in subset:
            context = json.loads(snapshot["ContextJson"])
            audit = context.get("audit") or {}
            computed = timestamp(audit.get("computed_at_utc"))
            captured = timestamp(snapshot["CapturedAtUtc"])
            if not computed or computed > captured:
                continue
            decisions = [row for row in audit.get("markets", []) if row["market"] == market]
            if len(decisions) != 1:
                continue
            decision = decisions[0]
            audited += 1
            ai = context.get("ai") or {}
            generated = timestamp(ai.get("GeneratedAtUtc"))
            if (ai.get("HasDecisionLayer") and generated and generated <= captured
                    and generated < timestamp(fixture["Date"])
                    and all(ai.get(key) for key in ("ModelVersion", "PromptHash", "InputHash"))):
                ai_verified += 1
            if not decision.get("qualified"):
                continue
            actual = outcome(fixture, market)
            qualified.append(actual)
            odds = decision.get("odds")
            odds_time = timestamp(context.get("odds_updated_at"))
            if (context.get("live_odds") and odds_time and odds_time <= captured
                    and isinstance(odds, (int, float)) and math.isfinite(odds) and 1.01 <= odds <= 15):
                priced.append(odds - 1 if actual else -1)
        result["recorded_decision_fixtures"] = audited
        result["verified_ai_decision_fixtures"] = ai_verified
        result["qualified_selections"] = picks(qualified)
        result["qualified_selections"]["coverage"] = len(qualified) / audited if audited else None
        result["priced_qualified_selections"] = {
            "count": len(priced), "unit_stake": 1,
            "profit_units": sum(priced) if priced else None,
            "roi": sum(priced) / len(priced) if priced else None,
        }
        return result

    weekly = []
    for index in range(weeks):
        week_start = start + datetime.timedelta(weeks=index)
        week_end = week_start + datetime.timedelta(weeks=1)
        subset = [pair for pair in pairs if week_start <= timestamp(pair[0]["Date"]) < week_end]
        eligible = sum(week_start <= timestamp(row["Date"]) < week_end for row in finished.values())
        weekly.append({"from": week_start.date().isoformat(),
                       "through": (week_end.date() - datetime.timedelta(days=1)).isoformat(),
                       "finished_matches": eligible, "recorded_matches": len(subset),
                       "markets": {market: market_summary(subset, market) for market in ("btts", "over25")}})
    forecasts = collections.defaultdict(list)
    for row in data["model-forecasts"]:
        if row["FixtureId"] in fixtures:
            forecasts[row["Model"]].append(row)
    ai_comparisons = []
    for model, rows in sorted(forecasts.items()):
        eligible = []
        excluded = collections.Counter()
        counts = collections.Counter(row["FixtureId"] for row in rows)
        for row in rows:
            fixture = finished.get(row["FixtureId"])
            if counts[row["FixtureId"]] != 1:
                excluded["duplicate_fixture_model"] += 1
            elif fixture is None:
                excluded["not_finished"] += 1
            elif (timestamp(row["KickoffUtc"]) != timestamp(fixture["Date"])
                  or not timestamp(row["PredictedAtUtc"])
                  or timestamp(row["PredictedAtUtc"]) > timestamp(fixture["Date"]) - datetime.timedelta(hours=1)):
                excluded["invalid_timing"] += 1
            elif not all(probability(row[field], strict=True) for field in ("SystemBttsProbability", "SystemOver25Probability")):
                excluded["missing_system_inputs"] += 1
            elif not all(probability(row[field]) for field in ("BttsProbability", "Over25Probability")):
                excluded["invalid_ai_probability"] += 1
            else:
                eligible.append((fixture, row))
        comparison = {"model": model, "records": len(rows), "paired_matches": len(eligible), "excluded": dict(excluded), "markets": {}}
        for market, column in (("btts", "BttsProbability"), ("over25", "Over25Probability")):
            comparison["markets"][market] = {
                "statistical": metrics([(row["System" + column], outcome(fixture, market)) for fixture, row in eligible]),
                "ai": metrics([(row[column], outcome(fixture, market)) for fixture, row in eligible]),
            }
        ai_comparisons.append(comparison)
    return {
        "generated_at_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "from": start.isoformat(), "until_exclusive": until.isoformat(), "timezone": timezone,
        "protocol": "Latest immutable snapshot at least one hour before actual kickoff; one per FT fixture. Both market probabilities must be finite in [0,1]. No historical forecasts regenerated. Classification uses p>=0.5. Qualified=false is an abstention, not a No prediction. Only pre-capture decision audits count for selections. ROI uses unit stakes on qualified selections with recorded fresh odds; no imputed odds.",
        "input_provenance": provenance, "finished_matches": len(finished), "recorded_matches": len(pairs),
        "missing_pre_match_snapshot": len(finished) - len(records), "invalid_snapshot_probabilities": invalid,
        "model_versions": dict(collections.Counter(snapshot["ModelVersion"] for _, snapshot in pairs)),
        "markets": {market: market_summary(pairs, market) for market in ("btts", "over25")},
        "fixture_statuses": dict(collections.Counter(row["Status"] for row in fixtures.values())),
        "weeks": weekly, "ai_forecasts": ai_comparisons,
        "scored_records": [{"fixture_id": fixture["Id"], "snapshot_id": snapshot["Id"],
                            "kickoff": fixture["Date"], "captured_at": snapshot["CapturedAtUtc"],
                            "home_goals": fixture["HomeGoal"], "away_goals": fixture["AwayGoal"],
                            "btts": snapshot["Btts"], "over25": snapshot["Over25"]} for fixture, snapshot in pairs],
        "limitations": ["Coverage is limited to fixtures and genuine pre-match records in the exported database.",
                        "Versions and selection rules changed during the period; these are recorded-system results, not validation of one fixed new model.",
                        "AI comparisons use the same fixtures within each model, but different models may have different fixture cohorts.",
                        "AI saw statistical probabilities, so it is not an independent voter; the forecast ledger lacks prompt/input hashes.",
                        "Nominal Wilson intervals do not account for dependence between fixtures.",
                        "Priced selections are a subset; their ROI cannot establish profitability across unpriced selections."]
    }


def percent(value):
    return "—" if value is None else f"{value:.1%}"


def markdown(report):
    lines = ["# Five-week BTTS and Over 2.5 backtest", "",
             f"Period: **{report['from'][:10]} through {(timestamp(report['until_exclusive']).date() - datetime.timedelta(days=1)).isoformat()}**, {report['timezone']}.", "",
             f"**{report['recorded_matches']} scored fixtures out of {report['finished_matches']} finished fixtures**. "
             f"{report['missing_pre_match_snapshot']} have no eligible saved pre-match prediction. "
             f"{report['invalid_snapshot_probabilities']} have invalid probabilities.", "",
             "Source: read-only PostgreSQL export, captured " + report["input_provenance"]["CapturedAtUtc"] + ".", "",
             "## Recorded prediction performance", "",
             "Accuracy scores both Yes and No at 50%. Yes hit rate scores only Yes forecasts; it is not the selected-bet win rate.", "",
             "| Market | Matches | Correct | Accuracy | 95% interval | Yes wins / calls | Yes hit rate | Always-Yes accuracy | Brier |",
             "|---|---:|---:|---:|---|---:|---:|---:|---:|"]
    for market, label in (("btts", "Goal Goal / BTTS"), ("over25", "Over 2.5")):
        result = report["markets"][market]
        bounds = result["accuracy_wilson95"]
        uncertainty = "—" if bounds is None else "–".join(percent(value) for value in bounds)
        yes = result["yes_predictions"]
        brier = "—" if result["brier"] is None else f"{result['brier']:.4f}"
        lines.append(f"| {label} | {result['matches']} | {result['correct']} | {percent(result['accuracy'])} | {uncertainty} | {yes['wins']} / {yes['picks']} | {percent(yes['hit_rate'])} | {percent(result['actual_yes_rate'])} | {brier} |")
    lines += ["", "Lower Brier is better. A constant 50% forecast scores 0.25.", "", "## Recorded selections", "",
              "Selections use the saved decision after its evidence gates. Skipped markets are abstentions. ROI is a flat-stake simulation using recorded prices, not actual account or published-ticket returns.", "",
              "| Market | Audited matches | Selections | Wins | Losses | Hit rate | Fresh-priced selections | Profit (1-unit stake) | ROI |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for market in ("btts", "over25"):
        result = report["markets"][market]
        selected = result["qualified_selections"]
        priced = result["priced_qualified_selections"]
        profit = "—" if priced["profit_units"] is None else f"{priced['profit_units']:+.2f}"
        lines.append(f"| {market} | {result['recorded_decision_fixtures']} | {selected['picks']} | {selected['wins']} | {selected['losses']} | {percent(selected['hit_rate'])} | {priced['count']} | {profit} | {percent(priced['roi'])} |")
    lines += ["", "## Week by week", "", "| Week | Finished | Scored | BTTS accuracy | BTTS Yes hit rate | Over 2.5 accuracy | Over Yes hit rate |",
              "|---|---:|---:|---:|---:|---:|---:|"]
    for week in report["weeks"]:
        btts = week["markets"]["btts"]
        over = week["markets"]["over25"]
        lines.append(f"| {week['from']}–{week['through']} | {week['finished_matches']} | {week['recorded_matches']} | {percent(btts['accuracy'])} | {percent(btts['yes_predictions']['hit_rate'])} | {percent(over['accuracy'])} | {percent(over['yes_predictions']['hit_rate'])} |")
    lines += ["", "## AI forecast comparison", "", "Each row compares AI and its stored statistical inputs on exactly the same fixtures. Models cannot be ranked across different cohorts.", "",
              "| Recorded model | Paired matches | BTTS: statistics → AI | Over 2.5: statistics → AI | BTTS Brier: statistics → AI | Over Brier: statistics → AI |",
              "|---|---:|---:|---:|---:|---:|"]
    for comparison in report["ai_forecasts"]:
        cells = []
        for metric in ("accuracy", "brier"):
            for market in ("btts", "over25"):
                result = comparison["markets"][market]
                values = [result[source][metric] for source in ("statistical", "ai")]
                cells.append(" → ".join(percent(value) if metric == "accuracy" else "—" if value is None else f"{value:.4f}" for value in values))
        lines.append(f"| {comparison['model']} | {comparison['paired_matches']} | " + " | ".join(cells) + " |")
    lines += ["", "## Method and limits", "", report["protocol"], "",
              "Scored versions: " + ", ".join(f"{name} ({count})" for name, count in report["model_versions"].items()) + ".", ""]
    lines += ["- " + limitation for limitation in report["limitations"]]
    lines += ["", "The adjacent JSON report contains weekly counts, selection confidence intervals, mean probabilities, base rates, exclusions, source hashes, and the exact scored fixture/snapshot IDs.", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--end", required=True, type=datetime.date.fromisoformat)
    parser.add_argument("--weeks", type=int, default=5)
    parser.add_argument("--timezone", default="Europe/Berlin")
    arguments = parser.parse_args()
    if arguments.weeks < 1:
        parser.error("--weeks must be positive")
    report = build_report(arguments.input_dir, arguments.end, arguments.weeks, arguments.timezone)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.with_suffix(".json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    arguments.output.with_suffix(".md").write_text(markdown(report))
    print(json.dumps({key: report[key] for key in ("from", "until_exclusive", "finished_matches", "recorded_matches", "model_versions", "markets")}, indent=2))


if __name__ == "__main__":
    main()
