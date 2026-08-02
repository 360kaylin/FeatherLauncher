# Feather Launcher

An unofficial, free, lightweight Minecraft: Java Edition launcher foundation. Phase 2A adds official Minecraft version-manifest browsing/caching plus secure identity domain boundaries. It does not authenticate, verify ownership, download game files, or launch Minecraft.

> Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.

## Current features
- Avalonia desktop shell, Account status, and Versions browser (releases by default; optional snapshots, old beta, old alpha)
- HTTPS-only parsing of Mojang's official version manifest, bounded validation, six-hour cache, manual refresh/clear, and validated offline fallback
- Microsoft account state/contracts and a Windows DPAPI token-vault adapter; unsupported platforms fail closed
- Environment-based placeholder authentication configuration with no client secret
- Local settings, diagnostics, safe paths, redacting structured logs, tests, and Windows portable CI packaging

Real Microsoft authentication still requires repository-owner public-client application registration **and Phase 2B implementation/testing**. Setting environment variables does not make sign-in work. See [BUILDING.md](BUILDING.md), [USER_GUIDE.md](USER_GUIDE.md), [PLAN.md](PLAN.md), and the [threat model](docs/IDENTITY_THREAT_MODEL.md).

## Phase 2B authentication

Optional Microsoft sign-in uses MSAL's public-client device-code flow and the official Xbox Live, XSTS, Minecraft authentication, entitlement, and profile endpoints. It is disabled unless explicitly configured; see [Microsoft app registration](docs/MICROSOFT_APP_REGISTRATION.md). Mocked automated tests do not constitute live-service verification. Downloading and launching Minecraft remain out of scope.
