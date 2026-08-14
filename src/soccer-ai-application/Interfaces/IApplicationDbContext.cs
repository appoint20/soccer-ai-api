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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}

