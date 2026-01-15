using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure; // Added this using statement for DatabaseFacade

namespace soccer_gpt_application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Team> Teams { get; }
    DbSet<Match> Matches { get; }
    DbSet<Fixture> Fixtures { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
