namespace soccer_gpt_application.Models;

public sealed class StrengthFactors
{
    public double HomeExpectedGoals { get; init; }
    public double AwayExpectedGoals { get; init; }

    public double HomeAttackStrength { get; init; }
    public double HomeDefenseStrength { get; init; }
    public double AwayAttackStrength { get; init; }
    public double AwayDefenseStrength { get; init; }

    public bool IsValid => HomeExpectedGoals > 0 && AwayExpectedGoals > 0;
}
