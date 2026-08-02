# Feather Launcher architecture and roadmap

## Product boundaries
Feather Launcher is an unofficial, free Minecraft: Java Edition launcher. It will authenticate only through legitimate Microsoft identity and Minecraft services, require ownership, never accept passwords directly, and never support offline/cracked account bypasses. Community capes will always be labelled unofficial and cannot become Mojang capes. No advertising, analytics, telemetry, auto-start entry, or background service is planned.

## Technology decision

**Selected:** .NET 10 LTS and C# with Avalonia UI 11.3.18. .NET 10 is the current maintained LTS line and provides nullable annotations, analyzers, modern runtime improvements, single-file/self-contained publishing, and strong CI tooling. Avalonia is a native-rendered, open-source desktop UI toolkit: it does not embed Electron or Chromium, supports Windows 10/11 x64, is comparatively lightweight at idle, follows XAML/MVVM conventions, and can be compiled/published for Windows from Linux CI. Version 11.3.18 is selected as the conservative maintained 11.x stable line rather than adopting the new major 12 line during foundation work. Versions are centrally pinned and should be reviewed with each release.

WPF/WinUI were rejected because Linux compilation and cross-platform testing are weaker. MAUI adds unnecessary mobile/workload weight. WebView/Electron approaches violate the no-Chromium requirement.

## Layered architecture

- `FeatherLauncher.Desktop`: composition root, Avalonia views/view-models, navigation, presentation state. It depends on Core and Infrastructure.
- `FeatherLauncher.Core`: dependency-free domain models, policies, use-case contracts, and cancellation-aware async service interfaces.
- `FeatherLauncher.Infrastructure`: JSON persistence, safe OS paths, redacting structured file logs, cache/file services, future HTTP/auth/runtime adapters. It depends on Core.
- `FeatherLauncher.Tests`: fast unit/integration tests using isolated temporary directories.

Dependencies point inward. Future network services use typed `HttpClient`, explicit timeouts, cancellation tokens, bounded concurrency, atomic downloads and checksum verification. Secrets belong in platform credential storage; logs receive only redacted data. Per-instance manifests isolate versions, loaders, mods, Java arguments, saves and content. Shared immutable downloads are content-addressed and cached.

## Data and security

Local application data contains versioned settings, logs, cache and instances. Paths are canonicalized below a known root. Settings are written via a temporary replacement. Corrupt or out-of-range configuration falls back to safe defaults. Logs are structured by timestamp, level and category and redact credentials/account identifiers before disk writes. Diagnostic exports will require preview and further scrubbing. Microsoft OAuth will use system-browser authorization code with PKCE/device authorization as officially supported; the launcher will never collect Microsoft passwords. Tokens will use Windows Credential Manager/DPAPI behind an abstraction.

Downloads will permit HTTPS endpoints, validate hashes/signatures when upstream metadata supplies them, prevent path traversal, and use staging followed by atomic promotion. Modrinth API use will honor its documented identification and rate limits. No executable mod is represented as safe merely because it is listed.

## Development phases

1. **Foundation (this phase):** solution/layers, dark shell and honest placeholders; persistent settings; diagnostics; safe paths; redacting logs; tests; documentation; Release CI and portable Windows ZIP.
2. **Identity and version metadata:** registered Azure application, legitimate Microsoft OAuth, secure token vault, Xbox/Minecraft entitlement and profile checks, sign-out/revocation; Mojang version manifest browsing and metadata/cache validation. No game launch yet until authentication tests and threat review pass.
3. **Vanilla instances and launch:** instance schema/migrations, Java discovery and managed Microsoft runtime acquisition, libraries/assets/natives, argument rules, process lifecycle, console/log UI, cancellation/resume, memory and resolution settings.
4. **Loaders:** independently tested Fabric, Forge, NeoForge, and possibly Quilt adapters; loader-version compatibility; install transactions and rollback. Never scrape undocumented endpoints where an official manifest exists.
5. **Content management:** documented Modrinth API client; mods/resource packs/shaders; dependency graph, incompatibility warnings, file hashes, update and disable/remove flows; loader/game-version filtering and licensing attribution.
6. **Creation tools:** 64×64 skin editor with layers/import/export and official profile upload only when authorized; cape designer; an opt-in, clearly unofficial community cape service with moderation/privacy design and no claims of Mojang visibility.
7. **Reliability and accessibility:** migration/backup recovery, offline metadata behavior, download queue, cleanup previews, keyboard/screen-reader/high-contrast/localization work, performance budgets, diagnostic bundles and broad automated tests.
8. **Distribution:** reproducible signed portable ZIP, installer/uninstaller (likely WiX or MSIX after evaluation), update manifest/signature strategy, SBOM, vulnerability and license scanning, release channels and rollback. No service, scheduled task, auto-start, telemetry, or bundled Java except a clearly documented managed runtime.

## Quality gates
Every phase requires analyzers with warnings as errors, unit and integration tests, dependency review, privacy/security documentation changes, manual Windows 10/11 smoke tests, measured (not claimed) memory/startup results, accessible controls, release checksum/SBOM, and recovery testing. Phase 2 must begin with identity threat modelling and Microsoft application-registration documentation.
