# Privacy
Feather Launcher has no advertising, analytics, telemetry, background service, or auto-start behavior. Phase 2A requests public Minecraft version metadata from `piston-meta.mojang.com`; that host receives ordinary network data such as IP address and user-agent/runtime HTTP behavior. Valid responses are cached locally.

No Microsoft sign-in request is implemented. The account models retain only a provider subject identifier, display name, Minecraft UUID/name, entitlement result, and token expiry when a future provider supplies them—no password, email, birthday, or payment data is required. Tokens must use Windows user-bound protected storage, never settings or plaintext JSON. Logs redact common credential/identity forms and omit response bodies, but users should inspect diagnostics before sharing.

Use Account sign-out (when implemented in Phase 2B) to remove vault records. Today, close the launcher and delete its application-data directory shown in Diagnostics to delete settings, logs, cache, and account-related local data. Microsoft-held data must be managed through Microsoft account controls.

## Microsoft authentication

When explicitly enabled, Feather opens a device-code session through MSAL and exchanges short-lived tokens with Microsoft, Xbox, and Minecraft services. Passwords are entered only on Microsoft's site and are never collected. On Windows, MSAL cache material is persisted only through current-user DPAPI storage; tokens are not placed in settings or plaintext JSON. Profile name, UUID, entitlement, and expiry are held for the active session and cleared at sign-out. Tokens, authorization headers, XUIDs, email addresses, cookies, and complete service responses are excluded from logs.
