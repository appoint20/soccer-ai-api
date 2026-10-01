# Analysis-first API and iOS rollout

## Contract

The new authenticated read surface is separate from legacy betting decisions:

- `GET /api/briefings?date=2026-10-01&language=de&limit=100&offset=0`
- `GET /api/briefings/{fixtureId}?language=en`
- `GET /api/briefings/{fixtureId}/history`

Existing bearer authentication and the CombinedPolicy apply. The response uses the existing `success` / `data` envelope and snake-case JSON. Lists expose `matches`, `limit`, `offset`, `total`, `has_more`, `mode: analysis` and `provider_refresh_requested: false`. Detail returns `data.match`. The list date follows the existing API's UTC-day convention. Individual fixture lookup is independent of the selected day.

These routes only read the database. They neither call the football provider nor run AI, model recomputation, sync jobs, or writes. A `refresh=true` query does not enable refresh. Missing snapshots remain visible as fixtures with `data_status: unavailable` and nullable estimates. A changed kickoff or changed team names invalidates the stored briefing until the normal pipeline writes a matching snapshot. Invalid/missing probabilities are not invented zeros.

`match-analysis-v1` includes match identity, last stored score, contextual summary, limitations, home/draw/away estimates, the three goal patterns (`btts`, `over25`, `goals_2_3`), source participation, team statistics and head-to-head history. It deliberately excludes betting selections, qualification flags, odds, Kelly stakes and best-bet headlines. Goal patterns remain visible even below 50%.

Each goal pattern has at most five distinct evidence items. Measured counter-evidence comes first. AI agreement counts and generic rule-gate explanations are excluded. Unsupported rule evidence is omitted rather than converted to a factual claim. AI context is used only when the existing validation/hash policy considers it current; otherwise the API uses explicitly labelled stored-form context, not old advice text.

Source participation is not independence, confidence, or proven accuracy. Experimental blend weights are disclosed. `analysis_updated_at_utc` dates the analysis snapshot, not the last provider request. Browsing does not make an old snapshot fresh. The existing two-hour sync cadence is unchanged; scores and analysis are not advertised as live.

## History and evaluation

History reads the immutable pre-match model ledger, not mutable analysis text. It accepts only the fixture's current kickoff, capture times strictly before kickoff and not in the future, valid outcome distributions, and valid goal probabilities. It returns up to 50 recent records (the app shows ten), timestamps and model versions. Empty history is shown honestly; no prior briefing or reason for an estimate change is reconstructed. Legacy ledger rows do not contain a full archived narrative.

The app's 90-day review reuses the existing scored pre-match ledger and exposes coverage, missing records and probability error. It is not a returns report or evidence that the four-source blend improves accuracy. Different model versions can coexist in the sample.

## Compatibility and rollout

1. Deploy and smoke-test the backend routes first, using a normal signed-in account as well as an unauthenticated denial check. No schema migration is introduced.
2. Release the iOS build after backend availability. The app does not fall back to the old betting-tip interface if the new routes are unavailable.
3. Verify stored source/context availability in staging before promoting the app. This implementation does not execute a fresh NVIDIA/provider probe or change scheduled AI settings.
4. Keep old `/api/analyze`, prediction/refresh and picks endpoints for existing clients and administration. Their legacy contracts are intentionally unchanged. New user navigation has no accumulator builder or staking controls.

## Research workspace and billing

The app saves match bookmarks, up to 2,000-character notes and league preferences locally, scoped to the authenticated subject and issuer where available. Bookmarks open current stored analysis; they are not immutable briefing archives. Notes can be read/edited and bookmarks removed offline from Saved research. Account deletion clears this account's local research; signing out hides it without deleting it.

Free accounts can save three matches; StoreKit-verified subscribers can save more. Existing saved notes are not deleted or locked after subscription expiry. Core evidence, caveats, source participation, history and model review remain free. This is a client-side local-workspace entitlement, not server-side protection of premium data. Do not represent it as secure paid API access or cloud synchronisation.

Product identifiers and real prices are unchanged. Local StoreKit test descriptions and in-app copy describe Research Pro, without a fictitious free trial. Before release, the owner must align App Store Connect subscription descriptions, screenshots, legal/privacy text and existing subscriber expectations with the new offer. No App Store metadata or live billing configuration was modified by this change.

## Validation

- Factory tests cover bilingual measured evidence, the five-item limit, preservation of all goal patterns, missing-vs-zero estimates and exclusion of betting fields.
- HTTP end-to-end tests verify authentication, cold/warm reads without provider or AI calls, no writes for missing analyses, rescheduling invalidation and recorded-only history.
- iOS tests cover the new wire contract, endpoint routing, all goal patterns, evidence limits, account isolation, persistence, the free limit, note length, expiry-safe access and bilingual navigation.
- Run the existing backend suite with its isolated PostgreSQL regression database and the complete iOS simulator suite before release.

Local validation on 2026-10-01: 860 backend tests passed with no skips, using a disposable localhost PostgreSQL instance; 59 iOS simulator tests passed. The new matchday and match-detail layouts were visually inspected with explicitly labelled offline demo fixtures on iPhone 17 / iOS 26.5. No live-provider/NVIDIA response or remote staging deployment was tested in this change.

No production deployment or live billing change is part of this implementation.
