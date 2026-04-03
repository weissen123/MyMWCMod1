# MyMWCMod1

A gameplay mod for **My Winter Car** built on the [MSCLoader](https://github.com/piotrulos/MSCLoader) framework.

Targets two vehicles: **CORRIS** (the car — wear reduction on selected engine and body components) and **MACHTWAGEN** (the taxi job drivetrain — Automated Manual Transmission).

---

## Features

- **Automated Manual Transmission (AMT)** — the taxi shifts gears automatically, with configurable shift-up and shift-down RPM thresholds
- **Heavily reduced wear rates** for oil level, oil filter dirt, headlight bulbs, spark plugs, alternator, brake fluid, heaterbox, waterpump, and head gasket
- **Configurable `canStall` flag** for the CORRIS engine, active only while the ignition is on
- **XML-driven configuration** — all monitors and drivetrain settings live in a single editable file
- **FSM CSV dumper** — export all PlayMaker float variables from CORRIS or BACHGLOTZ to a CSV, useful for discovering new paths and variable names

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
| Corris Engine can stall | Checkbox | Off | — | Whether the CORRIS engine can stall; only applies while the ignition is on |

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
            <Condition path="CORRIS/Simulation/Electricity" fsmName="Power" fsmBool="ElectricsOK" />
        </Setting>
    </Drivetrain>
</Monitor>
```

A `<Setting>` of type `checkbox` may include an optional `<Condition>` child that gates its effect on a PlayMaker bool variable. If any required attribute (`path`, `fsmName`, `fsmBool`) is missing the condition is ignored and the setting is applied unconditionally.

---

## FSM CSV Dumper

Open the mod settings menu and click one of the dump buttons. A semicolon-delimited CSV is written to the game folder:

```
MWC_FSM_Dump_CORRIS.csv
MWC_FSM_Dump_BACHGLOTZ(1905kg).csv
```

Columns: `GameObject Path ; FSM Name ; Float Variable Name ; Float Value`

Use this to discover new object paths and FSM variable names for adding your own `<Monitor>` entries.

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
