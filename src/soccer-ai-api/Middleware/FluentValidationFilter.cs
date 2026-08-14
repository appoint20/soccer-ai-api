using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SoccerAi.Application.Models;

namespace SoccerAi.Api.Middleware;

/// <summary>
/// ASP.NET Core Action Filter that runs FluentValidation validators
/// against action parameters before the controller action executes.
/// </summary>
public class FluentValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var argumentType = argument.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
            
            if (serviceProvider.GetService(validatorType) is not IValidator validator) 
                continue;

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => ToSnakeCase(e.PropertyName))
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                context.Result = new BadRequestObjectResult(new
                {
                    status = 400,
                    message = "Validation failed",
                    errors,
                    timestamp = DateTime.UtcNow
                });
                return;
            }
        }

        await next();
    }

    /// <summary>
    /// Reports the field under the name the caller sent it as. FluentValidation
    /// names errors after the CLR property, which would hand a client reading a
    /// snake_case API a key like "PageSize" that appears nowhere in its request.
    /// Nested paths are converted per segment so "TimeFrame.StartTime" stays a
    /// path rather than becoming one long token.
    /// </summary>
    private static string ToSnakeCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : string.Join('.', propertyName
                .Split('.')
                .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
}
