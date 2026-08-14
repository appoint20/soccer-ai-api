using System.Net;

namespace SoccerAi.Application.Exceptions;

/// <summary>
/// Thrown when an external API call fails (rate limit, timeout, unexpected response).
/// </summary>
public class ExternalApiException : DomainException
{
    public string ServiceName { get; }
    public HttpStatusCode? StatusCode { get; }

    public ExternalApiException(string serviceName, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
        StatusCode = statusCode;
    }

    public static ExternalApiException RateLimited(string serviceName)
        => new(serviceName, $"{serviceName} rate limit exceeded. Please try again later.", HttpStatusCode.TooManyRequests);

    public static ExternalApiException Timeout(string serviceName, Exception? inner = null)
        => new(serviceName, $"{serviceName} request timed out.", HttpStatusCode.GatewayTimeout, inner);

    /// <summary>
    /// A rejected credential. Unlike a rate limit this never clears on its own,
    /// so it must stop the run rather than let it continue call after call.
    /// </summary>
    public static ExternalApiException Unauthorized(string serviceName, HttpStatusCode statusCode, string? detail = null)
        => new(serviceName,
            $"{serviceName} rejected the API key ({(int)statusCode}). "
            + "Check the credential configured for this service."
            + (string.IsNullOrWhiteSpace(detail) ? "" : $" Upstream said: {detail}"),
            statusCode);
}
