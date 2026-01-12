using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;

namespace soccer_gpt_application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Match> Matches { get; }
    DbSet<Team> Teams { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
