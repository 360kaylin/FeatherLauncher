# Building
Install .NET 10 SDK (pinned by `global.json`); no Node, Java, Visual Studio, Windows machine, or client secret is needed.

```bash
dotnet restore FeatherLauncher.slnx
dotnet format FeatherLauncher.slnx --verify-no-changes --no-restore
dotnet build FeatherLauncher.slnx -c Release --no-restore
dotnet test FeatherLauncher.slnx -c Release --no-build
dotnet publish src/FeatherLauncher.Desktop/FeatherLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

## Authentication configuration (preparation only)
Copy values conceptually from `authsettings.example.json` into local environment variables: `FEATHER_AUTH_ENABLED=true`, the repository owner's registered public-client application ID in `FEATHER_MS_CLIENT_ID`, its exact loopback `FEATHER_MS_REDIRECT_URI` (or set `FEATHER_MS_USE_DEVICE_CODE=true`), `FEATHER_MS_SCOPES`, and the consumers authority. Register that redirect/flow in Microsoft Entra and enable public-client flows. Do not create or supply a client secret. Phase 2A still uses a disabled adapter; these variables only validate configuration readiness and do not enable real authentication.

## Optional authentication configuration

No authentication configuration is needed to build or test. For opt-in interactive device-code testing, follow `docs/MICROSOFT_APP_REGISTRATION.md`. Never use a client secret or automate a Microsoft password. Release validation includes `dotnet list FeatherLauncher.slnx package --vulnerable --include-transitive`.

## Authentication test policy
Coordinator tests use scripted adapters and require no credentials. Live integration testing is opt-in, disabled by default, and must never run with credentials in GitHub Actions. Before release run restore, format verification, Release build/test, win-x64 publish, ZIP integrity validation, package vulnerability scanning, and `git diff --check`.

## Live authentication test portable package
Run `dotnet publish src/FeatherLauncher.Desktop/FeatherLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64`, ZIP that directory, validate the archive, and generate `sha256sum FeatherLauncher-live-auth-test-win-x64.zip > FeatherLauncher-live-auth-test-win-x64.zip.sha256`. The manual `live-auth-test.yml` workflow performs restore, format, build, tests, vulnerability scan, publish, ZIP integrity validation, checksum generation, and upload without credentials or live sign-in.
