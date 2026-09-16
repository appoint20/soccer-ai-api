# Prediction integrity audit and implementation notes

Completed 11 September 2026 against the surviving `soccer-ai-api` and native `soccer-ai-ios` projects. No production deployment or production database migration was performed in this work. Other concurrent workspace edits, including hourly worker scheduling and match presentation, were preserved.

## Findings that can be measured

The fixture export contains **24,481 fixtures, 24,127 FT results**. Its last FT kickoff is **2 May 2026**, so it cannot establish current-season or production accuracy. It contains **zero provider-observed xG values** in the new `HomeObservedXg`/`AwayObservedXg` fields. 9,605 FT fixtures have both odds provenance timestamps preceding kickoff; this does not guarantee complete coverage of all markets or every historical forecast horizon.

The chronological audit evaluates **16,261 matches** across eight later test blocks, from 13 August 2023 to 2 May 2026. Each block has separate training, calibration and test days. Model calibration rows are excluded from fitting the trees. Features exclude the target result, rebuild Elo chronologically, exclude same-day result feedback, and require pre-kickoff provenance for historical odds. Observed xG is distinguished from legacy synthetic xG. The exported fixture hash is `c27b9f4f37f3ac95512deb5d2a9a74032afe98ad0ec2493f1bbc83a4b045d1ec`.

**The trainer on this Mac was FastTreeTweedie**, because ML.NET's native LightGBM dependency could not load. This is a fallback experiment, not a measurement of the production LightGBM model, the complete app's calibrated/confluence decisions, or executed bets.

| Offline metric | Goal-rate fallback | Dixon–Coles benchmark |
| --- | ---: | ---: |
| Over/Under 2.5 accuracy at 0.5 | 54.29% | 54.84% |
| GG/No GG accuracy at 0.5 | 53.00% | 54.26% |
| Over 2.5 Brier score, lower is better | 0.2512 | 0.2467 |
| GG Brier score, lower is better | 0.2510 | 0.2474 |

The candidate **failed publication**. The strengthened gate requires at least 500 out-of-fold forecasts and both Brier score and log loss no worse than the historical frequency prior AND the Dixon–Coles benchmark for both markets. A completed rejected evaluation also respects the retraining interval, avoiding an expensive retry at every sync.

At probability >=0.80, Over 2.5 hit 70.12% on 164 matches; GG hit 61.29% on 31. Those are descriptive threshold slices, not thresholds selected and independently verified for production. Four Over 2.5 predictions above 0.90 all winning is still only four observations; the nominal lower confidence bound is about 51%. **No evidence here supports an 80% accuracy or profitability claim.** No candidate model was published by the offline command.

Retrospective limitations remain: historical rows may contain corrected/backfilled statistics, not the exact payload available at the original forecast time; the audit assumes prior completed-match statistics were available by the following UTC day. Bookmaker coverage changes over time. These limitations are why recorded live forecasts, recorded odds, and future shadow evaluation are required in addition to offline tests.

## Weekend hypothesis

`weekend-audit-2026-09-09.json` tests a fixed rule: before a Sunday fixture, at least three Friday/Saturday matches in its league have finished and fewer than half were Over 2.5/GG. An expanding league frequency is computed from matches before Friday; logistic baseline and trigger coefficients are fitted before 1 July 2025 and frozen for the later period. The study uses the Europe/Berlin calendar and a three-hour completion allowance. No fixed quota of goals or exactly eleven matches is assumed.

There are **3,700 training and 1,027 test Sunday matches**. The Over 2.5 trigger occurred for 375 test matches, with a 50.93% Over rate; the GG trigger occurred for 350, with a 49.71% GG rate. Across the full test set, adding the trigger changed Over Brier score from 0.2499998 to 0.2499951 and worsened GG from 0.2476764 to 0.2479487. Both week-cluster bootstrap intervals include zero improvement. The rule was **not added to live predictions**. This observational diagnostic is not proof of independence, a causal conclusion, or a profitable strategy.

## What now works in the code

- Live odds: collection and bookmaker update timestamps both must be within three hours, ordered correctly, and precede kickoff. A missing market is withdrawn from the live fixture while historical quote rows remain available. Prices below **1.70** withdraw old value/high-confidence picks and combo legs. EV and Kelly are recomputed from the latest price; a favorable price change alone does not invent missing confluence. Old audits without a recorded Kelly multiplier expose no recalculated stake.
- Refreshing: odds loop ticks every 30 minutes within a 72-hour horizon; regular fixtures are due on a three-hour policy, with one tick of look-ahead to avoid overshooting. Inside six hours of kickoff they are checked each tick. The **newer concurrent configuration currently runs full sync hourly** (`IntervalMinutes=60`, anchor :20), although the original requested full-sync interval was three hours. Set that scalar to 180 and startup threshold to 3 if restoring the original cadence; explicit UTC schedule remains supported. Long runs, provider outages and quota exhaustion can delay acquisition; freshness filtering prevents those delays from being presented as live prices.
- Native caching: high picks, ticket legs and builder choices check provenance and the 1.70 floor. One stale leg withdraws its whole ticket. Active screens recheck local expiry every 30 seconds, fetch the app API every three minutes, and refresh on foreground return. This does not call the football provider directly.
- Same-fixture combinations with multiplied marginal odds are excluded from the live ticket flow when there is no genuine combined quote. Cross-fixture products still reflect observed leg prices and are not guaranteed executable bookmaker tickets; best prices can come from different bookmakers. A bookmaker-consistent ticket quote is a remaining product improvement.
- Immutable prediction ledger: one capture per fixture per UTC three-hour window, only while upcoming and within seven days. Stores raw and final probabilities, selected sides, kickoff, model version and odds context. Duplicate captures do not inflate sample counts. Model provenance records the actual fallback or hybrid used.
- Calibration: mutable `FixtureAnalyses` rows no longer train isotonic maps. Only recorded pre-match raw forecasts, with the same model version and finished before the evaluation week, qualify. Different databases/model generations do not share fitted maps. The minimum sample gate remains active; insufficient history passes through rather than fabricating calibration.
- Native **Bilanz → Statistik**: 30/90/365-day views, market accuracy, Yes/No precision, misses, nominal Wilson intervals, league comparisons and error categories. Overall league score equally weights four markets; it is not ROI. `/api/statistics` uses the last recorded prediction at least one hour before actual kickoff, one per FT fixture. Rescheduled mismatches and missing history are excluded and counted. New live statistics can initially be empty; old predictions are not manufactured from post-result caches. Hard-coded measured “80%” bands were removed.
- Data collection: batched fixture details/events, observed xG plus missingness/sample features, timestamped injury observations near kickoff, and odds history. Optional enrichment stops when reported daily quota reaches the final 10%; minute quota affects spacing. This is a provider-header tracker per process, not a distributed hard 7,600-call reservation system. It does not aim to spend the allowance for its own sake. Injury collection is available for future feature work; an injury-derived model uplift has not been demonstrated.
- Subscription correctness: premium access no longer survives entitlement loss through a persisted local flag. StoreKit product IDs and transaction state are checked; the proof helper returns signed JWS. “Manage” opens Apple's subscription management, and unavailable products no longer show an invented price. **Server-side Apple verification and paid API authorization remain launch work**; these fixes do not claim that commercial billing is end-to-end ready.

GG and Over 2.5 are different targets: 3–0 is Over without GG; 1–1 is GG without Over. The error categories identify groups to investigate, not causes established by one result. The offline report's `ErrorsAtHalf` and future statistics make these groups countable. No language-model explanation should be presented as a proven causal account.

## Next experiments that could produce real accuracy gains

1. Run the worker against its intended current data source and inspect coverage by league, date, observed xG, odds age and lineup/injury availability. Prioritize accurate recent results and timed odds, then backfill provider-observed historical statistics where supported. Spending 7,000 requests without a coverage target is not an ML improvement.
2. Run the same isolated audit on a host with working native LightGBM. Compare richer input groups one at a time against the same chronological benchmark, including log loss, Brier score and sample coverage. Freeze all choices before a final later holdout. Do not tune repeatedly on the final test and then describe it as unseen.
3. Build injury/lineup features only from reports actually available at prediction time, with explicit missingness. Avoid treating a missing report as “all players fit.” Preserve quotes at fixed forecast horizons so odds movement is comparable across matches.
4. Rank league/market specializations only after adequate samples and uncertainty checks. Use shadow forecasts and abstention when the model is poorly supported. A higher hit rate achieved by publishing almost nothing must disclose its coverage.
5. Evaluate profitable execution separately: observed and obtainable price, bookmaker, timing, closing-line comparison, voids, returns, and ticket-level dependence. High classification accuracy alone does not establish an economic edge.

## Reproduction and validation

Run from `/Users/shivm/Workspace/soccer-ai-api`. Use a quiescent SQLite snapshot; the exporter refuses an active WAL. It reads fixture columns only and never migrates the source database.

```sh
python3 tools/ml/export_fixtures.py /path/to/soccer.db /tmp/fixtures.json
dotnet run --project src/soccer-ai-tools -- audit-goal-rate --input=/tmp/fixtures.json --output=/tmp/goal-rate-audit --settings=src/soccer-ai-api/appsettings.json
python3 tools/ml/audit_weekend.py --input=/tmp/fixtures.json --output=/tmp/weekend-audit.json
dotnet test --no-restore
```

`audit-goal-rate` dispatches before host construction, so it does not start hosted services, migrate a database, call a provider, or publish a model. Configuration provenance contains model options, not API keys. Other operational CLI commands may migrate/connect to the configured database and should not be substituted for the isolated audit.

Validation includes real SQLite migrations and ledger/statistics queries, PostgreSQL idempotent SQL generation (not a live PostgreSQL migration), causal feature and calibration splits, odds withdrawal and repricing, model artifact compatibility, native cached-price expiry and entitlement loss. **Latest full suites: 561 backend tests and 32 native iOS tests passed on 11 September 2026.** Existing XML documentation/AppIntents warnings do not represent test failures.

For deployment, apply the included provider-specific migrations through the existing deployment process before enabling ledger writes, deploy the API/worker and native client, and inspect actual sync/odds timestamps. Migration execution on production, current-season results, StoreKit sandbox/server verification and live monitoring are not claimed by these local tests.
