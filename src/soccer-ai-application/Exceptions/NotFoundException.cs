namespace SoccerAi.Application.Exceptions;

/// <summary>
/// Thrown when a requested resource is not found.
/// </summary>
public class NotFoundException(string message) : DomainException(message);
