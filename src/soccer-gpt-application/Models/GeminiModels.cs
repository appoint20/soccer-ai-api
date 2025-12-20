using System.Text.Json.Serialization;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Models
{
    public class AnalyzedMatchDto
    {
        public required string MatchId { get; set; }
        public required string HomeTeam { get; set; }
        public required string AwayTeam { get; set; }
        public required string Date { get; set; }
        public required MatchOdds Odds { get; set; }

        public required RichTeamStatsDto HomeStats { get; set; }
        public required RichTeamStatsDto AwayStats { get; set; }
        public required MatchProbabilitiesDto MathProbabilities { get; set; }
        public required StreakAnalysisDto MonteCarlo { get; set; }
        public required MatchPredictionOutput MlPrediction { get; set; }

        // AI Output
        public required string AiPrediction { get; set; }
        public required string AiReasoning { get; set; }
        public double AiConfidence { get; set; }
    }

    public class GeminiTicketResponse
    {
        [JsonPropertyName("ticket_id")]
        public int TicketId { get; set; }
        
        [JsonPropertyName("matches")]
        public List<GeminiTicketMatch> Matches { get; set; }

        [JsonPropertyName("total_odds")]
        public double TotalOdds { get; set; }

        [JsonPropertyName("analysis")]
        public string Analysis { get; set; }
    }

    public class GeminiTicketMatch
    {
        [JsonPropertyName("match")]
        public required string Match { get; set; } // "Home vs Away"
        
        [JsonPropertyName("selection")]
        public required string Selection { get; set; }

        [JsonPropertyName("odds")]
        public double Odds { get; set; }
    }
}
