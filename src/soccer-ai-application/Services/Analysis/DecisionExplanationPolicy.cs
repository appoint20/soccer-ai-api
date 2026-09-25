using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Services.Analysis;

/// <summary>Grounds presentation in the final audit and invalidates text after evidence or decision changes.</summary>
public static class DecisionExplanationPolicy
{
    public const int Version = 2;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static AiDecisionExplanation? Read(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AiDecisionExplanation>(json, Json); }
        catch (JsonException) { return null; }
    }

    public static DecisionExplanationInput Input(MatchAnalysis match) => new(
        Version, match.Id, match.Date, match.HomeTeam, match.AwayTeam, match.HomeStats, match.AwayStats, match.H2H,
        (match.DecisionAudit?.Markets ?? []).Where(m => m.Probability >= .5).OrderBy(m => m.Market)
        .Select(m => new DecisionExplanationMarket(m.Market, m.Selection, m.Qualified, m.GateOutcome,
            // Prices are informational and the writer may not discuss them.
            // Exclude them from both the prompt and its cache identity so a quote
            // refresh cannot reduce an otherwise current summary to two lines.
            m.Probability, null, null, Facts(m, "en"))).ToList(),
        (match.DecisionAudit?.Markets ?? []).Where(m => m.Qualified).Select(m => m.Market).Order().ToList());

    public static string Hash(DecisionExplanationInput input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    public static bool IsCurrent(MatchAnalysis match, AiDecisionExplanation? explanation) =>
        explanation is not null && explanation.FixtureId == match.Id &&
        explanation.InputHash == Hash(Input(match)) && Invalid(explanation, Input(match)) is null;

    public static string? Invalid(AiDecisionExplanation? result, DecisionExplanationInput input) => Invalid(result, input, false);

    private static string? Invalid(AiDecisionExplanation? result, DecisionExplanationInput input, bool legacy)
    {
        if (result is null || result.FixtureId != input.FixtureId) return "wrong or missing fixture";
        foreach (var block in new[] { result.En, result.De })
        {
            if (block?.SummaryLines is not { Count: >= 4 and <= 6 } || block.Markets is null)
                return "four to six context sentences required before the two final decision sentences";
            if (legacy && block.SummaryLines.Count != 4) return "legacy summary must have four context sentences";
            if (!block.Markets.Select(m => m.Market).Order().SequenceEqual(input.Markets.Select(m => m.Market).Order()))
                return "market list differs from final audit";
            if (block.SummaryLines.Any(s => !ShortSentence(s, 260) ||
                (!legacy && (!AiNarrativeIntegrity.IsCompleteSentence(s) || AiNarrativeIntegrity.SentenceCount(s) != 1)) ||
                Regex.IsMatch(s, @"\b(bet|bets|pick|picks|recommend|selected|wette|wetten|empfehlen|empfehlung)\b", RegexOptions.IgnoreCase)))
                return "summary must contain complete context sentences without betting advice";
            if (!legacy && AiNarrativeIntegrity.WordCount(string.Join(" ", block.SummaryLines)) < 50)
                return "summary needs at least 50 words of match context, not terse labels";
            var supportedNumbers = Numbers(JsonSerializer.Serialize(input)).ToHashSet();
            if (block.SummaryLines.SelectMany(Numbers).Except(supportedNumbers).Any())
                return "summary introduces an unsupported number";
            foreach (var market in block.Markets)
            {
                var facts = input.Markets.Single(m => m.Market == market.Market).Facts;
                // One rewrite per supplied fact. The count follows the evidence
                // that actually fired, so it varies by market and by fixture.
                if (market.Checks is null || market.Checks.Count != facts.Count ||
                    market.Checks.Any(s => !ShortSentence(s, 260)))
                    return $"one short check required per supplied fact ({facts.Count} for {market.Market})";
                for (var i = 0; i < facts.Count; i++)
                    if (Numbers(market.Checks[i]).Except(Numbers(facts[i])).Any()) return "check introduces an unsupported number";
            }
        }
        return null;
    }

    private static IEnumerable<string> Numbers(string s) => Regex.Matches(s, @"\d+(?:[.,]\d+)?")
        .Select(m => decimal.TryParse(m.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            ? n.ToString("G29", CultureInfo.InvariantCulture) : m.Value);

    private static bool ShortSentence(string? text, int max) => !string.IsNullOrWhiteSpace(text) &&
        text.Length <= max && !text.Contains('\n') && !text.Contains('\r') &&
        !Regex.IsMatch(text, @"\bn/a\b|\bguaranteed\b|\bsure bet\b|\brisk.free\b|\bsichere Wette\b|\bgarantiert\b", RegexOptions.IgnoreCase);

    public static void Refresh(MatchAnalysis match, string? lang = null)
    {
        match.PresentationLanguage = lang ?? match.PresentationLanguage;
        var de = match.PresentationLanguage == "de";
        var current = IsCurrent(match, match.DecisionExplanation) || IsHistoricalLegacyCurrent(match);
        var block = de ? match.DecisionExplanation?.De : match.DecisionExplanation?.En;
        var lines = current ? block!.SummaryLines.ToList() : new List<string>();
        var markets = (match.DecisionAudit?.Markets ?? []).Where(m => m.Probability >= .5).ToList();
        // The card visibility filter must not rewrite the system's decision
        // (a configured draw threshold can, for example, be below 50%).
        // Ranked by probability, not EV: EV is null wherever no price arrived,
        // which would sort the best-evidenced call to the bottom of its own list.
        var selected = (match.DecisionAudit?.Markets ?? []).Where(m => m.Qualified)
            .OrderByDescending(m => m.Probability).ToList();
        var best = selected.FirstOrDefault();
        if (best is not null)
        {
            lines.Add(de ? $"Das System wählt: {string.Join(", ", selected.Select(m => Label(m, true)))}."
                : $"The system selects: {string.Join(", ", selected.Select(m => Label(m, false)))}.");
            lines.Add(de
                ? $"{Label(best, true)} besteht alle Checks: {Pct(best.Probability)} Chance bei benötigten {Pct(best.Threshold)}, "
                  + $"{Plural(best.ConfirmationsFired, "bestätigender Datencheck", "bestätigende Datenchecks")}, kein Ausschlusskriterium."
                : $"{Label(best, false)} passes every check: {Pct(best.Probability)} chance against the required {Pct(best.Threshold)}, "
                  + $"{Plural(best.ConfirmationsFired, "supporting evidence check", "supporting evidence checks")}, nothing ruling it out.");
        }
        else
        {
            lines.Add(de ? "Das System wählt derzeit keine Wette." : "The system currently selects no bet.");
            var top = markets.OrderByDescending(m => m.Probability).FirstOrDefault();
            lines.Add(top is null
                ? (de ? "Kein Markt erreicht die benötigte Wahrscheinlichkeit." : "No market reaches the required probability.")
                : $"{Label(top, de)}: {GateReason(top, de)}.");
        }
        match.Presentation = new(current, lines, markets.Select(m =>
        {
            var (text, outcomes) = Checks(m, match.PresentationLanguage);
            return new AiMarketExplanation
            {
                Market = m.Market,
                // The writer supplies the words; the marks are ours either way,
                // so a rewritten check cannot quietly flip its own verdict.
                Checks = current ? block!.Markets.Single(x => x.Market == m.Market).Checks : text.ToList(),
                CheckOutcomes = outcomes.ToList()
            };
        }).ToList());
    }

    // A completed fixture cannot get a new pre-match explanation. Preserve its
    // existing text only when the original v1 input hash still matches exactly.
    // Upcoming fixtures use v2 and are regenerated by the normal AI sync.
    private static bool IsHistoricalLegacyCurrent(MatchAnalysis match)
    {
        if (match.Date > DateTimeOffset.UtcNow || match.DecisionExplanation is not { } explanation)
            return false;
        var input = Input(match);
        var legacyInput = input with
        {
            Version = 1,
            Markets = input.Markets.Select(m => m with
            {
                Odds = match.DecisionAudit!.Markets.Single(a => a.Market == m.Market).Odds,
                Bookmaker = match.OddsBookmaker
            }).ToList()
        };
        return explanation.InputHash == Hash(legacyInput) && Invalid(explanation, legacyInput, true) is null;
    }

    private static string Num(double? n) => n?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>"1 Ausschlusskriterien" read as a typo, because it was one.</summary>
    private static string Plural(int count, string one, string many) =>
        $"{count} {(count == 1 ? one : many)}";
    private static string Pct(double n) => (n * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public static string Label(MarketRuleAudit m, bool de) => m.Market switch
    {
        "btts" => de ? "Beide Teams treffen" : "Both teams to score",
        "over25" => de ? "Über 2,5 Tore" : "Over 2.5 goals",
        "under25" => de ? "Unter 2,5 Tore" : "Under 2.5 goals",
        "goals_2_3" => de ? "2–3 Tore" : "2–3 goals",
        "btts_and_over25" => de ? "Beide treffen + über 2,5 Tore" : "Both score + over 2.5 goals",
        "match_winner" => m.Selection == ConfluenceRuleEngine.Selections.MatchWinnerHome
            ? (de ? "Heimsieg" : "Home win") : (de ? "Auswärtssieg" : "Away win"),
        "draw" => de ? "Unentschieden" : "Draw",
        _ => m.Selection
    };

    /// <summary>
    /// Why a market was or was not selected, in the reader's language.
    /// </summary>
    /// <remarks>
    /// Only reasons about the match appear here. A quote is not one: prices are
    /// no longer part of the decision, so "a fresh quote is unavailable" told a
    /// reader nothing about the fixture while reading as a verdict on it. The
    /// four retired price outcomes are normalised away before this point.
    /// </remarks>
    public static string GateReason(MarketRuleAudit m, bool de) => m.GateOutcome switch
    {
        GateOutcome.Qualified => de ? "Wahrscheinlichkeit und Daten bestehen alle Checks" : "probability and evidence pass all checks",
        GateOutcome.BelowProbabilityFloor => de ? "die Wahrscheinlichkeit ist zu niedrig" : "the estimated probability is too low",
        GateOutcome.Vetoed => de ? "die Daten enthalten ein Ausschlusskriterium" : "an evidence check rules this out",
        GateOutcome.AiDisagrees => de ? "die KI unterstützt die Auswahl nicht" : "the AI does not support this selection",
        GateOutcome.InformationalOnly => de ? "dieser Markt ist in den Einstellungen ausgeschlossen" : "this market is excluded by configuration",
        _ => de ? "es gibt zu wenige bestätigende Hinweise" : "there is not enough supporting evidence"
    };

    /// <summary>
    /// The checks behind one market: what the data actually says, in the order
    /// a reader works through it.
    /// </summary>
    /// <remarks>
    /// This used to report counts — "4 evidence checks support this selection"
    /// — which tells the reader a number and nothing about the match. Every
    /// rule that fired now speaks for itself, naming its team: "Barnsley scored
    /// in 3 of their last 3 home matches". The measured evidence is written in
    /// English; the writer rewrites each line into both languages, and the
    /// localised counts below survive only as the offline fallback.
    ///
    /// The list length varies with how many rules fired, so nothing downstream
    /// may assume five.
    /// </remarks>
    private static (IReadOnlyList<string> Text, IReadOnlyList<bool?> Outcomes) Checks(
        MarketRuleAudit m, string lang)
    {
        var de = lang == "de";
        var text = new List<string>();
        var outcomes = new List<bool?>();

        void Add(string line, bool? outcome)
        {
            text.Add(line);
            outcomes.Add(outcome);
        }

        Add(de ? $"Geschätzte Chance {Pct(m.Probability)}; benötigt werden {Pct(m.Threshold)}."
               : $"Estimated chance {Pct(m.Probability)}; required {Pct(m.Threshold)}.",
            m.ProbabilityPassed);

        // Measured evidence, one line each. English only — the writer produces
        // the German. Without it the fallback below keeps the reader informed.
        var fired = m.Rules
            .Where(r => r.Fired && !r.RuleId.Contains("ai_agrees") && !string.IsNullOrWhiteSpace(r.Evidence))
            .ToList();

        if (!de)
            foreach (var rule in fired)
                Add(Sentence(rule.Evidence!), rule.Kind == RuleResult.Confirm);
        else
        {
            Add(m.ConfirmationsFired == 1 ? "1 Datencheck unterstützt die Auswahl."
                    : $"{m.ConfirmationsFired} Datenchecks unterstützen die Auswahl.",
                m.ConfirmationsFired > 0);
            Add(m.VetoesFired == 0 ? "Kein Ausschlusskriterium wurde gefunden."
                    : m.VetoesFired == 1 ? "1 Ausschlusskriterium wurde gefunden."
                    : $"{m.VetoesFired} Ausschlusskriterien wurden gefunden.",
                m.VetoesFired == 0);
        }

        Add(m.AiAgrees is true ? (de ? "Die KI unterstützt diesen Markt." : "The AI supports this market.")
            : m.AiAgrees is false ? (de ? "Die KI unterstützt diesen Markt nicht." : "The AI does not support this market.")
            : (de ? "Für diesen Markt liegt keine KI-Einschätzung vor." : "No AI opinion is available for this market."),
            m.AiAgrees);

        // The verdict, in terms of the match. The bookmaker's price belongs to
        // the odds row above, not to the reasons a market was chosen.
        Add(Sentence(GateReason(m, de)), m.Qualified);

        return (text, outcomes);
    }

    /// <summary>A measured label as a sentence: capitalised, full-stopped.</summary>
    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return trimmed;
        var capitalised = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return ".!?".Contains(capitalised[^1]) ? capitalised : capitalised + ".";
    }

    private static IReadOnlyList<string> Facts(MarketRuleAudit m, string lang) =>
        Checks(m, lang).Text;
}
