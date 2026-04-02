namespace SoccerAi.Application.Exceptions;

/// <summary>
/// Base exception for all domain/application-level errors.
/// </summary>
public abstract class DomainException(string message, Exception? innerException = null)
    : Exception(message, innerException);
