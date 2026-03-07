using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<Fixture> Fixtures { get; }
    DbSet<FixtureAnalysis> FixtureAnalyses { get; }
    DbSet<Combination> Combinations { get; }
    DbSet<DailyCombination> DailyCombinations { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}

