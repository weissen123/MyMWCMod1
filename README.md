# MyMWCMod1

A gameplay mod for **My Winter Car** built on the [MSCLoader](https://github.com/piotrulos/MSCLoader) framework.

Targets two vehicles: **CORRIS** (the car — wear reduction on configured engine and body components) and **MACHTWAGEN** (the taxi drivetrain — Automated Manual Transmission).

---

## Features

- **Automated Manual Transmission (AMT)** — the taxi shifts gears automatically, with configurable shift-up and shift-down RPM thresholds
- **Heavily reduced wear rates** for oil level, oil filter dirt, headlight bulbs, spark plugs, alternator, brake fluid, heaterbox, waterpump, and head gasket — no need to swap half of the Corris after each drive any more
- **Configurable `canStall` flag** for the CORRIS engine, applied only when electricity, fuel, and combustion FSM conditions are all true — fix for automatic transmission "not crawl" issue
- **Player camera pivot reset** — press a configurable key to snap the in-car camera back to a stored seated pose; works per-vehicle (Taxi and Corris configured by default), only fires while you are inside the matching vehicle
- **Torque Converter Simulator** — per-gear torque-converter physics layered on top of the drivetrain; integrates output-shaft speed and optionally writes back `differentialSpeed`, `netTorque`, `frictionTorque`, and `finalDriveRatio`; live overlay shows TC state; toggle between write-back (`on`) and display-only (`display`) mode with a configurable key
- **Drivetrain statistics recorder** — press a key to show a live overlay of configured drivetrain fields; `Ctrl+key` saves a timestamped CSV and resets the buffer; hiding the overlay discards unsaved data
- **XML-driven configuration** — all monitors, drivetrain settings, and pivot reset poses live in a single editable file; delete it to regenerate defaults
- **CSV dumpers** — export PlayMaker FSM variables or Drivetrain component fields to CSV from the mod settings menu, useful for discovering new paths and variable names

---

## Installation

1. Install [MSCLoader](https://github.com/piotrulos/MSCLoader) for My Winter Car.
2. Drop `MyMWCMod1.dll` into your `Mods/` folder.
3. Launch the game. The mod creates `Mods/MyMWCMod1_monitors.xml` with default settings on first load.

---

## In-Game Settings

Settings are registered via MSCLoader and appear in the mod settings menu.

| Setting | Type | Default | Range | Description |
|---|---|---|---|---|
| Automated Manual Transmission (AMT) | Checkbox | On | — | Auto-shifts the taxi |
| Shift Up RPM | Slider | 3500 | 1000 – 8000 | RPM at which AMT shifts up |
| Shift Down RPM | Slider | 1700 | 500 – 7000 | RPM at which AMT shifts down |
| Corris Engine can stall | Checkbox | Off | — | Whether the CORRIS engine can stall; only applied when electricity, fuel, and combustion FSM conditions are all true simultaneously |
| Reset Player Pivot | Keybind | `\` | — | Snaps the in-car PLAYER transform to the stored pose for the current vehicle |
| Save Player Pivot | Keybind | `Ctrl+\` | — | Saves the current PLAYER transform pose to XML for the current vehicle |

---

## XML Configuration

`Mods/MyMWCMod1_monitors.xml` controls every monitor. Delete the file to regenerate defaults.

### Component monitors (FSM floats)

```xml
<Monitor label="OilLevel"
         path="CORRIS/..."
         fsmName="Data"
         fsmFloat="OilLevel"
         direction="Decreases"
         factor="0.0001"/>
```

| Attribute | Description |
|---|---|
| `label` | Identifier shown in log messages |
| `path` | Unity `GameObject.Find()` path to the component |
| `fsmName` | PlayMaker FSM name on (or inside) that object |
| `fsmFloat` | Float variable name within that FSM |
| `direction` | `Increases` or `Decreases` — which direction counts as wear |
| `factor` | Fraction of each wear tick that is actually applied. `0.01` = 1% of normal rate |

### Default monitors

| Label | Variable | Factor |
|---|---|---|
| OilLevel | OilLevel | 0.0001 |
| BrakeFluidF | BrakeFluidF | 0.0001 |
| OilFiltDirt | Dirt | 0.01 |
| WearBulbL / WearBulbR | WearBulb | 0.01 |
| SparkPlug1 – SparkPlug4 | Wear | 0.01 |
| Alternator | Wear | 0.01 |
| Heaterbox | Wear | 0.01 |
| Waterpump | Wear | 0.01 |
| Headgasket | Wear | 0.01 |

### How wear reduction works

Every physics tick (`FixedUpdate`), if a monitored value has moved in the wear direction the change is rolled back by `(1 − factor)`:

```
new value = previous + (raw change) × factor
```

With `factor = 0.01` only 1 % of each tick's wear is kept. The component still wears — just 100× slower than normal.

### Drivetrain monitors

```xml
<Monitor label="MACHTWAGEN" path="JOBS/TAXIJOB/MACHTWAGEN">
    <Drivetrain>
        <Setting id="autoTransmission" type="checkbox" label="Automated Manual Transmission (AMT)" default="true" />
        <Setting id="shiftUpRPM"       type="slider"   label="Shift Up RPM"   min="1000" max="8000" default="3500" />
        <Setting id="shiftDownRPM"     type="slider"   label="Shift Down RPM" min="500"  max="7000" default="1700" />
    </Drivetrain>
</Monitor>
```

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

A `<Setting>` may include one or more `<Condition>` children. All conditions are AND-ed; an empty list means always apply. Two condition forms are supported:

| Form | Required attributes | Evaluates as |
|---|---|---|
| FsmBool | `path`, `fsmName`, `fsmBool` | value of the named PlayMaker bool variable |
| ComponentFloat | `path`, `CompName`, `varFloat`, `minFloat` | `field >= minFloat` via reflection |

Unresolved conditions (object not found at load time) are retried every physics tick and evaluate as `false` until resolved.

### Statistics collector

A `<Statistics>` child inside `<Drivetrain>` records drivetrain fields over time.

```xml
<Drivetrain>
  <Setting .../>
  <Statistics fileName="MWC_Drivetrain_Stat_CORRIS" KeyCode="KeypadDivide">
    <Statistic field="torque" live="1" />
    <Statistic field="rpm"   live="1" />
  </Statistics>
</Drivetrain>
```

| Attribute / element | Description |
|---|---|
| `fileName` | Base name for the output CSV; timestamp (`_yyyyMMdd_HHmmss`) appended automatically |
| `KeyCode` | Unity KeyCode — press to show overlay and start collecting; press again to hide overlay and discard data. `Ctrl+KeyCode` saves a timestamped CSV, resets the buffer, and keeps the overlay on |
| `<Statistic field="...">` | Field name on `Drivetrain` (public or private). Unknown fields are logged and skipped |
| `live="X"` (optional) | Show field value in the live overlay; omit to record in CSV only |

CSV format: `Time(s);field1;field2;...` — sampled at 10 Hz, InvariantCulture decimal separators.

### Torque Converter Simulator

A `<TorqueConverter>` child inside `<Drivetrain>` enables per-gear TC physics.

```xml
<TorqueConverter mode="on" KeyCode="KeypadEnter" RPMStall="2000" rStall="2">
  <vehicleMass path="CORRIS/Simulation/CarData" fsmName="GetWeight" fsmFloat="Mass" />
  <wheelRadius path="CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RL/tire/VINP_WheelRL"
               fsmName="Data" fsmFloat="TireRadius" />
  <Gearbox>
    <GearRatio gear="2" ratio="10.6116" />
    <GearRatio gear="3" ratio="6.438" />
    <GearRatio gear="4" ratio="4.44" />
  </Gearbox>
</TorqueConverter>
```

| Attribute / element | Description |
|---|---|
| `mode` | `on` — integrate and write back drivetrain fields + show overlay; `display` — overlay only, no write-back; `off` — skip entirely |
| `KeyCode` (optional) | Toggle between `on` and `display` at runtime. No effect when initial `mode` is `off` |
| `RPMStall` | Engine speed at stall in RPM |
| `rStall` | Torque ratio T_out/T_drag at speed ratio ν = 0 |
| `<vehicleMass>` / `<wheelRadius>` | Lazily-resolved FSM floats (staged GO → FSM → variable lookup); integration and write-back are suspended until both are found |
| `<Gearbox>` | Required wrapper; contains one `<GearRatio>` per active gear |
| `<GearRatio gear="N" ratio="V">` | Active gears only. Rename to `<xGearRatio>` to deactivate a gear without deleting it |

In `on` mode, every physics tick the simulator reads `engineAngularVelo`, integrates `ω_out`, and writes `differentialSpeed`, `finalDriveRatio`, `netTorque`, and `frictionTorque` back to the drivetrain. The live overlay shows gear, throttle, torque values, speed ratio, and final drive ratio.

### Pivot reset

A `<PivotReset>` child on any `<Monitor>` defines the stored seated pose for a vehicle. When the keybind is pressed and the `PlayerCurrentVehicle` global FSM string matches `vehicleName`, the PLAYER transform at `playerPath` is snapped to the specified local position and rotation.

```xml
<Monitor label="MACHTWAGEN" path="JOBS/TAXIJOB/MACHTWAGEN">
    <Drivetrain>...</Drivetrain>
    <PivotReset vehicleName="Taxi"
                playerPath="JOBS/TAXIJOB/MACHTWAGEN/Functions/PlayerTrigger/DriverHeadPivot/CameraPivotPLR/Pivot/PLAYER"
                posX="0.005122664" posY="-0.6894007" posZ="0.1324202"
                rotX="0"           rotY="359.5581"   rotZ="0" />
</Monitor>
```

| Attribute | Description |
|---|---|
| `vehicleName` | Must match the `PlayerCurrentVehicle` PlayMaker global string exactly |
| `playerPath` | `GameObject.Find()` path to the PLAYER transform |
| `posX/Y/Z` | Target `localPosition` |
| `rotX/Y/Z` | Target `localEulerAngles` |

To add pivot reset support for another vehicle, add a `<PivotReset>` element to that vehicle's `<Monitor>` — no code changes needed. Press `Save Player Pivot` (`Ctrl+\`) while seated in the vehicle to write the current pose back to the XML file.

---

## CSV Dumpers

Buttons in the mod settings menu trigger CSV exports to the game folder.

**FSM dump** (`MWC_FSM_Dump_<vehicle>.csv`) — recursively walks a game object hierarchy and writes all PlayMaker FSM variables.

Columns: `GameObject Path ; FSM Name ; Type ; Variable Name ; Value`

`Type` is `Float`, `Int`, or `Bool`. Rows with no variables show `N/A`.

**Drivetrain dump** (`MWC_Drivetrain_<vehicle>.csv`) — finds all `Drivetrain` components under a root game object and writes their public instance fields via reflection.

Use these to discover object paths and variable names for adding your own `<Monitor>` or `<Statistic>` entries.

---

## Building from source

Requirements: Visual Studio (or MSBuild) with .NET Framework 3.5, plus the game's `Assembly-CSharp.dll`, `UnityEngine.dll`, and `MSCLoader.dll` referenced in the project. `PlayMaker.dll` is included.

A good starting point for setting up an MSCLoader mod project: https://www.overtake.gg/threads/guide-how-to-make-mods-for-mysummercar.147475/

```
msbuild MyMWCMod1.sln /p:Configuration=Release
```

The post-build event copies the DLL to the configured game Mods folder automatically.

---

## License

Free to use, modify, and redistribute for any purpose. Provided as-is with no warranty of any kind.
