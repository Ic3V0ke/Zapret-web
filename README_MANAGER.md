# Zapret Manager

Windows GUI wrapper for Flowseal zapret-discord-youtube strategies.

## Build

```powershell
dotnet publish .\ZapretManager\ZapretManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish
```

The app embeds the zapret files from the repository root and extracts them to `%LOCALAPPDATA%\ZapretManager\zapret-runtime` on first launch.
