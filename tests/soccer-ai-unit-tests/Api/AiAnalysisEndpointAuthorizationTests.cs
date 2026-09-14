using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using SoccerAi.Api.Controllers;
using SoccerAi.Api.Security;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The automation controller's own policy accepts user tokens (JWT and
/// Supabase) with no role requirement. An endpoint that spends money on every
/// call must also demand the admin key, or any app user could start — and with
/// <c>force</c>, keep restarting — a paid model run over a whole matchday.
/// </summary>
public class AiAnalysisEndpointAuthorizationTests
{
    [Theory]
    [InlineData(nameof(AutomationController.RunAiAnalysisForDate))]
    [InlineData(nameof(AutomationController.GetAiAnalysisJob))]
    public void ManualAiAnalysisRequiresTheAdminKey(string action)
    {
        var method = typeof(AutomationController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;

        method.GetCustomAttributes<AuthorizeAttribute>()
            .Should().Contain(a => a.Policy == AdminApiKeyAuthenticationDefaults.PolicyName);
    }
}
