namespace SoccerAi.Application.Exceptions;

/// <summary>
/// The caller is authenticated but not permitted to perform the action — for
/// example, the wrong password on an account-delete confirmation. Maps to
/// HTTP 403 rather than 401, so the client's session-expired handler is not
/// triggered by a typo.
/// </summary>
public class ForbiddenException(string message) : Exception(message);
