# Soccer AI API — Project Status

**Date:** 2026-08-05 · **Commits:** 229 · **Code:** ~26,400 lines across 6 projects

---

## Short answer

**The engine works and the product now exposes it.** Blockers 1 and 2 are closed.
Blocker 3 is odds coverage, which is calendar time, not code.

`GET /api/picks` serves the day's tickets and confidence picks. Selection runs
through `PickSelector` — the same component the backtest measures — so what a
user sees and what the report measured are one code path, not two that happen
to agree.

Result tracking is in: every published ticket is recorded at the price shown and
settled once the fixture finishes, so `GET /api/picks/performance` reports what
the strategy *did* return, not what a backtest says it would have.

**What is left before real money:** enough odds coverage to fill a daily board,
a re-run of the backtest at the new 3% edge, and a few weeks of the ledger
filling up so the performance numbers mean something.

---

## 1. What is built

| Layer | State | Notes |
|---|---|---|
| Dixon-Coles model | ✅ Done | τ correction, time decay, venue split, Bayesian shrink |
| Shin margin removal | ✅ Done | 3-way and 2-way |
| Market calibration (w=0.5) | ✅ Done | blend model ↔ bookmaker |
| Isotonic calibration | ✅ Done | walk-forward, no look-ahead, shrinkage for small samples |
| Strategic signals (A–H) | ✅ Done | gate only — never touch probabilities |
| Confluence rule engine | ✅ Done | confirm / veto / audit trail |
| EV + Kelly staking | ✅ Done | quarter-Kelly |
| Ticket builder | ✅ Wired | `PickSelector` → `TicketBuilder`, shared by backtest and API |
| Confidence picks (Product 2) | ✅ Wired | served by `/api/picks` |
| Backtest report | ✅ Done | 12 sections, 30-week runs |
| Results ledger | ✅ Done | published tickets frozen at price, auto-settled |
| Sync worker | ✅ Running | 03:30 + 15:30 UTC, resumable, publishes + settles |
| Odds capture worker | ✅ Running | 30-min loop, T-24h / T-1h |
| CLI tools | ✅ Done | backtest, sync, backfill, odds-coverage |
| REST API | ✅ Done | 7 controllers incl. `GET /api/picks` |
| Auth | ⚠️ Basic | login only — untouched by design |
| Tests | ✅ 190 tests | 21 files, maths core well covered |

---

## 2. The real numbers in your database today

| Item | Count |
|---|---|
| Fixtures | 24,481 (24,127 finished) |
| Leagues | 17 |
| Teams | 456 |
| Analyses with probabilities | 10,394 |
| Analyses with snapshot | 10,352 |
| Backtest reports | 15 |
| Migrations applied | 14 |
| Upcoming fixtures (not started) | 312 |
| Database size | 228 MB (SQLite) |

### Odds coverage — the remaining blocker

Measured on the `Fixtures` table (the source the gate actually reads):

| Market | Fixtures with a valid price | All history | Last 2,000 finished |
|---|---|---|---|
| Home / Draw / Away | ~10,800 | 44 % | 35 % |
| Over 2.5 | 10,797 | 44 % | 35 % |
| Under 2.5 | 10,797 | 44 % | 35 % |
| BTTS | 737 | **3 %** | 35 % |

**No odds = no EV = no pick.** BTTS is the weak one historically (3 %), which is
why the backtest sees so few BTTS tickets. Recent weeks are healthy at 35 %,
because the newer sync path captures it — so this improves on its own as data
accumulates.

Live odds quote table right now: **79 quotes for 1 fixture.** The capture worker
started recently. This needs weeks, not days.

> An earlier draft of this document reported Under 2.5 coverage as 0 %. That was
> wrong: it measured the snapshot JSON, which did not serialize `OddsUnder25`.
> The gate always received the price. The snapshot gap is now fixed.

---

## 3. The 3 blockers

### ✅ Blocker 1 — The API now serves the picks

Before, `TicketBuilder` was called in exactly one place: the backtest handler.
The model found a good ticket, computed it, and threw it away.

**Now:** `GET /api/picks?date=YYYY-MM-DD` returns singles, same-match pairs,
combos and confidence picks, plus a coverage block.

The design point that matters: per-fixture selection was extracted out of the
backtest into `PickSelector`, a pure component with no I/O and no knowledge of
outcomes. The backtest joins results back on `(FixtureId, Market)` afterwards.
Both callers now run identical selection code — the only way published picks can
be trusted to match measured ones.

```
PickSelector ──┬── GetBacktestReportHandler   (measures)
               └── DailyPickService           (sells)
```

### ✅ Blocker 2 — The LLM no longer decides

`GetMatchCombinationHandler` called `IChatCombinationEngine`, which asked a
language model to assemble the portfolios. That broke the project's own rule
(the LLM writes text, never decides), produced parlays nobody could backtest,
and returned nothing at all when no model API key was configured — which is why
`combos_total` was 0.

It now builds from the same board as `/api/picks`. No model call in the path.

### 🔴 Blocker 3 — Odds coverage is still too thin

BTTS sits at 3 % across history and 35 % in recent weeks. At that level some days
produce zero tickets. The capture worker fixes this by running, not by coding.

**Remaining action:** let it run 4–6 weeks, then re-measure with `odds-coverage`.

One real bug found and fixed on the way: the snapshot serializer never persisted
`OddsUnder25` or the BTTS∧Over 2.5 joint probability. The joint matters — a
same-match double priced as `p_btts × p_over25` is badly understated, because the
two markets are positively correlated.

---

## 4. Not blockers, but do them before real money

| Item | Why | Effort |
|---|---|---|
| Publish measured, not modelled, probability | Confidence picks are upward biased (Over 2.5: says 66 %, delivers 55 %). The fix exists in the report — use those numbers in the UI. | Small |
| Postgres switch | You are on a 228 MB SQLite file. Provider exists, migrations exist, just untested under load. | Medium |
| Auth hardening | Login works, but nothing else. Fine for a private beta, not for paying users. | Medium |
| Rate limiting / API keys for customers | No customer-facing key system. | Medium |

---

## 5. What is left

1. ~~Wire the picks endpoint~~ ✅
2. ~~Replace the LLM combination engine~~ ✅
3. ~~Add result tracking~~ ✅
4. **Start the worker and leave it running** — the ledger only fills with time
5. **Re-run the backtest at 3 % MinEdge** — confirm the config change
6. Postgres + auth + rate limiting — before you charge anyone

The coding before launch is done. Steps 4 and 5 are calendar time and one
command. Step 6 is the commercial hardening you do before taking money.

---

## 6. Honest warnings

- **Ticket results are n=8.** +81 % ROI means nothing at that sample size. Do not
  put it on a landing page.
- **The EV sweep ROI column is noise.** Samples of 9–26 bets. I changed MinEdge to
  3 % based on *hit rate and pick count*, which have bigger samples — not on ROI.
- **Your shadow cohorts showed losses** for picks just below the bar (Over 2.5
  −9.7 %, match winner −8.8 %). That is real evidence against loosening further.
- **Selection bias is live in Product 2.** Choosing the highest probability across
  markets always overstates it. Publish the measured buckets.

---

## 7. On using an LLM to pick the matches

The proposal was to let a large model read the data and decide the market itself,
with no filtering. Three reasons that does not work here, and one way it does:

**It cannot produce a calibrated number.** The whole product is `EV = p × odds − 1`.
That needs a `p` that means something: when the model says 62 %, the thing must
happen 62 % of the time. Dixon-Coles plus isotonic calibration gives that, and
the backtest measures it — Brier score, log loss, calibration buckets. A language
model asked for "62 %" is producing a plausible-sounding token, not an estimate
with a track record.

**It cannot be backtested.** Same fixture, two runs, two answers. Nothing to
measure, nothing to improve, and no way to answer a customer asking why.

**The bookmaker already read the news.** The price contains the market's
information. Beating it requires a systematic, measurable edge — which is what
the shadow cohorts and the EV sweep exist to find.

**Where a model genuinely helps:** turning unstructured text into structured
facts — injuries, suspensions, manager quotes, rotation news — that then feed the
signal catalog as ordinary features and get measured like any other signal. And
writing the narrative, which it already does.

**If you want to test the idea properly:** run it in shadow mode. Have the model
produce a pick for every fixture, store it, act on none of it, and after ~500
fixtures compare its Brier score against the model's. That costs you nothing but
API calls and answers the question with evidence. Say the word and I will build
it as a shadow cohort — it fits the existing reporting.
