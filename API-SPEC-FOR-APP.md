# Soccer AI — API Spec for a Mobile App Prototype

Hand this whole file to Claude (or any designer/developer) as the brief for
building a mobile app against this backend.

- **Base URL (local):** `http://localhost:5000`
- **All JSON is `snake_case`.** The server sets `SnakeCaseLower` globally.
- **Every response is wrapped in the same envelope** (see below).
- **Everything except login requires auth.**

---

## 1. What this product actually is

Two separate products, from one backend. Design them as two different screens,
because they answer different questions and carry very different confidence.

| | **Product 1 — Value bets** | **Product 2 — Confidence picks** |
|---|---|---|
| Question | "Where is the bookmaker wrong?" | "What is most likely to happen?" |
| Endpoint | `GET /api/picks` → `singles`, `combos`, `same_match_pairs` | `GET /api/picks` → `confidence_picks` |
| Needs odds? | Yes — no price, no pick | No |
| Volume | ~1–3 per day | ~3 per day |
| Proven? | **Not yet.** ~72 measured bets, ROI +7% ± 10pp | Better: 239 samples, 67% hit rate |

**Design implication:** lead the app with Product 2. It has volume and evidence.
Treat Product 1 as the premium, low-volume tab.

---

## 2. How the AI is used (read this before designing)

There is a strict split, and the app should reflect it honestly:

- **Numbers come from mathematics.** A Dixon-Coles Poisson model produces the
  probabilities; bookmaker odds calibrate them; a transparent rule engine
  decides what qualifies. Every probability, price, edge and stake is computed.
- **The LLM writes the words.** Per-match narrative, per-market summaries, a
  one-line verdict. That text lives at `match.ai.*` and `prediction.*.reason`.

The LLM never picks a bet and never influences a number. So in the UI, never
present the AI text as the reason a bet qualified — present it as commentary
next to the numbers that did the qualifying. The `decision_audit` field is the
real reason, and it is human-readable.

---

## 3. Conventions

### Response envelope

Every endpoint returns:

```json
{
  "success": true,
  "data": { },
  "message": null,
  "timestamp": "2026-08-10T09:00:00Z"
}
```

On failure: `success: false`, `data: null`, `message` explains.

### Paging

Every field name in this API — query parameters and JSON keys alike — is
`snake_case`.

Collection endpoints put a paged envelope inside `data`:

```json
{
  "items": [ ],
  "limit": 20,
  "offset": 0,
  "total": 82,
  "has_more": true
}
```

| Field | Meaning |
|---|---|
| `items` | this page |
| `limit` | page size actually applied |
| `offset` | rows skipped |
| `total` | size of the **whole** matching set, not the page |
| `has_more` | another page exists — prefer this over comparing counts yourself |

Send `limit` and `offset`. Both are optional; **`limit` defaults to 50 and is
capped at 200, so omitting it returns the first page, never everything.** Design
for the list to be paged — an endpoint that looks small today grows on a busy
matchday.

`page` and `page_size` are accepted as a **deprecated** one-based alias
(`offset = (page - 1) * page_size`) and will be removed. Prefer `limit`/`offset`.
If both are sent, `limit`/`offset` win.

Invalid paging returns `400` with errors keyed by the snake_case field:

```json
{ "status": 400, "message": "Validation failed",
  "errors": { "limit": ["'limit' must be between 1 and 200."] } }
```

### Auth

```
POST /api/auth/login
Content-Type: application/json

{ "username": "admin", "password": "..." }
```

→ `{ "success": true, "data": { "token": "<jwt>" } }`

Send it on every other call:

```
Authorization: Bearer <jwt>
```

Errors: `401` bad credentials or missing/expired token.

### Dates

`YYYY-MM-DD` in query strings. Timestamps are ISO-8601 UTC. **Kickoff times are
UTC — convert to the device's timezone before display.**

---

## 4. Endpoints the app needs

### 4.1 Today's picks — the main screen

```
GET /api/picks?date=2026-08-10&language=en
```

Both parameters optional (`date` defaults to today UTC, `language` is `en` or
`de`).

```json
{
  "date": "2026-08-10",
  "singles": [ /* TicketDto */ ],
  "same_match_pairs": [ /* TicketDto */ ],
  "combos": [ /* TicketDto */ ],
  "confidence_picks": [ /* ConfidencePickDto */ ],
  "coverage": {
    "fixtures": 82,
    "analyzed": 80,
    "priced": 71,
    "priced_pct": 86.6
  }
}
```

**`TicketDto`**

```json
{
  "kind": "single",
  "legs": [
    {
      "fixture_id": 24932,
      "kickoff_utc": "2026-08-10T16:30:00+00:00",
      "league": "La Liga 2",
      "match": "Racing Santander vs Valladolid",
      "market": "under25",
      "selection": "Under 2.5 Goals",
      "probability": 0.6412,
      "odds": 1.95,
      "ev": 0.2503
    }
  ],
  "total_odds": 1.95,
  "fair_odds": 1.56,
  "probability": 0.6412,
  "ev": 0.2503,
  "kelly_stake": 0.0642,
  "contains_goals_market": true
}
```

- `kind` — `single` | `same_match_pair` | `combo`
- `market` — `btts` | `over25` | `under25` | `match_winner` | `draw` | `goals_2_3`
- `fair_odds` — break-even price for that probability. **Show it next to
  `total_odds`:** "we need 1.56, Bet365 offers 1.95" is the clearest possible
  explanation of value, and it lets the user sanity-check us.
- `ev` — expected value as a fraction. `0.25` = +25%.
- `kelly_stake` — **share of bankroll, not an amount.** `0.0642` = 6.4%.
  If the app shows currency, multiply by a bankroll the user enters.
- `same_match_pair` — BTTS + Over 2.5 on one fixture. Its two legs share a
  `fixture_id` and both carry the *joint* probability, not their own. Show it
  as one bet, not two.

**`ConfidencePickDto`**

```json
{
  "fixture_id": 24932,
  "kickoff_utc": "2026-08-10T16:30:00+00:00",
  "league": "Premier League",
  "match": "Arsenal vs Chelsea",
  "market": "match_winner",
  "selection": "Match Winner (Home)",
  "model_probability": 0.7412
}
```

⚠️ **Do not display `model_probability` as a headline percentage.** It is the
*highest* of several market estimates for that fixture, and a maximum always
reads higher than the truth. Measured reality by band:

| Model says | Actually happens |
|---|---|
| 60–65% | ~65% (match winner), ~57% (BTTS) |
| 65–70% | ~66% (match winner), ~75% (BTTS) |
| 70–80% | **~80%** (match winner) |

Safer UI: show a **confidence label** (`High` / `Medium`) rather than a number,
or show the measured band rate. Never invent a precision you cannot back.

**`coverage` matters.** An empty board with `priced_pct: 20` means *"no odds
yet"*, not *"no value today"*. Say so — otherwise the app looks broken.

---

### 4.2 All matches with analysis — the browse screen

This is the "show all matches with LLM analysis" screen.

```
GET /api/analyze?date=2026-08-10&language=en&limit=20&offset=0&only_analyzed=true
```

| Param | Notes |
|---|---|
| `date` | defaults to today |
| `language` | `en` or `de` |
| `limit` | page size, 1–200. **Defaults to 50 — omitting it does not return the whole day.** |
| `offset` | rows to skip, defaults to 0 |
| `page`, `page_size` | **deprecated** one-based alias, resolves to the same window. Migrate to `limit`/`offset`. |
| `only_analyzed` | `true` = skip fixtures with no analysis |
| `refresh` | **never send from the app** — forces recomputation, very slow |

Out-of-range paging is rejected with `400`, not clamped: `limit` outside 1–200,
a negative `offset`, or `page` below 1 all return a validation error keyed by the
snake_case field name.

```json
{
  "items": [ /* MatchAnalysis */ ],
  "limit": 20,
  "offset": 0,
  "total": 82,
  "has_more": true,

  "matches": [ /* deprecated: same array as items */ ],
  "total_count": 82,

  "summary": { "total_matches": 40, "correct_matches": 26, "accuracy_rate": 65.0 }
}
```

`matches` and `total_count` are duplicates kept for one release so the shipped
app survives the cutover. Read `items` and `total`; the old keys will be removed.

**`summary` describes the page, not the day.** With paging on by default it
covers only the finished fixtures in `items`, so do not label it as a day-wide
accuracy figure.

**`MatchAnalysis`** — the big one. Key fields for a mobile UI:

```json
{
  "id": 24932,
  "date": "2026-08-10T16:30:00+00:00",
  "league": "La Liga 2",
  "home_team": "Racing Santander",
  "away_team": "Valladolid",

  "result": { "actual_score": "2:1", "is_correct": true,
              "is_btts_correct": true, "is_over25_correct": true },

  "odds_home_win": 1.85, "odds_draw": 3.40, "odds_away_win": 4.20,
  "odds_over25": 1.95, "odds_under25": 1.85, "odds_btts_yes": 1.80,

  "home_stats": { "name": "...", "rank": 1, "points": 69, "form": "DWWLW",
                  "form_percentage": 67, "momentum": 61.9,
                  "avg_goals_scored_last_3": 2.67, "btts_rate_last_3": 0.67,
                  "over_25_rate_last_3": 0.67, "clean_sheet_rate": 0.14 },
  "away_stats": { },

  "prediction": {
    "over25":     { "prediction": true, "probability": 0.64,
                    "is_qualified": true, "reason": "LLM text or rule text" },
    "btts":       { },
    "low_scoring":{ },
    "home_win":   { },
    "draw":       { },
    "away_win":   { },
    "match_winner": { "prediction": "home", "confidence": 0.62, "is_qualified": true }
  },

  "ai": {
    "one_line_summary": "...",
    "analysis": "long narrative",
    "recommendation": "...",
    "btts_summary": "...", "over25_summary": "...", "under25_summary": "...",
    "home_win_summary": "...", "away_win_summary": "...",
    "is_trap": false, "trap_reason": ""
  },

  "h2h": { },
  "signals": { },
  "decision_audit": { },
  "calibration_trace": [ ]
}
```

**Suggested match-detail layout:**

1. Teams, kickoff, league, form strings — from `home_stats.form` / `away_stats.form`
2. `ai.one_line_summary` as the headline
3. Market cards from `prediction.*` — probability, `is_qualified` badge, `reason`
4. `ai.analysis` in an expandable "Full analysis" section
5. "Why?" expander → `decision_audit` (see below)

**`decision_audit`** — this is what makes the app trustworthy rather than a
black box. Per market:

```json
{
  "min_confirmations": 2,
  "markets": [
    {
      "market": "over25",
      "selection": "Over 2.5 Goals",
      "probability": 0.6412,
      "threshold": 0.50,
      "probability_passed": true,
      "odds": 1.95,
      "ev": 0.2503,
      "min_edge": 0.05,
      "kelly_stake": 0.0642,
      "confirmations_fired": 2,
      "vetoes_fired": 0,
      "qualified": true,
      "gate_outcome": "qualified",
      "rules": [
        { "rule_id": "over25_confirm_both_venue_rates", "kind": "confirm",
          "fired": true, "evidence": "Home 4/5 over 2.5 at home; Away 3/5 away" }
      ]
    }
  ]
}
```

`gate_outcome` tells the user exactly why a match is *not* a pick — render it
as plain language:

| `gate_outcome` | Show as |
|---|---|
| `qualified` | ✅ Qualified |
| `analysis_only_no_odds` | No odds available |
| `below_min_edge` | Price too short for the edge |
| `below_probability_floor` | Not likely enough |
| `vetoed` | Blocked: *(name the fired veto rule's evidence)* |
| `insufficient_confirms` | Not enough supporting evidence |
| `informational_only` | Not offered as a bet |

---

### 4.3 Track record — the trust screen

```
GET /api/picks/performance?from=2026-05-01&to=2026-08-10
```

Defaults to the last 90 days. **These are live published results at the prices
users were shown** — not a simulation.

```json
{
  "from": "2026-05-01", "to": "2026-08-10",
  "overall": {
    "key": "overall", "settled": 41, "won": 22, "pending": 6, "voided": 2,
    "hit_rate_pct": 53.7, "staked": 41.0, "returned": 46.2,
    "roi_pct": 12.7, "sample_too_small": false
  },
  "by_kind":   [ /* single | same_match_pair | combo */ ],
  "by_market": [ /* btts | over25 | under25 | match_winner | draw */ ]
}
```

⚠️ **`sample_too_small` is `true` below 30 settled tickets. When it is true, the
app must not show `roi_pct` as a headline.** Show "building track record —
N results so far" instead. Betting returns are wildly variable; a number from
12 bets is noise, and publishing it as a claim is misleading.

`voided` = abandoned or unsettleable fixtures. Excluded from ROI, never counted
as losses.

---

### 4.4 Supporting endpoints

```
GET /api/leagues                  → paged { "items": [{ "id": 39, "name": "Premier League" }, ...] }
GET /api/leagues/{id}/status      → sync/persistence status for one league (single object, not paged)
GET /api/backtest?weeks_back=30&stake=1   → full historical report (large, slow; admin only)
POST /api/combinations            → paged; legacy shape of /api/picks combos; prefer /api/picks
```

`GET /api/leagues` now returns the paged envelope rather than a bare array —
read `data.items`. The list is a fixed sixteen entries, so the default window
returns all of them.

`POST /api/combinations` takes `limit`/`offset` in the JSON body alongside
`date`. Its `combinations` key is deprecated and duplicates `items`.

`GET /api/picks` is **not** paged: it is a composite of four separately-bounded
lists (`singles`, `same_match_pairs`, `combos`, `confidence_picks`) plus
`coverage`, none of which grows with fixture count. `GET /api/picks/performance`
is likewise a fixed set of slices.

Admin-only, **not for the app**: `POST /api/automation/*` (sync triggers),
`GET /api/automation/health`, `GET /api/automation/sync-status`.

**`GET /api/automation/health` is liveness only.** It returns a constant and
never reads the database, so it says `healthy` regardless of whether any data is
being synced. Do not use it to judge data freshness.

**`GET /api/automation/sync-status`** is the honest check. Optional
`stale_after_hours` (default 26).

```json
{
  "status": "healthy",
  "last_successful_sync_utc": "2026-08-13T03:31:12+00:00",
  "last_run_started_utc": "2026-08-13T03:30:00+00:00",
  "last_completed_step": "ai_narratives",
  "last_error": null,
  "hours_since_last_success": 1.2,
  "is_stale": false,
  "fixture_count": 41233, "team_count": 812, "analysis_count": 39104
}
```

`status` is one of `never_run`, `healthy`, `stale`, `failing`. Read it rather
than deriving a verdict from the timestamps — an unresolved `last_error` reports
`failing` even when a older run succeeded recently. The row counts are there
because a sync can report success while the tables stay empty.

---

## 5. Suggested app structure

```
┌─ Today ──────────────── default tab
│   • Confidence picks (Product 2) — the volume
│   • "Value bets" section — often empty; say why using `coverage`
│
├─ Matches ────────────── GET /api/analyze
│   • Day selector, league filter
│   • Match card → detail with AI analysis + decision audit
│
├─ Track record ───────── GET /api/picks/performance
│   • Honest numbers; hide ROI while `sample_too_small`
│
└─ Settings ───────────── language (en/de), bankroll for Kelly, timezone
```

---

## 6. Rules the UI must not break

These exist because breaking them misleads users about money.

1. **Never show ROI when `sample_too_small` is true.**
2. **Never present `model_probability` as a precise headline figure** — it is
   upward biased by construction.
3. **`kelly_stake` is a fraction of bankroll**, not currency. Multiplying it by
   nothing gives a meaningless number.
4. **Never label the AI narrative as the reason a bet qualified.** Numbers
   qualify bets; the LLM only describes them.
5. **An empty board is not an error.** Use `coverage` to say whether it was
   missing odds or no value found.
6. **Show `fair_odds` beside `total_odds`.** It is the one number that lets a
   user check our claim themselves.
7. **Odds move.** Display "price at publication" and tell users to verify at the
   bookmaker before betting.

---

## 7. Known limitations to design around

- **BTTS has odds for only ~30% of fixtures**, so BTTS picks are rare. Do not
  build a screen that assumes they exist daily.
- **Value bets are ~1–3 per day**, sometimes zero. The app must look intentional
  when empty.
- **2-3 Goals (`goals_2_3`) is analysis-only** — no odds exist for it. Show it as
  information, never as a bet.
- **Fixtures more than a week old may have no odds at all**; the odds provider
  keeps only 7 days of history.
