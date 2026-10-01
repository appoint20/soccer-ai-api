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
    public const int Version = 3;
    public const int MaximumChecks = 5;
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
            m.Probability, null, null, Facts(m, "en", match))).ToList(),
        (match.DecisionAudit?.Markets ?? []).Where(m => m.Qualified).Select(m => m.Market).Order().ToList());

    public static string Hash(DecisionExplanationInput input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    public static bool IsCurrent(MatchAnalysis match, AiDecisionExplanation? explanation) =>
        explanation is not null && explanation.FixtureId == match.Id &&
        explanation.InputHash == Hash(Input(match)) && Invalid(explanation, Input(match)) is null;

    public static string? Invalid(AiDecisionExplanation? result, DecisionExplanationInput input)
    {
        if (result is null || result.FixtureId != input.FixtureId) return "wrong or missing fixture";
        foreach (var block in new[] { result.En, result.De })
        {
            if (block?.SummaryLines is not { Count: >= 4 and <= 6 } || block.Markets is null)
                return "four to six context sentences required before the two final decision sentences";
            if (!block.Markets.Select(m => m.Market).Order().SequenceEqual(input.Markets.Select(m => m.Market).Order()))
                return "market list differs from final audit";
            if (block.SummaryLines.Any(s => !ShortSentence(s, 260) ||
                (!AiNarrativeIntegrity.IsCompleteSentence(s) || AiNarrativeIntegrity.SentenceCount(s) != 1) ||
                Regex.IsMatch(s, @"\b(bet|bets|pick|picks|recommend|selected|wette|wetten|empfehlen|empfehlung)\b", RegexOptions.IgnoreCase)))
                return "summary must contain complete context sentences without betting advice";
            if (AiNarrativeIntegrity.WordCount(string.Join(" ", block.SummaryLines)) < 50)
                return "summary needs at least 50 words of match context, not terse labels";
            var supportedNumbers = Numbers(JsonSerializer.Serialize(input)).ToHashSet();
            if (block.SummaryLines.SelectMany(Numbers).Except(supportedNumbers).Any())
                return "summary introduces an unsupported number";
            foreach (var market in block.Markets)
            {
                var facts = input.Markets.Single(m => m.Market == market.Market).Facts;
                if (market.Checks is null || market.Checks.Count != facts.Count || market.Checks.Count > MaximumChecks ||
                    market.Checks.Distinct(StringComparer.OrdinalIgnoreCase).Count() != market.Checks.Count ||
                    market.Checks.Any(s => !ShortSentence(s, 260)))
                    return $"one clear check required per supplied fact, at most {MaximumChecks} for {market.Market}";
                for (var i = 0; i < facts.Count; i++)
                {
                    if (Numbers(market.Checks[i]).Except(Numbers(facts[i])).Any()) return "check introduces an unsupported number";
                    if (Numbers(facts[i]).Except(Numbers(market.Checks[i])).Any()) return "check removes measured evidence";
                    if (IsOpaqueCheck(market.Checks[i])) return "check contains opaque ratings or check counts";
                    foreach (var team in new[] { input.HomeTeam, input.AwayTeam }.Where(team => !string.IsNullOrWhiteSpace(team)))
                        if (facts[i].Contains(team, StringComparison.OrdinalIgnoreCase) &&
                            !market.Checks[i].Contains(team, StringComparison.OrdinalIgnoreCase))
                            return "check removes the team name";
                }
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
        var current = IsCurrent(match, match.DecisionExplanation);
        if (!current) match.DecisionExplanation = null;
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
            var reason = Checks(best, match.PresentationLanguage, match).Text.FirstOrDefault();
            lines.Add(reason ?? (de ? "Die verfügbaren Spieldaten sprechen für diese Auswahl; der Ausgang bleibt offen."
                : "The available match evidence supports this selection; the outcome remains uncertain."));
        }
        else
        {
            lines.Add(de ? "Das System wählt derzeit keine Wette." : "The system currently selects no bet.");
            var top = markets.OrderByDescending(m => m.Probability).FirstOrDefault();
            lines.Add(top is null
                ? (de ? "Kein Markt erreicht die benötigte Wahrscheinlichkeit." : "No market reaches the required probability.")
                : Sentence($"{Label(top, de)}: {GateReason(top, de)}"));
        }
        // Every audited market gets its checks, not only those at 50% or more:
        // the app shows a card for each market, and one below 50% is still
        // analysis the reader asked to see. The writer only covers the markets
        // in its input, so the others keep the system's own wording.
        match.Presentation = new(current, lines, (match.DecisionAudit?.Markets ?? []).Select(m =>
        {
            var written = current ? block!.Markets.FirstOrDefault(x => x.Market == m.Market) : null;
            // The writer rewrote the English facts one for one, so its checks
            // take their marks from that same list; a market it did not cover
            // is worded in the reader's language.
            var (text, outcomes) = Checks(m, written is null ? match.PresentationLanguage : "en", match);
            return new AiMarketExplanation
            {
                Market = m.Market,
                // The writer supplies the words; the marks are ours either way,
                // so a rewritten check cannot quietly flip its own verdict.
                Checks = written?.Checks ?? text.ToList(),
                CheckOutcomes = outcomes.ToList()
            };
        }).ToList());
    }

    /// <summary>
    /// A goals-per-game figure in the reader's own notation.
    /// </summary>
    /// <remarks>
    /// Never the machine's culture: a German build server rendered "2,3" into
    /// the English sentence, which reads as a different number entirely.
    /// </remarks>
    private static string Goals(double value, bool de) =>
        value.ToString("0.0", de ? new CultureInfo("de-DE") : CultureInfo.InvariantCulture);

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
        GateOutcome.Qualified => de ? "die Spieldaten sprechen für diese Auswahl, ohne den Ausgang zu garantieren" : "the match evidence supports this selection, without guaranteeing the outcome",
        GateOutcome.BelowProbabilityFloor => de ? "die Wahrscheinlichkeit ist zu niedrig" : "the estimated probability is too low",
        GateOutcome.Vetoed => m.Rules.Where(r => r.Fired && r.Kind == RuleResult.Veto)
            .Select(r => DecisionEvidenceFormatter.Format(r, null, de)).FirstOrDefault(s => s is not null)
            ?? (de ? "die gespeicherte Analyse enthält keinen näher beschriebenen Ablehnungsgrund" : "the saved analysis does not describe the reason for rejecting this market"),
        GateOutcome.AiDisagrees => de ? "die KI unterstützt die Auswahl nicht" : "the AI does not support this selection",
        GateOutcome.InformationalOnly => de ? "dieser Markt ist in den Einstellungen ausgeschlossen" : "this market is excluded by configuration",
        _ => de ? "es gibt zu wenige bestätigende Hinweise" : "there is not enough supporting evidence"
    };

    public static bool IsOpaqueCheck(string text) => Regex.IsMatch(text,
        @"%|Datencheck|Ausschlusskriteri|evidence checks? support|exclusion criteri|Angriffswert|attacking strength|attack rating|head to head favours",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static (IReadOnlyList<string> Text, IReadOnlyList<bool?> Outcomes) Checks(
        MarketRuleAudit market, string lang, MatchAnalysis? match = null)
    {
        var de = lang == "de";
        var checks = new List<(string Text, bool? Outcome)>();
        void Add(string? text, bool? outcome)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 260 || checks.Count >= MaximumChecks) return;
            var sentence = Sentence(text);
            if (!IsOpaqueCheck(sentence) && !checks.Any(check => check.Text == sentence))
                checks.Add((sentence, outcome));
        }

        if (market.GateOutcome is GateOutcome.BelowProbabilityFloor or GateOutcome.InformationalOnly or GateOutcome.AiDisagrees)
            Add(GateReason(market, de), false);

        var fired = market.Rules.Where(rule => rule.Fired &&
            !rule.RuleId.Contains("_ai_") && !string.IsNullOrWhiteSpace(rule.Evidence))
            .OrderByDescending(rule => rule.Kind == RuleResult.Veto);
        foreach (var rule in fired)
            Add(DecisionEvidenceFormatter.Format(rule, match, de), rule.Kind == RuleResult.Confirm);

        if (market.GateOutcome == GateOutcome.InsufficientConfirms)
            Add(de ? "Heim-/Auswärtsform und direkte Duelle liefern noch kein ausreichend übereinstimmendes Bild für diese Auswahl."
                : "Home and away form and past meetings do not yet provide a consistent enough case for this selection.", false);

        if (match?.Provider is { } provider)
        {
            foreach (var (recent, team) in new[] { (provider.Home, match.HomeTeam), (provider.Away, match.AwayTeam) })
                if (recent is { Played: > 0, GoalsForAverage: { } scored, GoalsAgainstAverage: { } conceded }
                    && double.IsFinite(scored) && double.IsFinite(conceded) && scored >= 0 && conceded >= 0)
                    Add(de
                        ? $"{team} erzielte {Goals(scored, true)} und kassierte {Goals(conceded, true)} Tore pro Spiel in den letzten {recent.Played} Spielen."
                        : $"{team} scored {Goals(scored, false)} and conceded {Goals(conceded, false)} goals per game in their last {recent.Played} matches.", null);
        }

        if (checks.Count == 0)
            Add(de ? "Für diesen Markt fehlen konkrete gespeicherte Spielbelege; die Prognose allein reicht nicht als Begründung."
                : "No specific match evidence is stored for this market; the prediction alone does not explain the decision.", null);

        return (checks.Select(check => check.Text).ToList(), checks.Select(check => check.Outcome).ToList());
    }

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return trimmed;
        var capitalised = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return ".!?".Contains(capitalised[^1]) ? capitalised : capitalised + ".";
    }

    private static IReadOnlyList<string> Facts(MarketRuleAudit market, string lang, MatchAnalysis? match = null) =>
        Checks(market, lang, match).Text;
}
