# Bannerlord.ClanManagerEnhanced

A new Bannerlord module scaffold for clan-focused quality-of-life features.

This repository follows the same unified layout and build flow used by your existing Enhanced modules.

## Structure

```text
dev/
  build.ps1
  module/
    SubModule.xml
    ModuleData/Languages/
  src/
    Bannerlord.ClanManagerEnhanced/
      Bannerlord.ClanManagerEnhanced.csproj
      SubModule.cs
      ClanManagementBehavior.cs
      ClanManagerSettings.cs
```

## Build

```powershell
.\dev\build.ps1 -Version v1.0.0
```

Outputs:
- out/Bannerlord.ClanManagerEnhanced/
- out/Bannerlord.ClanManagerEnhanced-v1.0.0.zip
