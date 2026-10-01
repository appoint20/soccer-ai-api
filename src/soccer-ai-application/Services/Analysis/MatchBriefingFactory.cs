using System.Globalization;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Analysis;

public static class MatchBriefingFactory
{
    public static readonly string[] GoalMarkets = ["btts", "over25", "goals_2_3"];
    public static readonly string[] SourceNames = ["historical", "ml", "provider", "ai"];

    public static MatchBriefing Create(MatchAnalysis match, string language, DateTimeOffset? updatedAt = null)
    {
        var de = language == "de";
        var prediction = match.Prediction;
        var hasOutcomes = prediction is not null && ValidDistribution(prediction.HomeWin.Probability,
            prediction.Draw.Probability, prediction.AwayWin.Probability);
        var sources = SourceNames.Select(name =>
        {
            var source = match.CombinedPrediction?.Sources.FirstOrDefault(source => source.Name == name);
            return new BriefingSource(name, source is null ? "not_recorded"
                : source.OutcomeWeight > 0 && source.GoalsWeight > 0 ? "included"
                : source.OutcomeWeight > 0 || source.GoalsWeight > 0 ? "partial" : "unavailable", source?.CapturedAtUtc);
        }).ToList();
        var limitations = new List<string>
        {
            de ? "Die Zahlen sind Modellschätzungen, keine sicheren Ergebnisse oder Wettempfehlungen."
                : "The numbers are model estimates, not certain outcomes or betting recommendations.",
            de ? "Daten werden im Zweistundentakt geprüft. Diese Analyse ist keine Live-Einschätzung."
                : "Data is checked on a two-hour schedule. This is not a live match assessment."
        };
        if (sources.Any(source => source.Status != "included"))
            limitations.Add(de ? "Die Nutzung aller vier Analysequellen ist nicht vollständig belegt; fehlende Quellen werden nicht als Zustimmung gezählt."
                : "Use of all four analysis sources is not fully recorded; missing sources do not count as agreement.");
        if (match.CombinedPrediction is not null)
            limitations.Add(de ? "Die Quellengewichte sind experimentell. Ein Genauigkeitsvorteil der Kombination ist bisher nicht nachgewiesen."
                : "Source weights are experimental. An accuracy improvement from combining them has not yet been established.");
        if (match.H2H is not { IsValid: true })
            limitations.Add(de ? "Für einen belastbaren direkten Vergleich liegen zu wenige frühere Duelle vor."
                : "There are too few previous meetings for a reliable head-to-head comparison.");
        if (!hasOutcomes)
            limitations.Add(de ? "Für dieses Spiel liegen noch keine vollständigen Modellschätzungen vor."
                : "Complete model estimates are not yet available for this match.");

        var aiCurrent = DecisionExplanationPolicy.IsCurrent(match, match.DecisionExplanation);
        var summary = aiCurrent
            ? (de ? match.DecisionExplanation!.De : match.DecisionExplanation!.En).SummaryLines.ToList()
            : RecentForm(match, de).ToList();
        if (summary.Count == 0)
            summary.Add(de ? "Die gespeicherten Spieldaten reichen noch nicht für eine ausführliche Einordnung. Die vorhandenen Angaben werden ohne zusätzliche Annahmen angezeigt."
                : "The stored match data is not sufficient for a detailed briefing yet. Available information is shown without additional assumptions.");

        double? Probability(double? probability) => hasOutcomes && probability is { } value && ValidProbability(value) ? value : null;
        return new()
        {
            Id = match.Id, Date = match.Date, Status = match.Status, League = match.League,
            HomeTeam = match.HomeTeam, AwayTeam = match.AwayTeam, Language = de ? "de" : "en",
            AnalysisUpdatedAtUtc = updatedAt ?? match.CombinedPrediction?.CapturedAtUtc,
            HomeGoals = match.LiveHomeGoals, AwayGoals = match.LiveAwayGoals, ElapsedMinutes = match.ElapsedMinutes,
            HomeStats = match.HomeStats, AwayStats = match.AwayStats, H2H = match.H2H,
            DataStatus = !hasOutcomes ? "unavailable" : sources.All(source => source.Status == "included") ? "available" : "limited",
            SummaryLines = summary, AiGenerated = aiCurrent, Limitations = limitations,
            Sources = sources,
            Outcomes = [new("home", Probability(prediction?.HomeWin.Probability)),
                new("draw", Probability(prediction?.Draw.Probability)), new("away", Probability(prediction?.AwayWin.Probability))],
            GoalProfiles = GoalMarkets.Select(market => new BriefingGoalProfile(market,
                Probability(market switch
                {
                    "btts" => prediction?.BTTS.Probability,
                    "over25" => prediction?.Over25.Probability,
                    _ => prediction?.TwoToThreeGoals.Probability
                }), Evidence(match, market, de))).ToList()
        };
    }

    public static bool ValidProbability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
    public static bool ValidDistribution(double home, double draw, double away) =>
        ValidProbability(home) && ValidProbability(draw) && ValidProbability(away) && Math.Abs(home + draw + away - 1) < .001;

    private static IReadOnlyList<BriefingEvidence> Evidence(MatchAnalysis match, string market, bool de)
    {
        var audit = match.DecisionAudit?.Markets.FirstOrDefault(audit => audit.Market == market);
        var evidence = (audit?.Rules ?? []).Where(rule => rule.Fired && !rule.RuleId.Contains("_ai_"))
            .OrderByDescending(rule => rule.Kind == RuleResult.Veto)
            .Select(rule => new BriefingEvidence(DecisionEvidenceFormatter.FormatAnalysis(rule, match, de) ?? "",
                rule.Kind == RuleResult.Veto ? "against" : "supports"))
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && item.Text.Length <= 260)
            .DistinctBy(item => item.Text).Take(DecisionExplanationPolicy.MaximumChecks).ToList();
        foreach (var line in RecentForm(match, de))
            if (line.Length <= 260 && evidence.Count < DecisionExplanationPolicy.MaximumChecks && evidence.All(item => item.Text != line))
                evidence.Add(new(line, "context"));
        if (evidence.Count == 0)
            evidence.Add(new(de ? "Für dieses Torbild fehlen konkrete gespeicherte Spielbelege; eine Modellzahl allein erklärt den Spielverlauf nicht."
                : "No specific match evidence is recorded for this scoring pattern; a model number alone does not explain how a match unfolds.", "context"));
        return evidence;
    }

    private static IEnumerable<string> RecentForm(MatchAnalysis match, bool de)
    {
        foreach (var (recent, team) in new[] { (match.Provider?.Home, match.HomeTeam), (match.Provider?.Away, match.AwayTeam) })
        {
            if (recent is not { Played: > 0, GoalsForAverage: { } scored, GoalsAgainstAverage: { } conceded }
                || !double.IsFinite(scored) || !double.IsFinite(conceded) || scored < 0 || conceded < 0) continue;
            var culture = de ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.InvariantCulture;
            var goalsFor = scored.ToString("0.0", culture);
            var goalsAgainst = conceded.ToString("0.0", culture);
            yield return de ? $"{team} erzielte in den letzten {recent.Played} Spielen im Schnitt {goalsFor} Tore und kassierte {goalsAgainst} Gegentore pro Spiel."
                : $"In their last {recent.Played} matches, {team} averaged {goalsFor} goals scored and {goalsAgainst} conceded per match.";
        }
    }
}
