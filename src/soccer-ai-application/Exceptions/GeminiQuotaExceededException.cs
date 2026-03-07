using System;

namespace SoccerAi.Application.Exceptions;

public class GeminiQuotaExceededException : Exception
{
    public GeminiQuotaExceededException(string message) : base(message) { }
    public GeminiQuotaExceededException(string message, Exception innerException) : base(message, innerException) { }
}
