using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using Microsoft.EntityFrameworkCore;
using soccer_gpt_infrastructure.Persistence;

namespace soccer_gpt_infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ApplicationDbContext>(options => 
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soccer-gpt.db");
            options.UseSqlite($"Data Source={dbPath}");
        });
        
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        
        return services;
    }
}
