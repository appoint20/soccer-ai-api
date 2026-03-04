using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.IntegrationTests;

public class CustomWebApplicationFactory<TProgram>
    : WebApplicationFactory<TProgram> where TProgram : class
{
    private string _dbPath;
    private static readonly object _dbLock = new object();

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("APIFOOTBALL_API_KEY", "dummy");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "dummy");
        Environment.SetEnvironmentVariable("Jwt__Secret", "this-is-a-very-long-secret-key-1234567890");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "soccer-ai-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "soccer-ai-api");
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "dummy");

        _dbPath = $"test_{Guid.NewGuid()}.db";
        var connectionString = $"Data Source={_dbPath}";
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services => 
        {
            // Remove the real DB context
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite($"Data Source={_dbPath}");
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (System.IO.File.Exists(_dbPath))
        {
            try { System.IO.File.Delete(_dbPath); } catch { }
        }
    }
}
