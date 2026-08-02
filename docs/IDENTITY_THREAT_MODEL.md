# Identity and account threat model

## Scope and assets
Phase 2A defines identity state and interfaces but does **not** implement Microsoft, Xbox, entitlement, or profile network calls. Future authentication must use Microsoft's OAuth device-code flow or a system browser authorization-code flow with PKCE; Feather must never embed a password form or accept a client secret. Assets are access/refresh tokens, the stable provider subject, Minecraft profile, ownership result, and local cache.

## Threats and controls
| Risk | Required control / residual limitation |
|---|---|
| Token theft and replay | Keep tokens out of process arguments, settings, logs, UI, crash text, and metadata; minimize lifetime; bind browser flows with PKCE/state/nonce and reject reused callbacks. Bearer tokens remain replayable if local malware extracts them. |
| Refresh-token storage | Store only through the OS user-bound vault (Windows DPAPI/Credential Manager); never plaintext JSON. Fail closed on unsupported platforms. Delete on sign-out and data deletion. |
| Log leakage | Structured events contain host, status/category and counts only. Redact authorization/token/account fields; never log headers, tokens, complete responses, email, XUID, or subject. |
| Malicious redirects | Allow an exact registered redirect URI and loopback port rules, HTTPS except registered loopback HTTP; validate state, nonce, issuer, audience, expiry, and PKCE before accepting a result. Do not follow arbitrary redirect URIs. |
| Account switching | Complete sign-out and vault deletion before starting another account. Scope cached profile/entitlement state to the provider subject and replace state atomically. |
| Sign-out and revocation | Clear memory and vault even if remote revocation fails; attempt the provider's supported revocation/logout endpoint, report partial failure safely, and allow retry. Do not imply local deletion revokes an already stolen token. |
| Minecraft entitlement | After Microsoft and Xbox token exchange, call the official Minecraft ownership endpoint and fail closed on missing/ambiguous ownership. Never treat a profile or cached name as proof of ownership. |
| Xbox dependencies | Xbox Live and XSTS are external dependencies with consent, age, region, family, and enforcement failures. Map their safe error codes without logging responses/tokens; do not bypass them. |
| Local malware | DPAPI reduces at-rest exposure but malware running as the user can access memory or invoke user-bound decryption. This launcher cannot protect a fully compromised endpoint. |
| CI secrets | Tests use fakes only. No live client secret/token in source, examples, logs, artifacts, variables, or PRs. Repository-owner app configuration belongs in protected deployment/environment configuration. |
| User-data deletion | Sign-out removes tokens and account state. A documented full deletion removes settings, logs and metadata cache after exit; remote Microsoft data is controlled through Microsoft. |
| Offline behavior | Authentication/refresh/entitlement never succeeds from fabricated offline identity. Previously validated metadata may be browsed offline; an expired token cannot be upgraded to signed-in. |
| Failure recovery | Cancellation, timeout, network denial, consent denial, refresh failure, malformed responses and partial sign-out produce explicit recoverable/non-recoverable state. Use atomic state/storage updates and return to signed out on uncertain identity. |

## Trust boundaries and review gates
Microsoft identity, Xbox services, Minecraft services, and Mojang's metadata CDN are untrusted network inputs over TLS. Local cache is also revalidated after tampering. Before Phase 2B ships sign-in, register a public-client application, confirm permitted redirect/device-code flow and scopes, test token refresh/revocation and entitlement with owned and unowned accounts, and independently review callback and logging behavior.

## Phase 2B controls

Device code is the primary desktop flow: MSAL supplies the official verification URL/code and polls with cancellation/expiry, while Feather never hosts a browser or receives a password. The staged Microsoft → Xbox Live → XSTS → Minecraft → entitlement → profile chain treats JSON as untrusted, bounds responses, requires HTTPS, and exposes typed safe failures. Operations carry a generation so stale completion cannot replace a newer state, and overlapping sign-ins are rejected. Windows credentials use current-user DPAPI; unsupported platforms fail closed. Sign-out cancels work, removes MSAL accounts, deletes the encrypted cache, and clears profile/entitlement/tokens. Remote access tokens may remain valid until expiry because universal immediate revocation is not guaranteed.

## Phase 2C orchestration controls
Each authentication operation has a monotonically increasing generation and linked cancellation token. Sign-out/account switch invalidate the generation before clearing session material, so delayed completions cannot repopulate state. Production MSAL is behind an injectable adapter solely for deterministic testing; there is no fake production identity mode. A deterministic clock covers token/device-code expiry. Typed failures expose safe messages rather than remote payloads, including family/child restriction, missing Xbox profile, regional restriction, service denial, and unknown XSTS rejection.

Residual risks requiring live validation include tenant/application-registration policy, platform broker/MSAL cache behavior, undocumented service response changes, actual refresh/revocation timing, regional/family enforcement, and transient service failures. Logs and artifacts must be checked for Microsoft/refresh/Xbox/XSTS/Minecraft tokens, authorization headers, device codes, emails, XUIDs, UUIDs, account IDs, and token-bearing exception text.

## Phase 2D verification controls
The local public-client configuration is non-secret; material changes (enabled state, client ID, authority, scopes, or flow) invalidate the verification record. Sign-in is disabled unless GUID, HTTPS authority, required scopes, enabled state, and device-code flow validate. Checklist notes pass through redaction. Diagnostic export uses a fixed allow-list and omits identity, credential, payload, username, and path data. The confirmed clear action cancels operations, removes cached MSAL accounts where supported, deletes encrypted cache and authentication logs/state without touching unrelated launcher or Minecraft data. Residual risks include operator screenshots/clipboard capture and upstream service/account-policy variance.
