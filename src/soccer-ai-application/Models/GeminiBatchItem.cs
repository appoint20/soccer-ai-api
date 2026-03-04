using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Models;

public class GeminiBatchItem
{
    public int FixtureId { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public TeamStats HomeStats { get; set; } = TeamStats.Empty;
    public TeamStats AwayStats { get; set; } = TeamStats.Empty;

    public double ModelHomeWin { get; set; }
    public double ModelDraw { get; set; }
    public double ModelAwayWin { get; set; }

    public double ModelOver25 { get; set; }
    public double ModelBTTS { get; set; }

    public double OddsHomeWin { get; set; }
    public double OddsDraw { get; set; }
    public double OddsAwayWin { get; set; }
    public double OddsOver25 { get; set; }
    public double OddsBTTS { get; set; }

    public double? HomeElo { get; set; }
    public double? AwayElo { get; set; }
}
