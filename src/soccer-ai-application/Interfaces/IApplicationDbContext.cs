using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<Fixture> Fixtures { get; }
    DbSet<FixtureAnalysis> FixtureAnalyses { get; }
    DbSet<Combination> Combinations { get; }
    DbSet<User> Users { get; }
    DbSet<BacktestReport> BacktestReports { get; }
    DbSet<SyncState> SyncStates { get; }
    DbSet<FixtureOddsQuote> FixtureOddsQuotes { get; }
    DbSet<PublishedTicket> PublishedTickets { get; }
    DbSet<PublishedTicketLeg> PublishedTicketLegs { get; }
    DbSet<ModelForecast> ModelForecasts { get; }

    /// <summary>
    /// Immutable pre-match probabilities, written once per fixture per capture
    /// window. The statistics endpoint scores against these rather than against
    /// <see cref="FixtureAnalyses"/>, which is a mutable cache and is rewritten
    /// on every recompute — a cache cannot be a scoring ledger.
    /// </summary>
    DbSet<PredictionSnapshot> PredictionSnapshots { get; }

    /// <summary>
    /// Reported absences, captured with the time they were read so a pre-match
    /// report can be told apart from a post-hoc confirmation.
    /// </summary>
    DbSet<FixtureInjury> FixtureInjuries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}

