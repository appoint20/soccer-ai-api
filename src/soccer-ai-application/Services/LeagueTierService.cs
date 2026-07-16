using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services;

public sealed class LeagueTierService : ILeagueTierService
{
    private readonly LeagueTierOptions _options;
    private readonly HashSet<int> _tier1;
    private readonly HashSet<int> _tier2;

    public LeagueTierService(IOptions<LeagueTierOptions> options)
    {
        _options = options.Value;
        _tier1 = [.. _options.Tier1];
        _tier2 = [.. _options.Tier2];
    }

    public LeagueTier GetTier(int leagueId) =>
        _tier1.Contains(leagueId) ? LeagueTier.Tier1
        : _tier2.Contains(leagueId) ? LeagueTier.Tier2
        : LeagueTier.Unknown;

    public bool IsInScope(int leagueId) => GetTier(leagueId) switch
    {
        LeagueTier.Tier1 => true,
        LeagueTier.Tier2 => _options.IncludeTier2,
        _ => false
    };

    public IReadOnlyList<int> GetTier2LeagueIds() => _options.Tier2;

    public IReadOnlyList<int> GetSyncLeagueIds() =>
        _options.IncludeTier2
            ? [.. _options.Tier1, .. _options.Tier2]
            : _options.Tier1;

    public double GetQualificationThresholdBoost(int leagueId) =>
        GetTier(leagueId) == LeagueTier.Tier2
            ? _options.Tier2QualificationThresholdBoost
            : 0.0;
}
