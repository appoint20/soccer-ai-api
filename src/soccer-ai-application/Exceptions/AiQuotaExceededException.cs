using System;

namespace SoccerAi.Application.Exceptions;

public class AiQuotaExceededException : Exception
{
    public AiQuotaExceededException(string message) : base(message) { }
    public AiQuotaExceededException(string message, Exception innerException) : base(message, innerException) { }
}
