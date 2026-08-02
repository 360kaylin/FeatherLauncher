namespace FeatherLauncher.Core.Models;

public abstract record AccountState;
public sealed record SignedOutAccount : AccountState;
public sealed record SigningInAccount(DateTimeOffset StartedAt) : AccountState;
public sealed record MicrosoftAuthenticatedAccount : AccountState;
public sealed record XboxAuthenticatedAccount : AccountState;
public sealed record XstsAuthenticatedAccount : AccountState;
public sealed record MinecraftAuthenticatedAccount : AccountState;
public sealed record OwnershipConfirmedAccount : AccountState;
public sealed record ProfileLoadedAccount(MinecraftProfile Profile) : AccountState;
public sealed record SignedInAccount(MicrosoftIdentity Identity, MinecraftProfile? Profile, MinecraftEntitlement Entitlement, TokenExpiry Expiry) : AccountState;
public sealed record SigningOutAccount : AccountState;
public sealed record AuthenticationFailed(string Code, string SafeMessage, bool IsRecoverable) : AccountState;
public sealed record MicrosoftIdentity(string SubjectId, AccountDisplayInfo Display);
public sealed record AccountDisplayInfo(string DisplayName);
public sealed record MinecraftProfile(string Id, string Name);
public sealed record MinecraftEntitlement(bool OwnsMinecraft, string? ProductName = null);
public sealed record TokenExpiry(DateTimeOffset ExpiresAt) { public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now; }
public sealed record DeviceCodeInfo(string VerificationUrl, string UserCode, DateTimeOffset ExpiresAt);
public enum AuthenticationFailureCategory { ConfigurationMissing, UserCancelled, UserDenied, DeviceCodeExpired, NetworkUnavailable, MicrosoftAuthenticationFailed, XboxAuthenticationFailed, XstsAuthenticationFailed, ChildOrFamilyRestriction, XboxProfileMissing, RegionRestriction, XboxServiceDenied, MinecraftAuthenticationFailed, MinecraftNotOwned, MinecraftProfileMissing, TokenExpired, TokenRevoked, SecureStorageUnavailable, UnknownFailure }
public sealed class AuthenticationException(AuthenticationFailureCategory category, string safeMessage, bool recoverable = true, Exception? inner = null) : Exception(safeMessage, inner)
{
    public AuthenticationFailureCategory Category { get; } = category;
    public bool Recoverable { get; } = recoverable;
}

public sealed record AuthenticationDiagnostics(bool IsConfigured, string Flow, string State, DateTimeOffset? TokenExpiresAt, bool SecureStorageAvailable, AuthenticationFailureCategory? LastError, bool LiveAuthenticationManuallyVerified);
public enum VerificationResult { NotTested, Pass, Fail }
public sealed record AuthenticationChecklistItem(string Scenario, VerificationResult Result, DateTimeOffset? Timestamp, string Note);
public sealed record AuthenticationChecklist(IReadOnlyList<AuthenticationChecklistItem> Items);
public sealed record AuthenticationDiagnosticsReport(string LauncherVersion, string OperatingSystem, string Architecture, bool AuthenticationConfigured, string FlowType, string AuthorityHost, IReadOnlyList<string> ScopeNames, bool SecureStorageAvailable, string CurrentState, string SafeErrorCategory, bool ManuallyVerified, DateTimeOffset GeneratedAt, IReadOnlyList<string> TestScenarioLabels);
public sealed record ManualAuthenticationVerification(bool Verified, DateTimeOffset? VerifiedAt, string AppVersion, IReadOnlyList<string> ScenarioLabels, string ConfigurationFingerprint)
{
    public static ManualAuthenticationVerification NotVerified(string fingerprint, string version) => new(false, null, version, [], fingerprint);
}

public sealed record AuthenticationConfiguration(
    bool FeatureEnabled,
    string? ClientId,
    Uri? RedirectUri,
    IReadOnlyList<string> RequiredScopes,
    string Authority,
    bool UseDeviceCode)
{
    public bool HasClientId => !string.IsNullOrWhiteSpace(ClientId);
    public bool HasValidClientId => Guid.TryParse(ClientId, out var value) && value != Guid.Empty;
    public bool HasValidAuthority => Uri.TryCreate(Authority, UriKind.Absolute, out var authority) && authority.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(authority.Host);
    public bool HasRequiredScopes => RequiredScopes.Contains("XboxLive.signin", StringComparer.Ordinal) && RequiredScopes.Contains("offline_access", StringComparer.Ordinal);
    public bool IsConfigured => FeatureEnabled && HasValidClientId && UseDeviceCode && HasValidAuthority && HasRequiredScopes;
}
