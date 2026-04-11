# MyMWCMod1 — Claude Code Guide

## Project Overview

A gameplay mod for **My Winter Car** built with the MSCLoader framework. Targets the taxi vehicle (MACHTWAGEN / CORRIS / BACHGLOTZ) and modifies drivetrain and component wear behavior.

**Features:**
- Automated Manual Transmission (AMT) with configurable shift RPMs
- Heavily reduced wear rates for oil level, oil filter dirt, headlight bulbs, spark plugs, alternator, brake fluid, heaterbox, waterpump, and head gasket
- Configurable `canStall` flag for the CORRIS engine, gated on multiple FSM bool conditions (electricity, fuel, combustion)
- Player camera pivot reset — press a configurable key to snap the in-car PLAYER transform back to a stored pose; active only when `PlayerCurrentVehicle` matches a configured vehicle name
- XML-driven configuration — all monitors, drivetrain settings, pivot reset poses, and keybind defaults live in `Mods/MyMWCMod1_monitors.xml`
- FSM CSV dumper — exports float, int, and bool variables from CORRIS or BACHGLOTZ hierarchies

> **Extension rule:** All per-vehicle and per-component configuration must be added to `Mods/MyMWCMod1_monitors.xml`, never hardcoded in C#. `WriteDefaultXml()` is the authoritative source for the default XML schema; update it whenever a new XML element or attribute is introduced.

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
  - `OnLoad` → `Mod_OnLoad`: calls `SetupMonitors()` (component + drivetrain + pivot reset monitors) — resolves game objects, FSM references, and the `PlayerCurrentVehicle` global FsmString
  - `Update` → `Mod_Update`: checks the configured keybind; reads `PlayerCurrentVehicle`; applies the matching pivot reset from XML config
  - `FixedUpdate` → `Mod_FixedUpdate`: applies wear reduction (`ComponentMonitor.ApplyReduction`) and drivetrain settings (`DrivetrainMonitor.Apply`) every physics tick
  - `ModSettings` → `Mod_Settings`: registers in-game settings UI from XML, registers the `Reset Player Pivot` and `Save Player Pivot` keybinds, and adds CSV dump buttons

### Inner Classes

| Class | Role |
|---|---|
| `ComponentMonitor` | Tracks a single `FsmFloat` wear variable; rolls back changes each tick by `factor` |
| `DrivetrainBoolSetting` | Pairs a `SettingsCheckBox` with a `Drivetrain` property setter and a list of `ConditionRef` guards |
| `DrivetrainMonitor` | Holds a `Drivetrain` reference and a list of `DrivetrainBoolSetting`; calls `Apply()` each tick |
| `ConditionRef` | Lazily resolves a `FsmBool` via staged GO → FSM → variable lookup; caches on first success; evaluates as `false` until resolved |
| `PivotResetConfig` | Holds one XML-loaded pivot reset entry: vehicle name, PLAYER GO path, local position, local euler angles; list populated at `OnLoad` |

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

**Pivot reset** (`<PivotReset>` child on any `<Monitor>`, alongside `<Drivetrain>`):
```xml
<Monitor label="MACHTWAGEN" path="JOBS/TAXIJOB/MACHTWAGEN">
  <Drivetrain>...</Drivetrain>
  <PivotReset vehicleName="Taxi"
              playerPath="JOBS/TAXIJOB/MACHTWAGEN/Functions/PlayerTrigger/DriverHeadPivot/CameraPivotPLR/Pivot/PLAYER"
              posX="0.005122664" posY="-0.6894007" posZ="0.1324202"
              rotX="0"           rotY="359.5581"   rotZ="0" />
</Monitor>
```

`vehicleName` must match the `PlayerCurrentVehicle` global PlayMaker FsmString exactly. Multiple `<PivotReset>` elements across different monitors are all collected into `_pivotResetConfigs` at load. To add a new vehicle: add a `<PivotReset>` element to the XML — no C# changes needed.

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
| Reset Player Pivot | `pivotReset` | `\` (Backslash)      | keybind |
| Save Player Pivot  | `savePivot`  | `Ctrl+\`            | keybind |

## Working Style

- **Self-enforce standards without prompting.** When the user corrects an approach or states a preference, apply it immediately and add it to `CLAUDE.md` in the same commit — do not wait to be asked.

## Coding Standards

- **Short top-level methods.** Any method that handles multiple distinct cases must delegate each case to a named helper. The top-level method should read like an outline; detail lives in the helpers.
- **Symmetrical control flow.** If one branch of a conditional ends with `continue`/`return`, parallel branches must do the same. Do not leave one branch inline while others delegate.
- **Direct guards over indirect proxies.** Use the condition that names the actual reason (e.g. `if (pivotEl != null) continue`) rather than a secondary symptom (e.g. `if (string.IsNullOrEmpty(fsmName))`).
- **No workarounds.** If a fix feels like a workaround, find the structurally correct solution before committing.
- **Inner classes own their operations, not just their data.** A class that holds data is responsible for the operations on that data. Do not reach into it from the outside to do work on its behalf — move those methods onto the class itself.
- **Eliminate dispatcher methods.** A method that does nothing but call a single method on another class adds no value. Once logic lives on the right class, remove the wrapper.
- **Inject dependencies via Init, not outer-class coupling.** When an inner class needs external state (lists, paths, global strings), inject it once through a dedicated static `Init()` method. The inner class must not reference the outer class instance.
- **Cache results of work already done.** If a helper performs a lookup as part of its own logic, store the result on the object rather than discarding it and repeating the lookup in every caller.

## Notes

- Target framework is .NET 3.5 (Unity Full) — avoid APIs not available in this version
- `PlayMaker.dll` must remain in the project directory for FSM access
- `ModConsole.Error()` / `ModConsole.Log()` are used for in-game debug output
- AI-generated code must be disclosed per `AssemblyInfo.cs` comment
