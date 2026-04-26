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
  - `OnLoad` → `Mod_OnLoad`: calls `SetupMonitors()` then `PivotResetConfig.Init(xmlPath)` — resolves game objects and FSM references
  - `Update` → `Mod_Update`: calls `TorqueConverterSimulator.UpdateAll()` and `DrivetrainStatisticsCollector.UpdateAll()`, then dispatches keybind presses to `PivotResetConfig.ResetCurrentPivot()` or `PivotResetConfig.SaveCurrentPivot()`
  - `FixedUpdate` → `Mod_FixedUpdate`: calls `ComponentMonitor.ApplyAll()`, `DrivetrainMonitor.ApplyAll()`, `TorqueConverterSimulator.ApplyAll()`, and `DrivetrainStatisticsCollector.CollectAll()` every physics tick
  - `ModSettings` → `Mod_Settings`: calls `DrivetrainMonitor.RegisterSettings(XmlPath)` to register in-game settings UI from XML, registers the `Reset Player Pivot` and `Save Player Pivot` keybinds, and adds CSV dump buttons
  - `OnGUI` → `Mod_OnGUI`: calls `DrivetrainStatisticsCollector.DrawAll()` and `TorqueConverterSimulator.DrawAll()` to render overlays

### Inner Classes

| Class | Role |
|---|---|
| `ComponentMonitor` | Tracks a single `FsmFloat` wear variable; rolls back changes each tick by `factor`. Owns its static `_instances` list; `LoadFromXml` constructs from XML; `ApplyAll` iterates each tick. |
| `DrivetrainMonitor` | Holds a `Drivetrain` reference and a list of `DrivetrainBoolSetting`; owns `_instances`, settings dicts, setter dicts, and nested classes. `RegisterSettings` registers UI; `LoadFromXml` constructs from XML; `ApplyAll` iterates each tick. |
| `DrivetrainMonitor.ICondition` | Interface for condition types: `bool Evaluate()` and `bool IsResolved`. |
| `DrivetrainMonitor.FsmBoolCondition` | Lazily resolves a `FsmBool` via staged GO → FSM → variable lookup; caches on first success; evaluates as `false` until resolved. |
| `DrivetrainMonitor.ComponentFloatCondition` | Lazily resolves a Unity component field via staged GO → `GetComponent(name)` → reflection `GetField`; evaluates as `field >= minFloat`. |
| `DrivetrainMonitor.DrivetrainBoolSetting` | Pairs a `SettingsCheckBox` with a `Drivetrain` property setter and a list of `ICondition` guards. |
| `PivotResetConfig` | Self-contained pivot reset unit. Owns its static `_configs` list internally. Static `Init(xmlPath)` injects the xml path and resolves the vehicle `FsmString`. Static `Add`, `ResetCurrentPivot`, `SaveCurrentPivot` are the public interface. `Resolve()` finds the matching config and caches the active `GameObject` on the instance. `WriteToXml()` persists the pose back to the XML file. |
| `GameObjectCsvDumper` | Recursively walks a GameObject hierarchy and writes all PlayMaker FSM variables to a CSV. `Dump(rootName)` is the single public entry point. |
| `DrivetrainCsvDumper` | Finds all `Drivetrain` components under a root GameObject via `GetComponentsInChildren` and writes their public instance fields to a CSV via reflection. `Dump(rootName)` is the single public entry point. |
| `DrivetrainStatisticsCollector` | Per-tick Drivetrain field recorder. Toggled by a configurable hotkey (`Input.GetKeyDown`); shows a top-center overlay while active; writes a timestamped CSV on stop. Fields resolved at load time via reflection. Owns its static `_instances` list; `LoadFromXml` constructs from `<Statistics>` inside `<Drivetrain>`; `UpdateAll`/`CollectAll`/`DrawAll` are the tick-dispatch entry points. |
| `TorqueConverterSimulator` | Per-tick torque converter physics. `ω_in` is read from game's `engineAngularVelo` each tick; `ω_out` is integrated via `ω_out += T_out / I_eff × dt` when TC is active (throttle ≥ 0.15, drop ≤ 0.05/tick, R ≥ 1.05, both FSM floats resolved). In `on` mode writes `differentialSpeed`, `finalDriveRatio`, `netTorque`, `frictionTorque` back. Seeds from game on first tick or gear change; skips when engine off or gear not in table. Owns `_instances`; `LoadFromXml` constructs from `<TorqueConverter>` inside `<Drivetrain>`; `UpdateAll`/`ApplyAll`/`DrawAll` are the tick-dispatch entry points. |
| `TorqueConverterSimulator.DeferredFsmFloat` | Staged GO → FSM → variable lookup for a single `FsmFloat`; caches each stage; `TryResolve()` is a no-op once resolved. Used to lazily bind `vehicleMass` and `wheelRadius`. |

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
      <Condition path="CORRIS" CompName="Drivetrain" varFloat="rpm" minFloat="400" />
    </Setting>
  </Drivetrain>
</Monitor>
```

Two `<Condition>` forms are supported (discriminated by attribute presence):
- **FsmBool** — requires `path`, `fsmName`, `fsmBool`: resolves a PlayMaker bool variable; evaluates its value.
- **ComponentFloat** — requires `path`, `CompName`, `varFloat`, `minFloat`: resolves a Unity component field via reflection; evaluates `field >= minFloat`.

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

`vehicleName` must match the `PlayerCurrentVehicle` global PlayMaker FsmString exactly. Multiple `<PivotReset>` elements across different monitors are all collected into `PivotResetConfig._configs` at load. To add a new vehicle: add a `<PivotReset>` element to the XML — no C# changes needed.

Multiple `<Condition>` elements are AND-ed. If an object is not found at load time, the condition is retained and retried every tick (silent until resolved).

**Statistics collector** (`<Statistics>` child inside `<Drivetrain>`, alongside `<Setting>` elements):
```xml
<Drivetrain>
  <Setting .../>
  <Statistics fileName="MWC_Drivetrain_Stat_CORRIS" KeyCode="KeypadDivide">
    <Statistic field="torque" />
    <Statistic field="rpm" />
  </Statistics>
</Drivetrain>
```

- `fileName`: base name for the output file; timestamp is appended automatically (`fileName_yyyyMMdd_HHmmss.csv`).
- `KeyCode`: Unity `KeyCode` enum name (case-insensitive). Press to show overlay and start collecting; press again to hide overlay and discard data. Press `Ctrl+KeyCode` to save collected data to a timestamped CSV and reset the buffer (overlay stays visible, collection continues).
- `<Statistic field="...">`: each `field` must match a `Drivetrain` field name (public or private, instance). Unknown fields are logged and skipped at load.
- `live="X"` (optional): marks a field for live display in the overlay during collection. Omit the attribute to record in CSV only.
- CSV is sampled at 10 Hz (one row per 100 ms); overlay updates every OnGUI frame.
- CSV format: `Time(s);field1;field2;...` with `InvariantCulture` decimal separators.
- Statistics registration is independent of `<Setting>` — a `<Drivetrain>` with only `<Statistics>` (no `<Setting>` elements) creates a collector with no `DrivetrainMonitor`.

**Torque converter** (`<TorqueConverter>` child inside `<Drivetrain>`, alongside `<Setting>` and `<Statistics>`):
```xml
<TorqueConverter mode="on" KeyCode="KeypadEnter" RPMStall="2000" rStall="2">
  <vehicleMass path="CORRIS/Simulation/CarData" fsmName="GetWeight" fsmFloat="Mass" />
  <wheelRadius path="CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RL/tire/VINP_WheelRL" fsmName="Data" fsmFloat="TireRadius" />
  <Gearbox>
    <GearRatio gear="2" ratio="10.6116" />
    <GearRatio gear="3" ratio="6.438" />
    <GearRatio gear="4" ratio="4.44" />
  </Gearbox>
</TorqueConverter>
```

- `mode`: `on` — integrate and write all five drivetrain fields + overlay; `display` — integrate and show overlay only, no write-back; `off` — skip loading entirely.
- `RPMStall`: engine speed at stall in RPM — converted to rad/s internally (`wStall = RPMStall × 2π / 60`).
- `rStall`: torque ratio at stall (T_out / T_drag when ν = 0).
- `KeyCode` (optional): Unity `KeyCode` enum name (case-insensitive) — press at runtime to toggle between `on` and `display` modes. Omit to disable toggle. Has no effect when initial `mode` is `off` (instance not loaded).
- `<vehicleMass path="..." fsmName="..." fsmFloat="...">`: lazily resolved FSM float for vehicle mass (kg) — used to compute I_eff = m × r_wheel² / gearRatio².
- `<wheelRadius path="..." fsmName="..." fsmFloat="...">`: lazily resolved FSM float for wheel radius (m).
- Both are resolved via staged GO → FSM → variable lookup; resolution is retried every tick until successful. Integration and write-back are suppressed until both are resolved.
- `<Gearbox>`: required wrapper containing one `<GearRatio gear="N" ratio="V">` per active gear. Active gears only; other gears skip the simulation tick.
- Per tick: reads `ω_in` from game's `engineAngularVelo`; integrates `ω_out += T_out / I_eff × dt` when TC active; seeds `ω_out` from game on first tick or gear change. In `on` mode writes `differentialSpeed`, `finalDriveRatio`, `netTorque`, `frictionTorque` back. Skips when engine off or not in an active gear.

### Wear Reduction Logic (`ComponentMonitor.ApplyReduction`)

Each tick: if the FSM float moved in the wear direction, roll back `(1 − factor)` of the change:
```
new value = previous + (raw change) × factor
```
`factor = 0.01` → 1% of normal wear rate.

### FSM CSV Dumper (`GameObjectCsvDumper.Dump`)

Recursively walks a game object hierarchy and writes all PlayMaker FSM variables to a semicolon-delimited CSV. Triggered by buttons in the settings menu. Entry point: `GameObjectCsvDumper.Dump(rootName)`.

Output format: `GameObject Path;FSM Name;Type;Variable Name;Value`  
Types: `Float`, `Int`, `Bool`. FSMs with no variables emit `N/A`.

## Settings (In-Game)

| Setting | ID | Default | Range |
|---|---|---|---|
| Automated Manual Transmission | `automatic` | true | checkbox |
| Shift Up RPM | `shiftUpRPM` | 3500 | 1000–8000 |
| Shift Down RPM | `shiftDownRPM` | 1700 | 500–7000 |
| Corris Engine can stall | `canStall` | false | checkbox |
| Reset Player Pivot | `pivotReset` | `\` (Backslash)      | keybind |
| Save Player Pivot  | `savePivot`  | `Ctrl+\`            | keybind |

## Working Style

- **Self-enforce standards without prompting.** When the user corrects an approach or states a preference, apply it immediately and add it to `CLAUDE.md` in the same commit — do not wait to be asked.
- **"Make plan" always means plan mode.** When the user asks for a plan, write it to the plan file and call `ExitPlanMode` before implementing anything — regardless of whether a plan mode session is active.

## Coding Standards

- **Short top-level methods.** Any method that handles multiple distinct cases must delegate each case to a named helper. The top-level method should read like an outline; detail lives in the helpers.
- **Symmetrical control flow.** If one branch of a conditional ends with `continue`/`return`, parallel branches must do the same. Do not leave one branch inline while others delegate.
- **Direct guards over indirect proxies.** Use the condition that names the actual reason (e.g. `if (pivotEl != null) continue`) rather than a secondary symptom (e.g. `if (string.IsNullOrEmpty(fsmName))`).
- **No workarounds.** If a fix feels like a workaround, find the structurally correct solution before committing.
- **Inner classes own their operations, not just their data.** A class that holds data is responsible for the operations on that data. Do not reach into it from the outside to do work on its behalf — move those methods onto the class itself.
- **Eliminate dispatcher methods.** A method that does nothing but call a single method on another class adds no value. Once logic lives on the right class, remove the wrapper.
- **Inject dependencies via Init, not outer-class coupling.** When an inner class needs external state (lists, paths, global strings), inject it once through a dedicated static `Init()` method. The inner class must not reference the outer class instance.
- **Cache results of work already done.** If a helper performs a lookup as part of its own logic, store the result on the object rather than discarding it and repeating the lookup in every caller.
- **Code must be self-documenting at the point of use.** Any construct that forces a reader to look elsewhere to understand what a value means is wrong. Positional indices (`array[0]`, `array[3]`), magic numbers, and unnamed parameters all require cross-referencing; replace them with `Dictionary<string, T>`, named fields, named locals, or named constants so that meaning is visible at every call site.

## Notes

- Target framework is .NET 3.5 (Unity Full) — avoid APIs not available in this version
- `PlayMaker.dll` must remain in the project directory for FSM access
- `ModConsole.Error()` / `ModConsole.Log()` are used for in-game debug output
- AI-generated code must be disclosed per `AssemblyInfo.cs` comment
