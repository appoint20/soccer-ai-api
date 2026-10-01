using System.Globalization;
using System.Text.RegularExpressions;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Analysis;

public static class DecisionEvidenceFormatter
{
    public static string? FormatAnalysis(RuleResult rule, MatchAnalysis? match, bool de) => Format(rule, match, de, true);

    public static string? Format(RuleResult rule, MatchAnalysis? match, bool de, bool analysisOnly = false)
    {
        var meaning = analysisOnly ? AnalysisMeaning(rule.RuleId, de) : Meaning(rule.RuleId, de);
        if (meaning is null) return null;
        var measurements = rule.Evidence.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select((part, index) => Measurement(part, match, index, de))
            .Where(part => part is not null).Distinct().ToList();
        if (rule.RuleId is "btts_veto_failed_to_score" or "btts_veto_clean_sheets")
            measurements = rule.Evidence.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .OrderByDescending(part => Count(part))
                .Where(part => Count(part) > 0)
                .Select(part => Measurement(part, match, 0, de)).Where(part => part is not null).ToList();
        var selected = new List<string>();
        foreach (var measurement in measurements)
        {
            var candidate = string.Join("; ", selected.Append(measurement!)) + ". " + meaning;
            if (candidate.Length <= 260) selected.Add(measurement!);
        }
        return selected.Count == 0 ? analysisOnly ? null : meaning : string.Join("; ", selected) + ". " + meaning;
    }

    private static string? AnalysisMeaning(string rule, bool de) => rule switch
    {
        "btts_veto_clean_sheets" => de ? "Mindestens eine Abwehr blieb zuletzt häufig ohne Gegentor. Das spricht gegen Tore auf beiden Seiten."
            : "At least one defence has often kept a clean sheet recently. That argues against goals at both ends.",
        "btts_veto_failed_to_score" => de ? "Mindestens eine Offensive blieb zuletzt wiederholt torlos. Tore beider Teams sind deshalb keineswegs selbstverständlich."
            : "At least one attack has repeatedly failed to score recently. Goals from both teams are therefore far from assured.",
        "over25_veto_quiet_h2h" => de ? "Die direkten Duelle waren trotz sonst anfälliger Abwehrreihen torarm. Die Hinweise zur Torzahl widersprechen sich."
            : "Past meetings were low-scoring despite otherwise leaky defences. The evidence about the total is conflicting.",
        "over25_veto_dead_rubber_flat" => de ? "Die Ergebnisform beider Teams fällt ab und die Tabellenlage deutet auf wenig sportlichen Druck hin. Die tatsächliche Motivation lässt sich daraus nicht sicher ableiten."
            : "Both teams' results have declined and the table suggests limited competitive pressure. Their actual motivation cannot be established from that alone.",
        "goals23_veto_h2h_extremes" => de ? "Der Torschnitt der direkten Duelle liegt deutlich außerhalb von zwei bis drei Toren. Das ist ein Gegenargument zu diesem Torbild."
            : "Past meetings' average total lies well outside two to three goals. That is evidence against this scoring pattern.",
        _ => Meaning(rule, de)?.Replace("diese Auswahl", "dieses Torbild").Replace("dieser Auswahl", "diesem Torbild")
            .Replace("this selection", "this scoring pattern")
    };

    private static int Count(string evidence)
    {
        var count = Regex.Match(evidence, @"(?:failed to score in|kept) (\d+)");
        return count.Success ? int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static string? Measurement(string evidence, MatchAnalysis? match, int index, bool de)
    {
        var count = Regex.Match(evidence,
            @"^(?<team>.+?) (?<action>scored in|conceded in|failed to score in|kept) (?<count>\d+)(?: clean sheets)? (?:of their last|in their last) (?<sample>\d+) (?<venue>home|away) matches$");
        if (count.Success)
        {
            var team = count.Groups["team"].Value;
            var number = count.Groups["count"].Value;
            var sample = count.Groups["sample"].Value;
            var venue = count.Groups["venue"].Value;
            if (!de) return evidence;
            var action = count.Groups["action"].Value switch
            {
                "scored in" => "traf",
                "conceded in" => "kassierte Gegentore",
                "failed to score in" => "blieb ohne Tor",
                _ => "blieb ohne Gegentor"
            };
            return $"{team} {action} in {number} der letzten {sample} {(venue == "home" ? "Heimspiele" : "Auswärtsspiele")}";
        }

        var rate = Regex.Match(evidence,
            @"^(?:(?<team>.+?): )?(?<event>BTTS|Both teams scored|Over 2[.,]5|Under 2[.,]5|Draw) in (?<rate>\d+(?:[.,]\d+)?)\s*% of (?:their )?last (?<sample>\d+) (?:(?<venue>home|away) matches|(?:H2H )?meetings)$");
        if (rate.Success)
        {
            var sample = int.Parse(rate.Groups["sample"].Value, CultureInfo.InvariantCulture);
            var percent = Number(rate.Groups["rate"].Value);
            var occurrences = (int)Math.Round(percent * sample / 100);
            if (sample <= 0 || percent < 0 || percent > 100 || Math.Abs(100.0 * occurrences / sample - percent) > 0.51)
                return null;
            var team = rate.Groups["team"].Value;
            var venue = rate.Groups["venue"].Value;
            var where = team.Length == 0
                ? (de ? "direkten Duelle" : "head-to-head meetings")
                : (de ? $"{(venue == "home" ? "Heimspiele" : "Auswärtsspiele")} von {team}" : $"{venue} matches for {team}");
            var outcome = rate.Groups["event"].Value switch
            {
                "BTTS" or "Both teams scored" => de ? "trafen beide Teams" : "both teams scored",
                "Over 2.5" or "Over 2,5" => de ? "fielen mindestens drei Tore" : "at least three goals were scored",
                "Under 2.5" or "Under 2,5" => de ? "fielen höchstens zwei Tore" : "at most two goals were scored",
                _ => de ? "gab es ein Unentschieden" : "the match ended in a draw"
            };
            return de ? $"In {occurrences} der letzten {sample} {where} {outcome}"
                : $"In {occurrences} of the last {sample} {where}, {outcome}";
        }

        var average = Regex.Match(evidence, @"^Avg (?<goals>\d+(?:[.,]\d+)?) (?:total )?goals in last (?<sample>\d+) (?<type>H2H|matches)(?: despite leaky defenses)?$");
        if (average.Success)
        {
            var goals = Number(average.Groups["goals"].Value).ToString("0.0", de ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.InvariantCulture);
            var sample = average.Groups["sample"].Value;
            var h2h = average.Groups["type"].Value == "H2H";
            var team = index == 0 ? match?.HomeTeam : match?.AwayTeam;
            if (!h2h && string.IsNullOrWhiteSpace(team)) return null;
            return de ? $"In den letzten {sample} {(h2h ? "direkten Duellen" : $"Spielen von {team}")} fielen im Schnitt {goals} Tore"
                : $"The last {sample} {(h2h ? "head-to-head meetings" : $"matches for {team}")} averaged {goals} total goals";
        }
        return null;
    }

    private static double Number(string value) => double.Parse(value.Replace(',', '.'), CultureInfo.InvariantCulture);

    private static string? Meaning(string rule, bool de) => rule switch
    {
        "btts_confirm_both_score_venue" => de ? "Beide Teams treffen zuletzt regelmäßig am jeweiligen Spielort; das spricht für Tore auf beiden Seiten."
            : "Both teams have scored regularly at the relevant venue, supporting goals at both ends.",
        "btts_confirm_both_concede_venue" => de ? "Beide Abwehrreihen ließen zuletzt regelmäßig Gegentore zu; das hilft der jeweiligen gegnerischen Offensive."
            : "Both defences have conceded regularly at the relevant venue, giving each opposing attack a route to goal.",
        "btts_confirm_h2h_rate" => de ? "In den direkten Duellen trafen häufig beide Teams; das stützt diese Auswahl, ist aber keine Garantie."
            : "Both teams often scored in past meetings, supporting this selection without guaranteeing a repeat.",
        "btts_veto_clean_sheets" => de ? "Zuletzt blieb mindestens eine Abwehr häufig ohne Gegentor; deshalb wird „Beide treffen“ nicht ausgewählt."
            : "At least one defence has often kept a clean sheet recently, so both teams to score is not selected.",
        "btts_veto_failed_to_score" => de ? "Mindestens eine Offensive blieb zuletzt wiederholt torlos; deshalb wird „Beide treffen“ nicht ausgewählt."
            : "At least one attack has repeatedly failed to score recently, so both teams to score is not selected.",
        "over25_confirm_both_venue_rates" => de ? "Bei beiden Teams gab es am jeweiligen Spielort häufig mindestens drei Tore; das spricht für ein torreiches Spiel."
            : "Both teams' recent matches at the relevant venue often had at least three goals, supporting a higher total.",
        "over25_confirm_h2h_goals" => de ? "Die direkten Duelle sprechen mit ihren Torzahlen eher für ein torreiches Spiel."
            : "The scoring record in past meetings supports a higher goal total.",
        "over25_confirm_league_deviation" => de ? "Spiele beider Teams enden häufiger mit mindestens drei Toren als im Ligadurchschnitt."
            : "Both teams' matches produce at least three goals more often than the league average.",
        "over25_veto_quiet_h2h" => de ? "Die direkten Duelle waren trotz sonst anfälliger Abwehrreihen torarm; deshalb wird Über 2,5 nicht ausgewählt."
            : "Past meetings were low-scoring despite otherwise leaky defences, so over 2.5 is not selected.",
        "over25_veto_dead_rubber_flat" => de ? "Die jüngste Ergebnisform beider Teams fällt ab und die Tabellenlage bietet wenig sportlichen Druck; deshalb wird Über 2,5 nicht ausgewählt."
            : "Both teams' recent results have declined and the table suggests limited competitive pressure, so over 2.5 is not selected.",
        "over25_veto_under_profiles" => de ? "Bei beiden Teams endeten die jüngsten Spiele am jeweiligen Spielort häufig mit höchstens zwei Toren; das spricht gegen Über 2,5."
            : "Both teams' recent matches at the relevant venue often had at most two goals, arguing against over 2.5.",
        "goals23_confirm_tight_games" => de ? "Viele jüngste Spiele beider Teams wurden mit nur einem Tor Unterschied entschieden; das ist ein schwacher Hinweis, kein Beleg für die Gesamttorzahl."
            : "Many recent matches of both teams were decided by one goal, a weak clue rather than proof of the total goal count.",
        "goals23_confirm_h2h_band" => de ? "Der Torschnitt der direkten Duelle passt zum Bereich von zwei bis drei Toren; ein Durchschnitt ist kein exaktes Ergebnis."
            : "Past meetings' average total fits the two-to-three-goal range, but an average is not an exact score.",
        "goals23_confirm_moderate_totals" => de ? "Die jüngsten Spiele beider Teams hatten im Schnitt moderate Torzahlen; das passt eher zu zwei bis drei Toren."
            : "Both teams' recent matches averaged moderate totals, consistent with two to three goals.",
        "goals23_veto_chaos" => de ? "Die jüngsten Spiele mindestens eines Teams hatten sehr hohe Torzahlen; mehr als drei Tore sind daher ein wichtiges Risiko."
            : "At least one team's recent matches had very high goal totals, making more than three goals an important risk.",
        "goals23_veto_h2h_extremes" => de ? "Der Torschnitt der direkten Duelle liegt deutlich außerhalb des Bereichs von zwei bis drei Toren; deshalb wird diese Auswahl verworfen."
            : "Past meetings' average total lies well outside two to three goals, so this selection is rejected.",
        "winner_confirm_composite" => de ? "Der favorisierte Verein steht klar besser in der Tabelle, seine Ergebnisform ist stabil oder verbessert sich, und kein nahes Pokalspiel ist erfasst."
            : "The favoured team has a clear table advantage, stable or improving results, and no nearby cup fixture is recorded.",
        "winner_confirm_venue_ppg" => de ? "Der favorisierte Verein holte am jeweiligen Spielort zuletzt regelmäßig Punkte; das stützt die Siegprognose."
            : "The favoured team has regularly earned points at the relevant venue recently, supporting the win prediction.",
        "winner_confirm_h2h_dominance" => de ? "Der favorisierte Verein blieb in den jüngsten direkten Duellen ungeschlagen; das stützt ihn, bedeutet aber nicht automatisch einen Sieg."
            : "The favoured team was unbeaten in recent meetings, which supports it but does not necessarily imply a win.",
        "winner_veto_opposition_dominance" => de ? "Der Gegner blieb in den jüngsten direkten Duellen ungeschlagen; deshalb wird der favorisierte Sieg nicht ausgewählt."
            : "The opponent was unbeaten in recent meetings, so the favoured win is not selected.",
        "winner_veto_form_collapse" => de ? "Die jüngste Punkteausbeute des favorisierten Vereins liegt deutlich unter seinem Saisonniveau; deshalb wird sein Sieg nicht ausgewählt."
            : "The favoured team's recent points return is well below its season level, so its win is not selected.",
        "winner_veto_rotation_risk" => de ? "Ein zeitnahes Pokalspiel des favorisierten Vereins erhöht das Risiko einer veränderten Aufstellung; Rotation ist noch nicht bestätigt."
            : "A nearby cup fixture for the favoured team raises the risk of a changed lineup; rotation is not confirmed.",
        "under25_confirm_both_venue_rates" => de ? "Bei beiden Teams blieben die jüngsten Spiele am jeweiligen Spielort häufig bei höchstens zwei Toren."
            : "Both teams' recent matches at the relevant venue often stayed at two goals or fewer.",
        "under25_confirm_quiet_h2h" => de ? "Die direkten Duelle waren im Schnitt torarm; das spricht für höchstens zwei Tore."
            : "Past meetings were low-scoring on average, supporting at most two goals.",
        "under25_confirm_defensive_profile" => de ? "Spiele ohne Gegentor oder torlose Offensiven sprechen auf beiden Seiten für ein eher torarmes Spiel."
            : "Clean sheets or scoreless attacks on both sides support a lower-scoring match.",
        "under25_veto_chaos" or "draw_veto_chaos" => de ? "Die jüngsten Spiele mindestens eines Teams hatten sehr hohe Torzahlen; das macht diesen Ausgang weniger verlässlich."
            : "At least one team's recent matches had very high goal totals, making this outcome less dependable.",
        "under25_veto_attack_trends" => de ? "Beide Teams treffen zuletzt häufiger als im Saisonschnitt; das spricht gegen höchstens zwei Tore."
            : "Both teams have scored more frequently than their season averages recently, arguing against at most two goals.",
        "draw_confirm_tight_profiles" => de ? "Viele jüngste Spiele beider Teams wurden knapp entschieden; das deutet auf enge Spiele hin, belegt aber noch kein Unentschieden."
            : "Many recent matches of both teams were close, suggesting a tight contest but not proving a draw.",
        "draw_confirm_close_ppg" => de ? "Beide Teams holen im Saisonschnitt ähnlich viele Punkte pro Spiel; keines hat hier einen klaren Vorteil."
            : "Both teams have similar season points per game, with no clear advantage on this measure.",
        "draw_confirm_h2h_draws" => de ? "Die direkten Duelle endeten häufig unentschieden; das stützt diese Auswahl."
            : "Past meetings frequently ended in draws, supporting this selection.",
        "draw_confirm_low_scoring" => de ? "Die jüngsten Spiele beider Teams hatten niedrige Torzahlen; das passt eher zu einem torarmen Unentschieden."
            : "Both teams' recent matches had low goal totals, consistent with a low-scoring draw.",
        "draw_veto_h2h_dominance" => de ? "Eine Seite blieb in den jüngsten direkten Duellen ungeschlagen; das System verwirft deshalb das Remis, obwohl ungeschlagen auch Remis einschließt."
            : "One side was unbeaten in recent meetings; the system therefore rejects the draw, although unbeaten includes draws.",
        _ => null
    };
}
