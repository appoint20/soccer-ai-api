# Soccer AI API — Project Status

**Date:** 2026-08-05 · **Commits:** 229 · **Code:** ~26,400 lines across 6 projects

---

## Short answer

**The engine is finished. The product is not.**

The maths, the calibration, the rules and the ticket builder all work and are tested.
But they only run inside the **backtest**. A real user calling the API today does
**not** get them. That is the gap.

**My estimate: 3 blockers stand between you and go-live.** None of them is maths.
All three are plumbing and data.

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
| Ticket builder | ⚠️ Built, **not wired** | only called by the backtest |
| Confidence picks (Product 2) | ⚠️ Built, **not wired** | only computed in the backtest report |
| Backtest report | ✅ Done | 12 sections, 30-week runs |
| Sync worker | ✅ Running | 03:30 + 15:30 UTC, resumable |
| Odds capture worker | ✅ Running | 30-min loop, T-24h / T-1h |
| CLI tools | ✅ Done | backtest, sync, backfill, odds-coverage |
| REST API | ⚠️ Partial | 6 controllers, but no "today's picks" endpoint |
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

### Odds coverage — this is the problem

| Market | Snapshots with a price | Coverage |
|---|---|---|
| Home win | 3,676 | **35.5 %** |
| Over 2.5 | 3,606 | **34.8 %** |
| BTTS | 1,418 | **13.7 %** |
| Under 2.5 | 0 | **0.0 %** |

**No odds = no EV = no pick.** Your two main markets are the two worst covered.
BTTS at 13.7 % means the model can only look at 1 match in 7.

Live odds table right now: **79 quotes for 1 fixture.** The capture worker started
today, so this grows on its own — but it needs weeks, not days.

---

## 3. The 3 blockers before go-live

### 🔴 Blocker 1 — The API does not serve the picks

`TicketBuilder` is called in exactly one place:

```
src/soccer-ai-application/Features/Backtesting/GetBacktestReportHandler.cs:422
```

That is it. Nowhere else. Same for confidence picks.

So today: your model finds a good ticket → the ticket is computed → and then it is
thrown away, because nothing serves it.

**What is needed:** a `GET /api/picks?date=...` endpoint that runs the same code on
*upcoming* fixtures and returns:

- qualified singles (EV picks)
- 2-leg combos, goals-market first
- same-match BTTS+Over2.5 pairs
- confidence picks (Product 2)

This is the single most important missing piece. Without it there is no product.

### 🔴 Blocker 2 — The combinations endpoint is LLM-driven

`POST /api/combinations` → `ChatCombinationEngine` → `aiService.BuildCombinationsAsync(...)`

The LLM is picking the combinations. **This breaks your own rule:** the LLM writes
text, never decides. It is also why `combos_total` was 0 without an API key.

**What is needed:** point this endpoint at `TicketBuilder`. Keep the LLM only to
write the sentence explaining the ticket.

### 🔴 Blocker 3 — Odds coverage is too thin to launch

At 13.7 % BTTS coverage you cannot deliver daily picks reliably. Some days you will
have zero.

**What is needed:** let the capture worker run for 4–6 weeks, then re-measure with
`odds-coverage`. There is no shortcut — this is waiting, not coding.

---

## 4. Not blockers, but do them before real money

| Item | Why | Effort |
|---|---|---|
| Under 2.5 odds = 0 % | Market can never qualify. Check the parser is reading the right API label. | Small |
| Result tracking | Nothing records whether a *live* pick won. You cannot prove ROI to a customer without it. | Medium |
| Publish measured, not modelled, probability | Confidence picks are upward biased (Over 2.5: says 66 %, delivers 55 %). The fix exists in the report — use those numbers in the UI. | Small |
| Postgres switch | You are on a 228 MB SQLite file. Provider exists, migrations exist, just untested under load. | Medium |
| Auth hardening | Login works, but nothing else. Fine for a private beta, not for paying users. | Medium |
| Rate limiting / API keys for customers | No customer-facing key system. | Medium |

---

## 5. Suggested order

1. **Wire the picks endpoint** (Blocker 1) — turns the engine into a product
2. **Replace the LLM combination engine** (Blocker 2) — one file, big correctness win
3. **Fix Under 2.5 odds parsing** — small, and unlocks a whole market
4. **Add result tracking** — start collecting proof now, so in 6 weeks you have it
5. **Wait on odds coverage** (Blocker 3) — runs in the background while 1–4 happen
6. **Re-run the backtest at 3 % MinEdge** — confirm the change I just applied
7. Postgres + auth + rate limiting — before you charge anyone

Steps 1–4 are real coding work. Step 5 is calendar time, and it runs in parallel.

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

## 7. What I need from you

- Run `dotnet build && dotnet test` — I cannot run .NET here, so the last two
  commits are unverified on your machine.
- Confirm you want me to start on **Blocker 1** (the picks endpoint).
