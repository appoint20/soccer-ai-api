# Match summaries

The native iOS detail screen renders `presentation.summary_lines` without a line
limit. The active prompts are in `OpenAiAnalysisService`, not the unused
`AiPrompts.MatchAnalysisSystemPrompt` constant.

The displayed AI summary now contains **6–8 sentences in English and German**:

- Four to six sentences explain the match: the overall picture, how each attack
  compares with the opposing defence, the scoring pattern, conflicting evidence
  and uncertainty. The prompt targets 70–120 words of context. Validation requires
  at least 50 words, complete sentences and a maximum of 260 characters per entry.
- Two closing sentences come from the final decision audit. They name the actual
  selections and explain the strongest selection, or explain why nothing qualifies.

The earlier AI opinion's `analysis` field separately requires 4–8 sentences and
at least 60 words in both languages. The provider adapter rejects responses that
are too short or too long and tries its existing configured fallback. It does
not pad text or change models, prices, probabilities, qualification rules or fees.
Length checks enforce structure; they cannot guarantee good reasoning or factual
correctness. Existing grounding and decision checks still apply.

Explanation contract version 2 excludes informational odds and bookmaker fields
from the writer's inputs and cache identity. Price-only changes therefore preserve
the full summary. Changes to teams, match evidence, probabilities or decisions
still invalidate it. Missing or outdated explanations retain the honest pending
state with the current system decision; they are not presented as fresh AI text.

Deploy both API and worker to use the updated prompts and cache rules. The changed
opinion prompt hash and explanation version cause eligible upcoming matches to
refresh during AI sync. Existing completed-match records are not rewritten; their
older summaries remain readable when the original input hash still matches.
For an immediate refresh of an upcoming day, use the existing admin
`POST /api/automation/ai-analysis?date=YYYY-MM-DD` endpoint. Generation remains
subject to the configured provider's availability and quota.

Tests use local mock completion endpoints and no real AI requests. They cover both
languages, length boundaries, fallback after a short response, preservation after
price moves and invalidation after evidence changes.
