# Veri Takip (C#)

Separate C# WinForms + Guna.UI2 port. The Python repository is not included or modified.

## Build

On Windows with the .NET 10 SDK:

    dotnet restore MiralayIFSExplorer.csproj
    dotnet run --project MiralayIFSExplorer.csproj -c Release -- --self-test
    dotnet publish MiralayIFSExplorer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish

The publish folder contains one VeriTakip.exe. Guna.UI2, the PowerShell data bridge, the Teknorot logo and icon, SQLite, and the .NET runtime are bundled into that file.

## IFS access

- Uses the existing PowerShell/.NET Access Provider bridge and the signed-in Windows user's IFS client cache.
- Runs SELECT queries only. It does not update, insert into, or delete IFS data.
- Shows an IFS row count before manual fetches. If the count exceeds 500, asks whether to fetch the first 500 rows. Choosing No cancels without fetching.
- Scheduling is configured per saved source from that source's context menu. The selected source's interval controls its scheduled fetch; scheduled sources show a clock icon.
- Scheduled fetches reuse the configured source and interval, and fetch up to 500 rows without an interactive count confirmation.
- The “IFS giriş bilgilerimi hatırla” checkbox is opt-in. When enabled, the bridge uses the IFS Access Provider's stored-credential API; this app does not write passwords into its own database.
- Local sources and snapshots remain in %LOCALAPPDATA%\MiralayIFSExplorer\data.db, compatible with the current Python app's SQLite schema.

## GitHub Actions

.github/workflows/build-windows.yml runs smoke checks and creates a Windows x64 single-file VeriTakip.exe artifact on pushes and manual workflow dispatch.
