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
                    .GroupBy(e => e.PropertyName)
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
}
