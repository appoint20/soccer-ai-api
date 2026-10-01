import datetime
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "ml"))
from audit_source_combinations import compare, verified
from probe_nvidia import DEFAULT_MODEL, prepare, request_body, validate


class SourceAuditTests(unittest.TestCase):
    def test_paired_improvements_and_brier_use_identical_rows(self):
        rows = [
            {"actual": True, "probabilities": {"current": .6, "blend": .4}},
            {"actual": False, "probabilities": {"current": .7, "blend": .3}},
        ]
        results = compare(rows, "current")
        self.assertEqual(results["blend"]["matches"], 2)
        self.assertEqual(results["blend"]["accuracy"], .5)
        self.assertEqual(results["blend"]["improved_calls"], 1)
        self.assertEqual(results["blend"]["worsened_calls"], 1)
        self.assertAlmostEqual(results["blend"]["brier_difference"], -.1)

    def test_export_hash_and_row_count_are_enforced(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            content = b"[{}]"
            (path / "sample.json").write_bytes(content)
            provenance = {"Files": {"sample": {"Rows": 1, "Sha256": hashlib.sha256(content).hexdigest()}}}
            self.assertEqual(verified(path, "sample", provenance), [{}])
            provenance["Files"]["sample"]["Rows"] = 2
            with self.assertRaises(ValueError):
                verified(path, "sample", provenance)
            provenance["Files"]["sample"]["Rows"] = 1
            (path / "sample.json").write_bytes(b"[]")
            with self.assertRaises(ValueError):
                verified(path, "sample", provenance)


class NvidiaProbeTests(unittest.TestCase):
    def response(self):
        return {"fixture_id": 42, "btts_probability": .6, "over_2_5_probability": .55,
                "expected_home_goals": 1.4, "expected_away_goals": 1.2,
                "rationale": "Limited evidence.", "narration_en": "Both teams may score.",
                "narration_de": "Beide Teams könnten treffen.", "data_caveats": []}

    def test_response_requires_narrations_and_correct_fixture(self):
        result = self.response()
        self.assertEqual(validate(json.dumps(result), 42), result)
        with self.assertRaises(ValueError):
            validate(json.dumps(result), 43)
        for field in ("rationale", "narration_en", "narration_de"):
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate(json.dumps({**result, field: ""}), 42)

    def test_probabilities_are_never_clamped(self):
        for invalid in (65, -1, float("nan"), float("inf"), True, "0.6"):
            with self.subTest(value=invalid), self.assertRaises(ValueError):
                validate(json.dumps({**self.response(), "btts_probability": invalid}), 42)

    def test_request_only_allows_explicit_free_nvidia(self):
        for model in ("nvidia/paid", "openrouter/free", "google/example:free"):
            with self.assertRaises(ValueError):
                request_body(model, {})
        body = request_body(DEFAULT_MODEL, {})
        self.assertEqual(body["provider"]["max_price"], {"prompt": 0, "completion": 0})
        self.assertFalse(body["reasoning"]["enabled"])
        self.assertNotIn("models", body)

    def test_prepare_excludes_started_matches_and_future_evidence(self):
        now = datetime.datetime(2026, 9, 30, tzinfo=datetime.timezone.utc)
        snapshot = {"Id": 42, "Date": "2026-10-02T12:00:00Z", "Status": "NS",
                    "HomeTeam": "Home", "AwayTeam": "Away", "HomeStats": {}, "AwayStats": {},
                    "Prediction": {market: {"probability": .5} for market in
                                   ("btts", "over25", "home_win", "draw", "away_win")}}
        provider = {"FixtureId": 42, "CapturedAtUtc": "2026-09-29T12:00:00Z"}
        goals = {"GeneratedAtUtc": "2026-09-29T12:00:00Z", "Report": {"Predictions": [{
            "FixtureId": 42, "KickoffUtc": snapshot["Date"], "IsFinished": False,
            "Historical": {"Btts": .5}, "Ml": {"Btts": .55}, "MlHomeGoals": 1.2, "MlAwayGoals": 1.2}]}}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            goals_path = path / "goals.json"
            goals_path.write_text(json.dumps(goals))

            def write_inputs():
                data = {"forecast-inputs": [{"FixtureId": 42, "UpdatedAt": "2026-09-29T12:00:00Z",
                                             "SnapshotJson": json.dumps(snapshot)}],
                        "provider-predictions": [provider]}
                provenance = {"Files": {}}
                for name, rows in data.items():
                    content = json.dumps(rows).encode()
                    (path / (name + ".json")).write_bytes(content)
                    provenance["Files"][name] = {"Rows": len(rows), "Sha256": hashlib.sha256(content).hexdigest()}
                (path / "export-provenance.json").write_text(json.dumps(provenance))

            write_inputs()
            self.assertEqual(len(prepare(path, goals_path, now, 2)[0]), 1)
            self.assertEqual(prepare(path, goals_path, now + datetime.timedelta(days=3), 2)[0], [])
            provider["CapturedAtUtc"] = "2026-10-01T12:00:00Z"
            write_inputs()
            self.assertEqual(prepare(path, goals_path, now, 2)[0], [])


if __name__ == "__main__":
    unittest.main()
