# Building

## Prerequisites
Install the .NET 10 SDK. The repository pins the SDK in `global.json`; workloads, Node, Java, Visual Studio and a Windows machine are not required for ordinary builds.

```bash
dotnet restore FeatherLauncher.slnx
dotnet build FeatherLauncher.slnx -c Release --no-restore
dotnet test FeatherLauncher.slnx -c Release --no-build
```

Publish Windows x64 from Linux or Windows:

```bash
dotnet publish src/FeatherLauncher.Desktop/FeatherLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

The self-contained output includes the .NET runtime. GitHub Actions creates the ZIP. A signed installer is deferred.
