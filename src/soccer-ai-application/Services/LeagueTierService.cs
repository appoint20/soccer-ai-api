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

    /// <summary>
    /// League ids to process, each exactly once.
    ///
    /// The Distinct matters: .NET configuration binding <em>appends</em> to an
    /// array's default value rather than replacing it, so listing Tier1 in
    /// appsettings doubles it. Callers that loop over this list — the fixture
    /// sync above all — would then spend twice the API quota doing identical
    /// work, and the duplication is invisible in every membership check.
    /// </summary>
    public IReadOnlyList<int> GetSyncLeagueIds() =>
        _options.IncludeTier2
            ? [.. _options.Tier1.Concat(_options.Tier2).Distinct()]
            : [.. _options.Tier1.Distinct()];

    public double GetQualificationThresholdBoost(int leagueId) =>
        GetTier(leagueId) == LeagueTier.Tier2
            ? _options.Tier2QualificationThresholdBoost
            : 0.0;
}
