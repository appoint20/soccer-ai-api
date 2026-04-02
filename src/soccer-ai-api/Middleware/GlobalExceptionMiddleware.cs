using System.Net;
using System.Text.Json;
using SoccerAi.Application.Exceptions;

namespace SoccerAi.Api.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Maps domain exceptions to appropriate HTTP status codes and consistent response shapes.
/// </summary>
public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning(ex, "Validation error: {Message}", ex.Message);
            await HandleExceptionAsync(context, HttpStatusCode.BadRequest, ex.Message, ex.Errors);
        }
        catch (NotFoundException ex)
        {
            logger.LogWarning(ex, "Not found: {Message}", ex.Message);
            await HandleExceptionAsync(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (ExternalApiException ex)
        {
            logger.LogError(ex, "External API error ({Service}): {Message}", ex.ServiceName, ex.Message);
            var statusCode = ex.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.GatewayTimeout => HttpStatusCode.GatewayTimeout,
                _ => HttpStatusCode.BadGateway
            };
            await HandleExceptionAsync(context, statusCode, ex.Message);
        }
        catch (GeminiQuotaExceededException ex)
        {
            logger.LogWarning(ex, "Gemini quota exceeded: {Message}", ex.Message);
            await HandleExceptionAsync(context, HttpStatusCode.ServiceUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred");
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context, 
        HttpStatusCode statusCode, 
        string message,
        Dictionary<string, string[]>? errors = null)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            status = (int)statusCode,
            message,
            errors,
            timestamp = DateTime.UtcNow
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}

/// <summary>
/// Extension methods for middleware registration.
/// </summary>
public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
