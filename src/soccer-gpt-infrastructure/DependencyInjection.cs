using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<IScheduledJobService, ScheduledJobService>();
        // Or
        // services.AddTransient<IScheduledJobService, ScheduledJobService>();
        
        return services;
    }
}
