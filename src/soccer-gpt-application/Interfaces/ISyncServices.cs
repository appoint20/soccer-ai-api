using System;
using System.Threading;
using System.Threading.Tasks;

namespace soccer_gpt_application.Interfaces;

public interface ITeamStatsSyncService
{
    /// <summary>
    /// Step 1: Fetches team stats from API and saves raw JSON responses to Data/team_stats/
    /// </summary>
    Task SyncTeamStatsAsync(CancellationToken cancellationToken);
}

public interface ITeamMappingService
{
    /// <summary>
    /// Step 2: Reads raw JSON stats and generates Data/team_mapping.json
    /// Maps API Teams to CSV Names and stores IDs.
    /// </summary>
    Task MapTeamsAsync(CancellationToken cancellationToken);
}

public interface IFixtureGenerationService
{
    /// <summary>
    /// Step 3: Generates Data/upcoming/fixtures.csv based on mappings.
    /// Also performs Cache Warming for European Fatigue logic.
    /// </summary>
    Task GenerateFixturesAsync(CancellationToken cancellationToken);
}
