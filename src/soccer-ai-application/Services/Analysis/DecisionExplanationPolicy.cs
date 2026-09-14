using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Services.Analysis;

/// <summary>Grounds presentation in the final audit and invalidates text after any decision/price change.</summary>
public static class DecisionExplanationPolicy
{
    public const int Version = 1;
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
            m.Probability, m.Odds, match.OddsBookmaker, Facts(m, "en"))).ToList());

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
            if (block?.SummaryLines is not { Count: 4 } || block.Markets is null) return "four summary sentences required";
            if (!block.Markets.Select(m => m.Market).Order().SequenceEqual(input.Markets.Select(m => m.Market).Order()))
                return "market list differs from final audit";
            if (block.SummaryLines.Any(s => !ShortSentence(s, 180))) return "summary sentence too long or invalid";
            foreach (var market in block.Markets)
                if (market.Checks is not { Count: 5 } || market.Checks.Any(s => !ShortSentence(s, 220)))
                    return "five short checks required per market";
        }
        return null;
    }

    private static bool ShortSentence(string? text, int max) => !string.IsNullOrWhiteSpace(text) &&
        text.Length <= max && !text.Contains('\n') && !text.Contains('\r') &&
        !Regex.IsMatch(text, @"\bn/a\b|\bguaranteed\b|\bsure bet\b|\brisk.free\b|\bsichere Wette\b|\bgarantiert\b", RegexOptions.IgnoreCase);

    public static void Refresh(MatchAnalysis match, string? lang = null)
    {
        match.PresentationLanguage = lang ?? match.PresentationLanguage;
        var de = match.PresentationLanguage == "de";
        var current = IsCurrent(match, match.DecisionExplanation);
        var block = de ? match.DecisionExplanation?.De : match.DecisionExplanation?.En;
        var lines = current ? block!.SummaryLines.ToList() : new List<string>();
        var markets = (match.DecisionAudit?.Markets ?? []).Where(m => m.Probability >= .5).ToList();
        var selected = markets.Where(m => m.Qualified).OrderByDescending(m => m.Ev).ToList();
        var best = selected.FirstOrDefault();
        if (best is not null)
        {
            lines.Add(de ? $"Das System wählt: {string.Join(", ", selected.Select(m => Label(m, true)))}."
                : $"The system selects: {string.Join(", ", selected.Select(m => Label(m, false)))}.");
            var required = Math.Max(best.MinOdds, (1 + best.MinEdge) / best.Probability);
            lines.Add(de
                ? $"{Label(best, true)} besteht die Datenchecks: {Pct(best.Probability)} Chance und Quote {Num(best.Odds)} über der benötigten {Num(required)}."
                : $"{Label(best, false)} passes the evidence checks: {Pct(best.Probability)} chance and odds {Num(best.Odds)} above the required {Num(required)}.");
        }
        else
        {
            lines.Add(de ? "Das System wählt derzeit keine Wette." : "The system currently selects no bet.");
            var top = markets.OrderByDescending(m => m.Probability).FirstOrDefault();
            lines.Add(top is null
                ? (de ? "Kein Markt erreicht die benötigte Wahrscheinlichkeit." : "No market reaches the required probability.")
                : $"{Label(top, de)}: {GateReason(top, de)}.");
        }
        match.Presentation = new(current, lines, markets.Select(m => new AiMarketExplanation
        {
            Market = m.Market,
            Checks = current ? block!.Markets.Single(x => x.Market == m.Market).Checks : Facts(m, match.PresentationLanguage).ToList()
        }).ToList());
    }

    private static string Num(double? n) => n?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";
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

    public static string GateReason(MarketRuleAudit m, bool de) => m.GateOutcome switch
    {
        GateOutcome.Qualified => de ? "Wahrscheinlichkeit, Daten und Preis bestehen alle Checks" : "probability, evidence and price pass all checks",
        "stale_odds" => de ? "es fehlt eine aktuelle Quote" : "a fresh quote is unavailable",
        GateOutcome.AnalysisOnlyNoOdds => de ? "der Anbieter liefert keine nutzbare Quote" : "the provider has no usable quote",
        GateOutcome.BelowMinOdds => de ? $"die Quote liegt unter {Num(m.MinOdds)}" : $"the odds are below {Num(m.MinOdds)}",
        GateOutcome.BelowMinEdge => de ? "die Quote ist für die geschätzte Chance zu niedrig" : "the price is too low for the estimated chance",
        GateOutcome.BelowProbabilityFloor => de ? "die Wahrscheinlichkeit ist zu niedrig" : "the estimated probability is too low",
        GateOutcome.Vetoed => de ? "die Daten enthalten ein Ausschlusskriterium" : "an evidence check rules this out",
        GateOutcome.AiDisagrees => de ? "die KI unterstützt die Auswahl nicht" : "the AI does not support this selection",
        GateOutcome.InformationalOnly => de ? "dieser Markt ist in den Einstellungen ausgeschlossen" : "this market is excluded by configuration",
        _ => de ? "es gibt zu wenige bestätigende Hinweise" : "there is not enough supporting evidence"
    };

    private static IReadOnlyList<string> Facts(MarketRuleAudit m, string lang)
    {
        var de = lang == "de";
        var support = m.Rules.FirstOrDefault(r => r.Fired && r.Kind == RuleResult.Confirm &&
            !r.RuleId.Contains("ai_agrees") && !string.IsNullOrWhiteSpace(r.Evidence));
        var risk = m.Rules.FirstOrDefault(r => r.Fired && r.Kind == RuleResult.Veto && !string.IsNullOrWhiteSpace(r.Evidence));
        // The fallback stays fully localised. The AI receives original measured
        // evidence in English and rewrites it into both languages when ready.
        return [
            de ? $"Geschätzte Chance {Pct(m.Probability)}; benötigt werden {Pct(m.Threshold)}." : $"Estimated chance {Pct(m.Probability)}; required {Pct(m.Threshold)}.",
            de ? $"{m.ConfirmationsFired} Datenchecks unterstützen die Auswahl." : support?.Evidence ?? $"{m.ConfirmationsFired} evidence checks support this selection.",
            de ? $"{m.VetoesFired} Ausschlusskriterien wurden gefunden." : risk?.Evidence ?? "No rejection check fired; this does not guarantee the outcome.",
            m.AiAgrees is true ? (de ? "Die KI unterstützt diesen Markt." : "The AI supports this market.") : m.AiAgrees is false
                ? (de ? "Die KI unterstützt diesen Markt nicht." : "The AI does not support this market.")
                : (de ? "Für diesen Markt liegt keine KI-Einschätzung vor." : "No AI opinion is available for this market."),
            (m.Odds is { } odds ? (de ? $"Quote {Num(odds)}: " : $"Odds {Num(odds)}: ") : "") + GateReason(m, de) + "."
        ];
    }
}
