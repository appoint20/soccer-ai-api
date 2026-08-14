using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <inheritdoc />
public sealed class ApiCallTracker : IApiCallTracker
{
    private readonly Lock _gate = new();
    private int _attempted;
    private int _failed;
    private string? _lastError;

    public ApiCallStats Current
    {
        get
        {
            lock (_gate)
                return new ApiCallStats(_attempted, _failed, _lastError);
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
            _attempted++;
    }

    public void RecordFailure(string reason)
    {
        lock (_gate)
        {
            _attempted++;
            _failed++;
            _lastError = reason;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _attempted = 0;
            _failed = 0;
            _lastError = null;
        }
    }
}
