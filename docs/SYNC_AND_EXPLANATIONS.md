# Provider cadence and readable market explanations

## Why Python is present

The API, worker, prediction combiner and scheduler run on .NET. Python under `tools/ml`, `tools/e2e`, `tests/ml` and `tests/e2e` is for offline backtesting, source comparisons and smoke-test tooling. It is not a second production API or a scheduled Python sync process.

## Two-hour provider polling

Repository defaults and the checked-in worker, API, Render and Compose configurations now use:

| Activity | Previous worker behavior | New configuration |
|---|---|---|
| Main fixture/standings/results pipeline | Every 30 minutes | Every 120 minutes; UTC 00:20, 02:20, …, 22:20 |
| Odds capture loop | Every 15 minutes | Every 120 minutes |
| Minimum age before re-fetching odds | 1 hour, shortened by lookahead | Full 2 hours, including the final approach to kickoff |
| Live scores, when tracked matches are in play | Every 60 seconds | Every 7,200 seconds |
| Picked-match live statistics | Every 5 minutes when due | Every 120 minutes when due |
| Optional combined-prediction scheduler | Every 15 minutes, disabled | Every 120 minutes, still disabled |

Injuries retain their slower six-hour freshness rule. Provider predictions and head-to-head captures retain their existing eligibility/deduplication rules. No scheduler has been newly enabled.

The old five-minute statistics setting really could cause API-Football requests, but only for eligible picked matches. The five-minute delay remaining in `SyncWorker` is a failure cooldown followed by a wait for the next scheduled slot, **not** a five-minute provider sync.

Normal iOS `/api/analyze` reads do not request a provider refresh. Its three-minute foreground cache reads and 30-second local odds-age checks are not API-Football polling. Explicit administrator refresh endpoints and startup catch-up are separate actions.

**Trade-offs:** live scores/statistics can now lag by up to roughly two hours or longer after failures. A pipeline pass makes multiple provider requests; twelve scheduled passes per day does not mean twelve API calls per day. Manual requests, startup catch-up, retries, multiple replicas and other services sharing a key add usage. This change reduces frequency; it is not a global daily quota guarantee.

The optional combined scheduler keeps its five-attempt daily cap. Its final-refresh window is widened to T−4h through T−30m so a two-hour polling interval has room to observe it. Slow passes, missing prerequisites and daily caps can still leave fixtures unserved.

These are repository changes, not a deployment. Apply them to the intended service and remove/update stale environment overrides during the normal release process. Check effective worker schedule logs and provider quota afterward.

## Presentation contract

- `presentation.markets[].checks` contains one to five points, not necessarily five.
- Actual fired rejection reasons lead; useful supporting evidence follows; available recent goal averages fill remaining space.
- The API and iOS both enforce five as an absolute limit. iOS removes obsolete generic counts and percentage ratings from cached/older responses before applying the limit, preserving each remaining point's original outcome marker.
- Home/away scoring counts and direct-meeting goal counts are named with their sample, e.g. “In 4 der letzten 5 direkten Duelle trafen beide Teams.”
- Provider comparison scores such as attack 60% and goals 53% are not expected-goal estimates and no longer appear as decision reasons.
- German fallback explanations work without AI. A fired scoreless/clean-sheet rule explains the actual objection rather than merely reporting that a veto exists.
- The full internal decision audit stays intact; this is not a change to probabilities, model weights or qualification rules.
- AI rewrites only the selected facts, in the same order, preserving team names and numerical evidence. Unsupported numbers, removed measurements, generic counters, percentage ratings, duplicates and excessive lists are rejected.
- Explanation contract version 3 invalidates older wording. On cache reads, outdated/invalid AI explanations are removed from the response and replaced with current deterministic evidence, without making a provider call. Future eligible narration jobs can regenerate AI text; finished matches use the readable fallback.
- The technical threshold/confirmation/veto footer is removed from iOS. The probability and quote fields remain separate from reasons.
- iOS reads `match_prediction` (with a legacy `prediction.match_winner` fallback) for the overall home/draw/away call, separately from BTTS, Over 2.5 and exactly 2–3 goals. Partial four-source availability is explicitly labeled.

## Verification

On 2026-10-01, 847 backend tests passed with no skips (including local PostgreSQL/HTTP end-to-end tests), and 52 iOS tests passed on the iPhone 17 simulator. Logs: `/tmp/soccer-readable-full-tests.log` and `/tmp/soccer-readable-ios-tests.log`. No live API-Football or NVIDIA requests were needed for these regression tests.

The backend regression suite covers bilingual reasons, five-point limits, rejection priority, AI validation, old cached payloads, odds request spacing, schedules and the actual HTTP refresh/read path using controlled providers. PostgreSQL tests use a disposable localhost database, never staging/production.

The iOS simulator suite covers old/new payloads, home/draw/away integration, retained goal-market cards, count/rating filtering, outcome-marker alignment and read-only frontend endpoints. These tests do not establish live NVIDIA availability or improved betting accuracy.
