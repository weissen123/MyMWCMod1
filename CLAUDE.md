# MyMWCMod1 — Claude Code Guide

## Project Overview

A gameplay mod for **My Winter Car** built with the MSCLoader framework. Targets the taxi vehicle (MACHTWAGEN / CORRIS) and modifies drivetrain and component wear behavior.

**Features:**
- Automated Manual Transmission (AMT) with configurable shift RPMs
- Oil filter dirt accumulation rate reduced to 1% of normal
- Oil level depletion rate reduced to 1% of normal
- Headlight bulb wear rate reduced to 1% of normal

## Build

**Requirements:**
- Visual Studio (or MSBuild) with .NET Framework 3.5
- MSCLoader and PlayMaker DLLs (PlayMaker.dll is included in project)

**Build steps:**
```bash
# Open solution in Visual Studio
MyMWCMod1.sln

# Or build via MSBuild
msbuild MyMWCMod1.sln /p:Configuration=Release
```

The post-build event automatically copies the compiled DLL to the game's mod folders.

## Project Structure

```
MyMWCMod1/
├── MyMWCMod1.sln              # Visual Studio solution
└── MyMWCMod1/
    ├── MyMWCMod1.cs           # Main mod implementation
    ├── MyMWCMod1.csproj       # MSBuild project (targets .NET 3.5)
    ├── PlayMaker.dll          # PlayMaker FSM dependency
    └── Properties/
        └── AssemblyInfo.cs    # Assembly metadata
```

## Key Architecture

- **Entry point:** `MyMWCMod1.cs` — inherits from `MSCLoader.Mod`
- **Lifecycle hooks registered in `ModSetup()`:**
  - `OnLoad` → `Mod_OnLoad`: finds game objects and FSM variables once after game loads
  - `FixedUpdate` → `Mod_FixedUpdate`: runs every physics tick to apply wear reduction
  - `ModSettings` → `Mod_Settings`: registers in-game settings UI

- **Game object paths** (CORRIS is the taxi engine):
  - Drivetrain: `JOBS/TAXIJOB/MACHTWAGEN`
  - Oil pan: `CORRIS/MotorPivot/.../Oilpan(VINXX)` → FSM "Data", var "OilLevel"
  - Oil filter: `CORRIS/MotorPivot/.../VINP_Oilfilter` → FSM "Data", var "Dirt"
  - Headlights: `CORRIS/Assemblies/VINP_HeadlightLeft|Right/...` → FSM "Data", var "WearBulb"

- **Wear reduction logic** (`Mod_FixedUpdate`): each tick, if a value has changed in the undesired direction, it is rolled back 99% — reducing the effective rate of change to 1%.

## Settings (In-Game)

| Setting | ID | Default | Range |
|---|---|---|---|
| Automated Manual Transmission | `autoTransmission` | true | checkbox |
| Shift Up RPM | `shiftUpRPM` | 3500 | 1000–8000 |
| Shift Down RPM | `shiftDownRPM` | 1700 | 500–7000 |

## Notes

- Target framework is .NET 3.5 (Unity Full) — avoid APIs not available in this version
- `PlayMaker.dll` must remain in the project directory for FSM access
- `ModConsole.Error()` / `ModConsole.Log()` are used for in-game debug output
- AI-generated code must be disclosed per `AssemblyInfo.cs` comment
