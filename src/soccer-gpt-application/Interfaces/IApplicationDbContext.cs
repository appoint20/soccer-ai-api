using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;

namespace soccer_gpt_application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<Fixture> Fixtures { get; }
    DbSet<UserCombination> UserCombinations { get; }
    DbSet<UserCombinationMatch> UserCombinationMatches { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}

