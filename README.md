# Bannerlord.ClanManagerEnhanced

A quality-of-life mod for Mount & Blade II: Bannerlord focused on **clan party automation** and **army management**.

**Target**: Bannerlord 1.2.12+ (e1.2 branch) and later.

## Features (all configurable via MCM)

### 1. Auto Create Party for Idle Clan Members
- Every day, if a clan member is idle in a town tavern and party slots are available, automatically creates a clan party for them.
- Prevents capable clan companions from sitting idle when they could be out earning influence.

### 2. Block Player Clan Parties from Joining External Armies
- Prevents player clan parties from joining non-player-led armies when disabled.
- Player's own army can still call clan parties as normal.
- Uses both a Harmony patch on `Army.AddParty` and a behavior-level safety net for reliability.

All actions are optional, produce (optional) notifications, and run only on the daily tick. Debug logging available for troubleshooting.

## Dependencies (load these **before** this mod)

- Bannerlord.Harmony
- Bannerlord.ButterLib (recommended)
- Bannerlord.UIExtenderEx (recommended)
- Bannerlord.MCM (Mod Configuration Menu) v5+

## Installation

1. Install the dependencies above (Workshop or Nexus).
2. Download the latest `Bannerlord.ClanManagerEnhanced-*.zip`.
3. Extract the `Bannerlord.ClanManagerEnhanced` folder into `Modules/`.
4. Enable in Launcher and place it **after** the MCM/Harmony entries in load order.
5. Start a campaign. Open **Mod Options** (ESC → Mod Options) to configure under "Clan Manager Enhanced".

## Load Order (example)

1. Native
2. SandBoxCore
3. Sandbox
4. StoryMode (optional)
5. Bannerlord.Harmony
6. Bannerlord.ButterLib
7. Bannerlord.UIExtenderEx
8. Bannerlord.MBOptionScreen (MCM)
9. **Bannerlord.ClanManagerEnhanced**
10. Everything else

## Localization (l10n)

All setting names, group headers, hints, and descriptions use `{=CME_...}` keys and are translated via `ModuleData/Languages/`.

**Included**:
- English (complete)
- 简体中文 (Simplified Chinese)

Additional languages: add a new folder under `ModuleData/Languages/<ISO>/` with `sta_strings.xml` + `language_data.xml` following the existing pattern.

## MCM Settings

After loading a campaign, go to **Mod Options**. Everything lives under **Clan Manager Enhanced**.

Settings are **global** (JSON) — not per-save.

- Master enable + notifications + debug logging
- Allow Player Clan Parties Join External Armies toggle
- Auto Create Party For Idle Clan Members toggle

## Building from Source (Unified Layout)

```
dev/
├── build.ps1
├── module/
│   ├── SubModule.xml          (uses __VERSION__)
│   └── ModuleData/Languages/...
└── src/
    └── Bannerlord.ClanManagerEnhanced/
        ├── Bannerlord.ClanManagerEnhanced.csproj
        └── *.cs
```

From the mod root:

```powershell
# Windows
.\dev\build.ps1 -Version v1.0.0

# macOS / Linux (PowerShell Core)
pwsh ./dev/build.ps1 -Version v1.0.0
```

Outputs:
- `out/Bannerlord.ClanManagerEnhanced/` (ready module)
- `out/Bannerlord.ClanManagerEnhanced-v1.0.0.zip`

Configure `GameFolder` (or `GAMEFOLDER` env var) on the command line for your local Bannerlord install.

## Development Notes

- `ArmyJoinPatches.cs`: Harmony prefix on `Army.AddParty` to block non-player army joins.
- `ClanManagementBehavior.cs`: Daily tick behavior for idle-member party creation + behavior-level army guard.
- `ClanManagerSettings.cs`: MCM settings via `AttributeGlobalSettings`.
- Player-owned only logic ensures AI clan behavior is untouched.
- Full multi-language scaffolding (EN primary + SC, with CN/CNs fallbacks).

## Credits

- [ledshic](https://github.com/ledshic)
