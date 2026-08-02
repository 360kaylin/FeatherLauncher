namespace FeatherLauncher.Core.Models;

public abstract record AccountState;
public sealed record SignedOutAccount : AccountState;
public sealed record SigningInAccount(DateTimeOffset StartedAt) : AccountState;
public sealed record SignedInAccount(MicrosoftIdentity Identity, MinecraftProfile? Profile, MinecraftEntitlement Entitlement, TokenExpiry Expiry) : AccountState;
public sealed record AuthenticationFailed(string Code, string SafeMessage, bool IsRecoverable) : AccountState;
public sealed record MicrosoftIdentity(string SubjectId, AccountDisplayInfo Display);
public sealed record AccountDisplayInfo(string DisplayName);
public sealed record MinecraftProfile(string Id, string Name);
public sealed record MinecraftEntitlement(bool OwnsMinecraft, string? ProductName = null);
public sealed record TokenExpiry(DateTimeOffset ExpiresAt) { public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now; }

public sealed record AuthenticationConfiguration(
    bool FeatureEnabled,
    string? ClientId,
    Uri? RedirectUri,
    IReadOnlyList<string> RequiredScopes,
    string Authority,
    bool UseDeviceCode)
{
    public bool IsConfigured => FeatureEnabled && Guid.TryParse(ClientId, out _) &&
        (UseDeviceCode || RedirectUri is { Scheme: "http" or "https" });
}
