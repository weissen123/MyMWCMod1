# MyMWCMod1 — Claude Code Guide

## Project Overview

A gameplay mod for **My Winter Car** built with the MSCLoader framework. Targets the taxi vehicle (MACHTWAGEN / CORRIS) and modifies drivetrain and component wear behavior.

**Features:**
- Automated Manual Transmission (AMT) with configurable shift RPMs
- Heavily reduced wear rates for oil level, oil filter dirt, headlight bulbs, spark plugs, alternator, brake fluid, heaterbox, waterpump, and head gasket
- Configurable `canStall` flag for the CORRIS engine, gated on multiple FSM bool conditions (electricity, fuel, combustion)
- XML-driven configuration — all monitors and drivetrain settings live in `Mods/MyMWCMod1_monitors.xml`
- FSM CSV dumper — exports float, int, and bool variables from CORRIS or BACHGLOTZ hierarchies

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
  - `OnLoad` → `Mod_OnLoad`: calls `SetupMonitors()` (component monitors) and `SetupDrivetrain()` (drivetrain monitors) — resolves game objects and FSM references
  - `FixedUpdate` → `Mod_FixedUpdate`: applies wear reduction (`ComponentMonitor.ApplyReduction`) and drivetrain settings (`DrivetrainMonitor.Apply`) every physics tick
  - `ModSettings` → `Mod_Settings`: registers in-game settings UI from XML and adds CSV dump buttons

### Inner Classes

| Class | Role |
|---|---|
| `ComponentMonitor` | Tracks a single `FsmFloat` wear variable; rolls back changes each tick by `factor` |
| `DrivetrainBoolSetting` | Pairs a `SettingsCheckBox` with a `Drivetrain` property setter and a list of `ConditionRef` guards |
| `DrivetrainMonitor` | Holds a `Drivetrain` reference and a list of `DrivetrainBoolSetting`; calls `Apply()` each tick |
| `ConditionRef` | Lazily resolves a `FsmBool` via a `Func<FsmBool>` resolver; caches on first success; evaluates as `false` until resolved |

### XML Configuration (`Mods/MyMWCMod1_monitors.xml`)

All monitors are loaded from XML; `WriteDefaultXml()` regenerates it if missing.

**Component monitor** (flat attributes, no children):
```xml
<Monitor label="OilLevel" path="CORRIS/..." fsmName="Data" fsmFloat="OilLevel"
         direction="Decreases" factor="0.0001"/>
```

**Drivetrain monitor** (nested `<Drivetrain>` → `<Setting>` → optional `<Condition>` elements):
```xml
<Monitor label="CORRIS" path="CORRIS">
  <Drivetrain>
    <Setting id="canStall" type="checkbox" label="Corris Engine can stall" default="false">
      <Condition path="CORRIS/Simulation/Electricity"       fsmName="Power"     fsmBool="ElectricsOK"  />
      <Condition path="CORRIS/Simulation/Engine/Fuel"       fsmName="FuelLine"  fsmBool="FuelOK"       />
      <Condition path="CORRIS/Simulation/Engine/Combustion" fsmName="Cylinders" fsmBool="CombustionOK" />
    </Setting>
  </Drivetrain>
</Monitor>
```

Multiple `<Condition>` elements are AND-ed. If an object is not found at load time, the `ConditionRef` is retained and retried every tick (silent until resolved).

### Wear Reduction Logic (`ComponentMonitor.ApplyReduction`)

Each tick: if the FSM float moved in the wear direction, roll back `(1 − factor)` of the change:
```
new value = previous + (raw change) × factor
```
`factor = 0.01` → 1% of normal wear rate.

### FSM CSV Dumper (`DumpToCSV` / `RecursiveCSV`)

Recursively walks a game object hierarchy and writes all PlayMaker FSM variables to a semicolon-delimited CSV. Triggered by buttons in the settings menu.

Output format: `GameObject Path;FSM Name;Type;Variable Name;Value`  
Types: `Float`, `Int`, `Bool`. FSMs with no variables emit `N/A`.

### Helper Methods

- `FindFsmBool(path, fsmName, varName, logLabel, logErrors=true)` — finds a `FsmBool` by path; pass `logErrors: false` for silent retry calls
- `FindFsmFloat(path, fsmName, floatName, logLabel)` — same pattern for floats

## Settings (In-Game)

| Setting | ID | Default | Range |
|---|---|---|---|
| Automated Manual Transmission | `autoTransmission` | true | checkbox |
| Shift Up RPM | `shiftUpRPM` | 3500 | 1000–8000 |
| Shift Down RPM | `shiftDownRPM` | 1700 | 500–7000 |
| Corris Engine can stall | `canStall` | false | checkbox |

## Notes

- Target framework is .NET 3.5 (Unity Full) — avoid APIs not available in this version
- `PlayMaker.dll` must remain in the project directory for FSM access
- `ModConsole.Error()` / `ModConsole.Log()` are used for in-game debug output
- AI-generated code must be disclosed per `AssemblyInfo.cs` comment
