namespace SoccerAi.Api.Automation;

/// <summary>Serializes the three background automation endpoints in this API process.</summary>
public sealed class ManualAutomationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool TryEnter() => _gate.Wait(0);
    public void Exit() => _gate.Release();
}
