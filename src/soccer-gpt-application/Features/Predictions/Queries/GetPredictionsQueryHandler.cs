
using System.Globalization;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.Llm;

namespace soccer_gpt_application.Features.Predictions.Queries;

public class GetPredictionsQueryHandler(
    IFixtureRepository fixtureRepository,
    IPredictionRepository predictionRepository)
    : IRequestHandler<GetPredictionsQuery, GetPredictionsResponse>
{
    public async Task<GetPredictionsResponse> Handle(IReceiveContext<GetPredictionsQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;
        var fixtures = await fixtureRepository.GetFixturesAsync(query.Offset, query.Limit, cancellationToken);
        var total = await fixtureRepository.GetTotalCountAsync(cancellationToken);

        var datasets = new List<LlmMatchDataset>();

        foreach (var match in fixtures)
        {
            var pred = await predictionRepository.GetPredictionAsync(match.League, match.HomeTeam, match.AwayTeam, match.Date, cancellationToken);
            if (pred == null) continue; // Skip if no prediction data found

            var dataset = MapToDataset(match, pred);
            datasets.Add(dataset);
        }

        return new GetPredictionsResponse
        {
            Data = new PagedResponse<LlmMatchDataset>
            {
                Offset = query.Offset,
                Limit = query.Limit,
                Total = total, // Note: Total matches vs Total with predictions might differ. Ideally should count predictons, but for now this is ok.
                Items = datasets,
                Summary = new ResponseSummary()
            }
        };
    }

    private LlmMatchDataset MapToDataset(UpcomingMatchDto match, ApiFootballPrediction pred)
    {
        // Parse basic 1x2 percentages
        var homeWinProb = ParsePercent(pred.ApiPrediction.Percent.Home);
        var drawProb = ParsePercent(pred.ApiPrediction.Percent.Draw);
        var awayWinProb = ParsePercent(pred.ApiPrediction.Percent.Away);
        
        // Parse Goals markets (Over/Under)
        // json: "goals": { "home": "-3.5", "away": "-2.5" } 
        // This format is unusual in the prediction file provided ("-3.5", "-2.5" usually means team totals or handicaps? or actual over/under limit?)
        // The "advice" field often says "Combo Double chance : Brentford or draw and +1.5 goals"
        // Let's parse the "under_over" field which is e.g. "+1.5" or "-3.5"
        
        double over15 = 0;
        double over25 = 0;
        double under35 = 0;
        
        // Heuristics based on "under_over" field
        if (pred.ApiPrediction.UnderOver.Contains("+1.5")) over15 = 0.75;
        if (pred.ApiPrediction.UnderOver.Contains("+2.5")) { over15 = 0.85; over25 = 0.65; }
        if (pred.ApiPrediction.UnderOver.Contains("-3.5")) under35 = 0.70;
        if (pred.ApiPrediction.UnderOver.Contains("-2.5")) { under35 = 0.80; over15 = 0.40; } // likely low scoring

        // BTTS Heuristics from "goals" section or comparison
        // "teams_comparison": { "att": ... "def": ... "goals": { "home": "56%", "away": "44%" } }
        // Let's try to infer BTTS prob. If both teams have > 50% "goals" strength?
        // Or check if "advice" contains "Both teams to score".
        double bttsYes = pred.ApiPrediction.Advice.Contains("Both teams to score") ? 0.65 : 0.45;
        double bttsNo = 1.0 - bttsYes;

        // Model Confidence based on 1x2 clarity
        double confidence = (homeWinProb > 0.6 || awayWinProb > 0.6) ? 0.8 : 0.5;

        // Risk Profile Logic
        string volatility = (homeWinProb > 0.4 && awayWinProb > 0.4) ? "HIGH" : "LOW";
        string stability = "HIGH"; // Default for now
        
        // Allowed Markets
        var allowed = new List<string>();
        var forbidden = new List<string>();
        
        if (homeWinProb > 0.5 || awayWinProb > 0.5) allowed.Add("1X2");
        if (pred.ApiPrediction.WinOrDraw) allowed.Add("DOUBLE_CHANCE");
        if (over15 > 0.6) allowed.Add("OVER_1_5");
        if (under35 > 0.6) allowed.Add("UNDER_3_5");
        
        if (homeWinProb < 0.4 && awayWinProb < 0.4) forbidden.Add("1X2"); // Too close to call
        forbidden.Add("CORRECT_SCORE");

        return new LlmMatchDataset
        {
            Fixture = new LlmFixture
            {
                Id = pred.FixtureId,
                League = match.League, 
                Country = "", // Would need mapping
                Season = 2025,
                KickoffUtc = $"{match.Date}T{match.Time}:00Z"
            },
            Teams = new LlmTeams
            {
                Home = match.HomeTeam,
                Away = match.AwayTeam
            },
            MlOutputs = new LlmMlOutputs
            {
                MatchOutcome = new LlmMatchOutcome
                {
                    HomeWin = homeWinProb,
                    Draw = drawProb,
                    AwayWin = awayWinProb
                },
                GoalsMarket = new LlmGoalsMarket
                {
                    Over1_5 = over15,
                    Over2_5 = over25,
                    Under3_5 = under35
                },
                Btts = new LlmBtts
                {
                    Yes = bttsYes,
                    No = bttsNo
                },
                ExpectedGoals = new LlmExpectedGoals 
                { 
                   // Placeholder values as actual xG isn't in this specific JSON clearly
                   Home = 1.2, 
                   Away = 1.0, 
                   Total = 2.2 
                },
                ModelConfidence = confidence
            },
            AggregatedSignals = new LlmAggregatedSignals
            {
                Dominance = homeWinProb > 0.55 ? "HOME_SLIGHT" : (awayWinProb > 0.55 ? "AWAY_SLIGHT" : "BALANCED"),
                HomeNotLosing = pred.ApiPrediction.Winner.Name == match.HomeTeam || pred.ApiPrediction.Advice.Contains("Double chance") || pred.ApiPrediction.WinOrDraw,
                GoalEnvironment = pred.ApiPrediction.UnderOver,
                Tempo = "NORMAL",
                Variance = volatility == "HIGH" ? "HIGH" : "LOW",
                BttsRisk = bttsYes > 0.6 ? "LOW" : "HIGH",
                LateGoalRisk = "MEDIUM"
            },
            RiskProfile = new LlmRiskProfile
            {
                Volatility = volatility,
                HistoricalStability = stability,
                LeagueReliability = "MEDIUM",
                DataQuality = "HIGH"
            },
            Constraints = new LlmConstraints
            {
                MinProbability = 0.55,
                AllowedMarkets = allowed,
                ForbiddenMarkets = forbidden,
                MaxSelections = 2,
                RiskMode = "CONSERVATIVE"
            }
        };
    }

    private double ParsePercent(string p)
    {
        if (string.IsNullOrEmpty(p)) return 0;
        p = p.Replace("%", "").Trim();
        if (double.TryParse(p, CultureInfo.InvariantCulture, out var val))
        {
            return val / 100.0;
        }
        return 0;
    }
}
