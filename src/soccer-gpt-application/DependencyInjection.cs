
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace soccer_gpt_application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Mediator.Net is usually configured in the entry point (API), 
        // effectively registering handlers there.
        return services;
    }
}
