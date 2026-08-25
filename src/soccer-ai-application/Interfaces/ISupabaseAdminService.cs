namespace SoccerAi.Application.Interfaces;

/// <summary>
/// The few Supabase admin calls this API makes on its own behalf.
/// </summary>
/// <remarks>
/// Only ever called from the server: every method here uses the service_role
/// key, which bypasses row-level security entirely.
/// </remarks>
public interface ISupabaseAdminService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Re-checks a password by attempting a normal sign-in. Account deletion
    /// asks for the password again on purpose — an unlocked phone must not be
    /// enough to delete an account — and Supabase offers no other way to
    /// confirm it.
    /// </summary>
    Task<bool> VerifyPasswordAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Hard-deletes the user. Irreversible.</summary>
    Task<bool> DeleteUserAsync(string userId, CancellationToken ct = default);
}
