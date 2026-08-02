using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Authentication;

internal static class AuthenticationHttp
{
    internal const int MaxBytes = 1024 * 1024;
    internal static async Task<JsonDocument> SendAsync(HttpClient http, HttpRequestMessage request, AuthenticationFailureCategory category, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new AuthenticationException(category, "The authentication service address is invalid.", false);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new AuthenticationException(category, "The authentication service rejected the request.");
            if (response.Content.Headers.ContentLength is > MaxBytes) throw new AuthenticationException(category, "The authentication response was invalid.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var destination = new MemoryStream(); var buffer = new byte[8192]; var total = 0; int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0) { total += count; if (total > MaxBytes) throw new AuthenticationException(category, "The authentication response was invalid."); await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken); }
            destination.Position = 0; return await JsonDocument.ParseAsync(destination, new JsonDocumentOptions { MaxDepth = 24 }, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AuthenticationException(AuthenticationFailureCategory.NetworkUnavailable, "The authentication service timed out."); }
        catch (HttpRequestException ex) { throw new AuthenticationException(AuthenticationFailureCategory.NetworkUnavailable, "The authentication service could not be reached.", true, ex); }
        catch (JsonException ex) { throw new AuthenticationException(category, "The authentication service returned an invalid response.", true, ex); }
    }
    internal static string Required(JsonElement element, string name, AuthenticationFailureCategory category)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new AuthenticationException(category, "The authentication service returned an incomplete response.");
        return value.GetString()!;
    }
}

public sealed class XboxAuthenticationService(HttpClient http) : IXboxAuthenticationService
{
    public static readonly Uri Endpoint = new("https://user.auth.xboxlive.com/user/authenticate");
    public async Task<XboxAuthenticationResult> AuthenticateAsync(string microsoftAccessToken, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftAccessToken }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        using var document = await AuthenticationHttp.SendAsync(http, request, AuthenticationFailureCategory.XboxAuthenticationFailed, cancellationToken);
        return Parse(document.RootElement);
    }
    public static XboxAuthenticationResult Parse(JsonElement root)
    {
        var token = AuthenticationHttp.Required(root, "Token", AuthenticationFailureCategory.XboxAuthenticationFailed);
        try { var claims = root.GetProperty("DisplayClaims").GetProperty("xui"); if (claims.ValueKind != JsonValueKind.Array || claims.GetArrayLength() != 1) throw new InvalidOperationException(); return new(token, AuthenticationHttp.Required(claims[0], "uhs", AuthenticationFailureCategory.XboxAuthenticationFailed)); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { throw new AuthenticationException(AuthenticationFailureCategory.XboxAuthenticationFailed, "Xbox authentication returned an incomplete response."); }
    }
}

public sealed class XstsAuthorizationService(HttpClient http) : IXstsAuthorizationService
{
    public static readonly Uri Endpoint = new("https://xsts.auth.xboxlive.com/xsts/authorize");
    public async Task<XstsAuthorizationResult> AuthorizeAsync(string xboxToken, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.Content.Headers.ContentLength is > AuthenticationHttp.MaxBytes) throw new AuthenticationException(AuthenticationFailureCategory.XstsAuthenticationFailed, "Xbox authorization returned an invalid response.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken); using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 24 }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = document.RootElement.TryGetProperty("XErr", out var value) && value.TryGetInt64(out var parsed) ? parsed : 0;
                throw MapError(code);
            }
            return Parse(document.RootElement);
        }
        catch (HttpRequestException ex) { throw new AuthenticationException(AuthenticationFailureCategory.NetworkUnavailable, "The Xbox authorization service could not be reached.", true, ex); }
        catch (JsonException ex) { throw new AuthenticationException(AuthenticationFailureCategory.XstsAuthenticationFailed, "Xbox authorization returned an invalid response.", true, ex); }
    }
    public static XstsAuthorizationResult Parse(JsonElement root)
    {
        var token = AuthenticationHttp.Required(root, "Token", AuthenticationFailureCategory.XstsAuthenticationFailed);
        try { return new(token, AuthenticationHttp.Required(root.GetProperty("DisplayClaims").GetProperty("xui")[0], "uhs", AuthenticationFailureCategory.XstsAuthenticationFailed)); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { throw new AuthenticationException(AuthenticationFailureCategory.XstsAuthenticationFailed, "Xbox authorization returned an incomplete response."); }
    }
    public static AuthenticationException MapError(long xerr) => xerr switch
    {
        2148916233 => new(AuthenticationFailureCategory.XboxProfileMissing, "This Microsoft account does not have an Xbox profile."),
        2148916235 or 2148916236 => new(AuthenticationFailureCategory.RegionRestriction, "Xbox authentication is unavailable for this account's region."),
        2148916237 or 2148916238 => new(AuthenticationFailureCategory.ChildOrFamilyRestriction, "A family organizer must allow Xbox access for this account."),
        2148916227 => new(AuthenticationFailureCategory.XboxServiceDenied, "Xbox services denied this account's authentication request."),
        _ => new(AuthenticationFailureCategory.XstsAuthenticationFailed, "Xbox authorization failed.")
    };
}

public sealed class MinecraftAuthenticationService(HttpClient http) : IMinecraftAuthenticationService
{
    public static readonly Uri Endpoint = new("https://api.minecraftservices.com/authentication/login_with_xbox");
    public async Task<MinecraftAuthenticationResult> AuthenticateAsync(string userHash, string xstsToken, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        using var document = await AuthenticationHttp.SendAsync(http, request, AuthenticationFailureCategory.MinecraftAuthenticationFailed, cancellationToken); return Parse(document.RootElement, DateTimeOffset.UtcNow);
    }
    public static MinecraftAuthenticationResult Parse(JsonElement root, DateTimeOffset now)
    {
        var token = AuthenticationHttp.Required(root, "access_token", AuthenticationFailureCategory.MinecraftAuthenticationFailed);
        if (!root.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out var seconds) || seconds is <= 0 or > 86400) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftAuthenticationFailed, "Minecraft authentication returned an invalid expiry.");
        return new(token, now.AddSeconds(seconds));
    }
}

public sealed class MinecraftEntitlementService(HttpClient http) : IMinecraftEntitlementService
{
    private static readonly Uri Endpoint = new("https://api.minecraftservices.com/entitlements/mcstore");
    public async Task<MinecraftEntitlement> VerifyAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = await AuthenticationHttp.SendAsync(http, request, AuthenticationFailureCategory.MinecraftAuthenticationFailed, cancellationToken);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftAuthenticationFailed, "Minecraft ownership returned an invalid response.");
        var owned = items.EnumerateArray().Any(x => x.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is "game_minecraft" or "product_minecraft");
        return new(owned, owned ? "Minecraft: Java Edition" : null);
    }
}

public sealed partial class MinecraftProfileService(HttpClient http) : IMinecraftProfileService
{
    private static readonly Uri Endpoint = new("https://api.minecraftservices.com/minecraft/profile");
    [GeneratedRegex("^[0-9a-fA-F]{32}$")] private static partial Regex IdPattern();
    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")] private static partial Regex NamePattern();
    public async Task<MinecraftProfile> GetAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = await AuthenticationHttp.SendAsync(http, request, AuthenticationFailureCategory.MinecraftProfileMissing, cancellationToken); return Parse(document.RootElement);
    }
    public static MinecraftProfile Parse(JsonElement root)
    {
        var id = AuthenticationHttp.Required(root, "id", AuthenticationFailureCategory.MinecraftProfileMissing); var name = AuthenticationHttp.Required(root, "name", AuthenticationFailureCategory.MinecraftProfileMissing);
        if (!IdPattern().IsMatch(id)) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftProfileMissing, "The Minecraft profile identifier is invalid.");
        if (!NamePattern().IsMatch(name)) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftProfileMissing, "The Minecraft profile name is invalid.");
        return new(id.ToLowerInvariant(), name);
    }
}
