import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("verify_combined_predictions", Path(__file__).resolve().parents[2] / "tools/e2e/verify_combined_predictions.py")
verify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify)


def complete_job(fixture_id=1):
    markets = {"home_win": .45, "draw": .3, "away_win": .25, "btts": .6,
               "over25": .65, "two_to_three_goals": .5, "btts_and_over25": .4, "expected_goals": 2.8}
    captured = "2030-01-01T10:00:01+00:00"
    sources = [{"name": name, "markets": copy.deepcopy(markets), "outcome_weight": weight, "goals_weight": weight,
                "captured_at_utc": captured, "model_version": verify.MODEL if name == "ai" else "test",
                "rationale": "Controlled test input"}
               for name, weight in zip(("historical", "ml", "provider", "ai"), (.45, .3, .15, .1))]
    return {"state": "completed", "started_at_utc": "2030-01-01T10:00:00+00:00", "result": {
        "fixture_id": fixture_id, "snapshot_id": "test-version", "narration_refreshed": True, "prediction": {
            "fixture_id": fixture_id, "status": "complete", "all_four_sources_available": True, "sources": sources,
            "captured_at_utc": captured, "kickoff_utc": "2030-01-02T10:00:00+00:00", "markets": markets}}}


def analysis(combined):
    return {"success": True, "data": {"match": {
        "id": combined["fixture_id"], "combined_prediction": copy.deepcopy(combined),
        "prediction": {**{market: {"probability": combined["markets"][market]} for market in verify.OUTCOMES + verify.GOALS[:3]},
                       "match_winner": {"prediction": "home"}}, "match_prediction": {"prediction": "home"},
        "ai": {"analysis": "Fresh English analysis", "generated_at_utc": "2030-01-01T10:00:02+00:00", "model_version": verify.MODEL}}}}


class StagingVerifierTests(unittest.TestCase):
    def test_complete_weighted_prediction_and_saved_analysis_pass(self):
        combined = verify.validate_job(complete_job(), 1)
        result = verify.validate_analysis(analysis(combined), combined)
        self.assertEqual(result["match_prediction"], "home")

    def test_partial_or_failed_narration_fails_even_with_valid_markets(self):
        for field, value in (("state", "completed_with_warnings"), ("narration_refreshed", False), ("all_four_sources_available", False)):
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                job = complete_job()
                target = job if field == "state" else job["result"] if field == "narration_refreshed" else job["result"]["prediction"]
                target[field] = value
                verify.validate_job(job, 1)

    def test_nonfinite_and_inconsistent_market_probabilities_fail(self):
        for value in (65, float("nan"), float("inf"), None):
            with self.subTest(value=value), self.assertRaises(verify.VerificationError):
                job = complete_job()
                job["result"]["prediction"]["markets"]["btts"] = value
                verify.validate_job(job, 1)

    def test_wrong_weighted_result_or_nvidia_model_fails(self):
        job = complete_job()
        job["result"]["prediction"]["markets"]["btts"] = .61
        with self.assertRaises(verify.VerificationError):
            verify.validate_job(job, 1)
        job = complete_job()
        job["result"]["prediction"]["sources"][-1]["model_version"] = "different/model:free"
        with self.assertRaises(verify.VerificationError):
            verify.validate_job(job, 1)

    def test_stale_or_wrong_language_analysis_is_not_fresh_success(self):
        combined = verify.validate_job(complete_job(), 1)
        for field, value in (("analysis", ""), ("generated_at_utc", "2029-01-01T00:00:00+00:00"), ("model_version", "other:free")):
            with self.subTest(field=field), self.assertRaises(verify.VerificationError):
                response = analysis(combined)
                response["data"]["match"]["ai"][field] = value
                verify.validate_analysis(response, combined)

    def test_outdated_saved_blend_and_wrong_winner_fail(self):
        combined = verify.validate_job(complete_job(), 1)
        response = analysis(combined)
        response["data"]["match"]["combined_prediction"]["captured_at_utc"] = "2029-01-01T00:00:00+00:00"
        with self.assertRaises(verify.VerificationError):
            verify.validate_analysis(response, combined)
        response = analysis(combined)
        response["data"]["match"]["match_prediction"]["prediction"] = "away"
        with self.assertRaises(verify.VerificationError):
            verify.validate_analysis(response, combined)

    def test_remote_credentials_cannot_follow_redirects_or_cross_origin_poll(self):
        client = verify.StagingClient("https://stage.example", "stage.example", "test-admin-key-123456")
        for path in ("https://attacker.example/api/jobs", "//attacker.example/api/jobs", "/not-api"):
            with self.subTest(path=path), self.assertRaises(verify.VerificationError):
                client.request(path)
        with self.assertRaises(verify.VerificationError):
            verify.NoRedirects().redirect_request(None, None, 302, "", {}, "https://attacker.example")
        with self.assertRaises(verify.VerificationError):
            verify.StagingClient("https://production.example", "stage.example", "test-admin-key-123456")
        with self.assertRaises(verify.VerificationError):
            verify.StagingClient("http://stage.example", "stage.example", "test-admin-key-123456")

    def test_live_runner_unwraps_envelopes_and_preserves_failed_job_evidence(self):
        job = complete_job()
        job["state"] = "completed_with_warnings"
        replies = iter([(401, {}), (202, {"success": True, "data": {"poll": "/api/automation/predictions/jobs/test"}}),
                        (200, {"success": True, "data": job})])
        class Client:
            def request(self, *args, **kwargs):
                return next(replies)
        reports = []
        with patch.object(verify.time, "sleep"):
            verify.run(Client(), [1, 2, 3], 10, reports)
        self.assertEqual(len(reports), 1)
        self.assertFalse(reports[0]["passed"])
        self.assertEqual(reports[0]["job"], job)
        self.assertIn("all requested sources", reports[0]["error"])

    def test_live_runner_verifies_three_fixtures_without_repeating_writes(self):
        requests = []
        class Client:
            def request(self, path, method="GET", authenticated=True):
                requests.append((path, method, authenticated))
                if not authenticated:
                    return 401, {}
                if method == "POST":
                    fixture_id = int(path.split("/")[4])
                    return 202, {"success": True, "data": {"poll": f"/api/automation/predictions/jobs/{fixture_id}"}}
                if "/jobs/" in path:
                    return 200, {"success": True, "data": complete_job(int(path.split("/")[-1]))}
                fixture_id = int(path.split("/")[-1].split("?")[0])
                payload = analysis(complete_job(fixture_id)["result"]["prediction"])
                if path.endswith("language=de"):
                    payload["data"]["match"]["ai"]["analysis"] = "Neue deutsche Analyse"
                return 200, payload
        reports = []
        with patch.object(verify.time, "sleep"):
            verify.run(Client(), [1, 2, 3], 10, reports)
        self.assertEqual(len(reports), 3)
        self.assertTrue(all(item["passed"] for item in reports))
        self.assertEqual(sum(method == "POST" and authenticated for _, method, authenticated in requests), 3)

    def test_failed_authorization_probe_stops_before_authenticated_writes(self):
        class Client:
            def request(self, path, method="GET", authenticated=True):
                if authenticated:
                    raise AssertionError("Must not send an authenticated request")
                return 404, {}
        reports = []
        with self.assertRaises(verify.VerificationError):
            verify.run(Client(), [1, 2, 3], 10, reports)
        self.assertEqual(reports, [])


if __name__ == "__main__":
    unittest.main()
