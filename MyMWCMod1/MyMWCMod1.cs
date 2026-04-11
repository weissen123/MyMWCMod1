using HutongGames.PlayMaker;
using MSCLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;

namespace MyMWCMod1
{
    public class MyMWCMod1 : Mod
    {
        public override string ID => "ZZ_MyMWCMod1"; // Your (unique) mod ID
        public override string Name => "ZZ_MyMWCMod1"; // Your mod name
        public override string Author => "JWE"; // Name of the Author (your name)
        public override string Version => "1.0"; // Version
        public override string Description => ""; // Short description of your mod
        public override Game SupportedGames => Game.MyWinterCar;

        // Path to the XML config file inside the Mods folder
        private string XmlPath
        {
            get
            {
                string modsFolder = Path.Combine(Path.Combine(Application.dataPath, ".."), "Mods");
                return Path.Combine(modsFolder, "MyMWCMod1_monitors.xml");
            }
        }

        private enum WearDirection { Increases, Decreases }

        private class ComponentMonitor
        {
            public string        Label;
            public FsmFloat      Value;
            public float         Previous;
            public WearDirection Direction;
            public float         Factor;

            public void ApplyReduction()
            {
                if (Value == null) return;

                bool conditionMet = Direction == WearDirection.Increases
                    ? Value.Value > Previous
                    : Value.Value < Previous;

                if (conditionMet)
                    Value.Value = Previous + (Value.Value - Previous) * Factor;

                Previous = Value.Value;
            }
        }

        private class PivotResetConfig
        {
            public string  VehicleName;
            public string  GameObjectPath;
            public Vector3 LocalPosition;
            public Vector3 LocalEulerAngles;

            private GameObject ActiveGO; // set by Resolve(); guaranteed non-null when config is non-null

            private static List<PivotResetConfig> _configs;
            private static FsmString              _vehicleString;
            private static string                 _xmlPath;

            public static void Init(List<PivotResetConfig> configs, string xmlPath)
            {
                _configs       = configs;
                _xmlPath       = xmlPath;
                _vehicleString = PlayMakerGlobals.Instance.Variables.FindFsmString("PlayerCurrentVehicle");
                if (_vehicleString == null)
                    ModConsole.Error("MyMWCMod1: Could not find global FsmString 'PlayerCurrentVehicle'.");
            }

            public static void ResetCurrentPivot()
            {
                PivotResetConfig config = Resolve();
                if (config == null) return;
                config.ActiveGO.transform.localPosition    = config.LocalPosition;
                config.ActiveGO.transform.localEulerAngles = config.LocalEulerAngles;
                ModConsole.Log("MyMWCMod1: PLAYER pivot reset for " + config.VehicleName + ".");
            }

            public static void SaveCurrentPivot()
            {
                PivotResetConfig config = Resolve();
                if (config == null) return;
                config.LocalPosition    = config.ActiveGO.transform.localPosition;
                config.LocalEulerAngles = config.ActiveGO.transform.localEulerAngles;
                config.WriteToXml();
            }

            private static PivotResetConfig Resolve()
            {
                if (_vehicleString == null)
                    _vehicleString = PlayMakerGlobals.Instance.Variables.FindFsmString("PlayerCurrentVehicle");
                if (_vehicleString == null) return null;
                string name = _vehicleString.Value;
                foreach (PivotResetConfig c in _configs)
                {
                    if (c.VehicleName != name) continue;
                    GameObject go = GameObject.Find(c.GameObjectPath);
                    if (go != null) { c.ActiveGO = go; return c; }
                }
                return null;
            }

            public static PivotResetConfig LoadFromXml(XmlElement pivotEl)
            {
                var ns = System.Globalization.NumberStyles.Float;
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                float px, py, pz, rx, ry, rz;
                float.TryParse(pivotEl.GetAttribute("posX"), ns, ic, out px);
                float.TryParse(pivotEl.GetAttribute("posY"), ns, ic, out py);
                float.TryParse(pivotEl.GetAttribute("posZ"), ns, ic, out pz);
                float.TryParse(pivotEl.GetAttribute("rotX"), ns, ic, out rx);
                float.TryParse(pivotEl.GetAttribute("rotY"), ns, ic, out ry);
                float.TryParse(pivotEl.GetAttribute("rotZ"), ns, ic, out rz);
                return new PivotResetConfig
                {
                    VehicleName      = pivotEl.GetAttribute("vehicleName"),
                    GameObjectPath   = pivotEl.GetAttribute("playerPath"),
                    LocalPosition    = new Vector3(px, py, pz),
                    LocalEulerAngles = new Vector3(rx, ry, rz),
                };
            }

            private void WriteToXml()
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(_xmlPath);

                foreach (XmlNode monNode in doc.DocumentElement.ChildNodes)
                {
                    if (monNode.NodeType != XmlNodeType.Element) continue;
                    XmlElement pivotEl = (XmlElement)((XmlElement)monNode).SelectSingleNode("PivotReset");
                    if (pivotEl == null) continue;
                    if (pivotEl.GetAttribute("vehicleName") != VehicleName)   continue;
                    if (pivotEl.GetAttribute("playerPath")  != GameObjectPath) continue;

                    var ic = System.Globalization.CultureInfo.InvariantCulture;
                    pivotEl.SetAttribute("posX", LocalPosition.x.ToString("G9", ic));
                    pivotEl.SetAttribute("posY", LocalPosition.y.ToString("G9", ic));
                    pivotEl.SetAttribute("posZ", LocalPosition.z.ToString("G9", ic));
                    pivotEl.SetAttribute("rotX", LocalEulerAngles.x.ToString("G9", ic));
                    pivotEl.SetAttribute("rotY", LocalEulerAngles.y.ToString("G9", ic));
                    pivotEl.SetAttribute("rotZ", LocalEulerAngles.z.ToString("G9", ic));

                    XmlWriterSettings ws = new XmlWriterSettings { Indent = true, IndentChars = "  " };
                    using (XmlWriter w = XmlWriter.Create(_xmlPath, ws))
                        doc.Save(w);

                    ModConsole.Log("MyMWCMod1: Saved pivot for " + VehicleName + " to XML.");
                    return;
                }

                ModConsole.Error("MyMWCMod1: <PivotReset vehicleName=\"" + VehicleName + "\"> not found in XML.");
            }
        }

        private class DrivetrainBoolSetting
        {
            public SettingsCheckBox         Checkbox;
            public Action<Drivetrain, bool> Setter;
            public readonly List<ConditionRef> Conditions = new List<ConditionRef>(); // empty = always apply
        }

        private class ConditionRef
        {
            private readonly string _path;
            private readonly string _fsmName;
            private readonly string _varName;
            private readonly string _logLabel;

            private const float RetryInterval = 1f; // seconds between resolution attempts

            private GameObject   _cachedGO;       // set once GameObject.Find succeeds
            private PlayMakerFSM _cachedFsm;      // set once FSM-by-name scan succeeds
            private FsmBool      _resolved;       // set once FsmVariables lookup succeeds
            private float        _nextRetryTime;  // default 0f → first call always attempts

            public ConditionRef(string path, string fsmName, string varName, string logLabel)
            {
                _path = path; _fsmName = fsmName; _varName = varName; _logLabel = logLabel;
            }

            public bool IsResolved => _resolved != null;

            public bool Evaluate()
            {
                // Hot path: bool already cached
                if (_resolved != null) return _resolved.Value;

                // Rate-limit retries to once per second
                if (UnityEngine.Time.fixedTime < _nextRetryTime) return false;
                _nextRetryTime = UnityEngine.Time.fixedTime + RetryInterval;

                // Stage 1: find the GameObject
                if (_cachedGO == null)
                {
                    _cachedGO = GameObject.Find(_path);
                    if (_cachedGO == null) return false;
                }

                // Stage 2: find the PlayMakerFSM by name (GetComponentsInChildren runs once, then cached)
                if (_cachedFsm == null)
                {
                    foreach (PlayMakerFSM fsm in _cachedGO.GetComponentsInChildren<PlayMakerFSM>())
                    {
                        if (fsm.FsmName == _fsmName) { _cachedFsm = fsm; break; }
                    }
                    if (_cachedFsm == null) return false;
                }

                // Stage 3: find the bool variable
                FsmBool result = _cachedFsm.FsmVariables.FindFsmBool(_varName);
                if (result != null)
                {
                    _resolved = result;
                    ModConsole.Log($"{_logLabel} '{_varName}' resolved = {result.Value}");
                    return result.Value;
                }

                return false;
            }
        }

        private class DrivetrainMonitor
        {
            public string    Label;
            public Drivetrain Drivetrain;
            public readonly List<DrivetrainBoolSetting> BoolSettings = new List<DrivetrainBoolSetting>();

            public void Apply()
            {
                foreach (DrivetrainBoolSetting s in BoolSettings)
                {
                    bool conditionMet = true;
                    foreach (ConditionRef c in s.Conditions)
                        if (!c.Evaluate()) { conditionMet = false; break; }
                    if (conditionMet)
                        s.Setter(Drivetrain, s.Checkbox.GetValue());
                }
            }
        }

        // Maps XML setting id → Drivetrain bool property setter
        private static readonly Dictionary<string, Action<Drivetrain, bool>> _drivetrainBoolSetters
            = new Dictionary<string, Action<Drivetrain, bool>>
            {
                { "autoTransmission", (d, v) => d.automatic = v },
                { "canStall",         (d, v) => d.canStall  = v },
            };

        // Maps XML setting id → Drivetrain float property setter
        private static readonly Dictionary<string, Action<Drivetrain, float>> _drivetrainFloatSetters
            = new Dictionary<string, Action<Drivetrain, float>>
            {
                { "shiftUpRPM",   (d, v) => d.shiftUpRPM   = v },
                { "shiftDownRPM", (d, v) => d.shiftDownRPM = v },
            };

        private List<ComponentMonitor>  _monitors           = new List<ComponentMonitor>();
        private List<DrivetrainMonitor> _drivetrainMonitors = new List<DrivetrainMonitor>();
        private List<PivotResetConfig>  _pivotResetConfigs  = new List<PivotResetConfig>();
        private SettingsKeybind _pivotResetKey;
        private SettingsKeybind _pivotSaveKey;

        private Dictionary<string, SettingsCheckBox> _checkboxSettings = new Dictionary<string, SettingsCheckBox>();
        private Dictionary<string, SettingsSlider>   _sliderSettings   = new Dictionary<string, SettingsSlider>();

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.Update, Mod_Update);
            SetupFunction(Setup.FixedUpdate, Mod_FixedUpdate);
            SetupFunction(Setup.ModSettings, Mod_Settings);
        }

        private void Mod_Settings()
        {
            EnsureXmlExists();
            LoadDrivetrainSettings();
            _pivotResetKey = Keybind.Add("pivotReset", "Reset Player Pivot", KeyCode.Backslash);
            _pivotSaveKey  = Keybind.Add("savePivot",  "Save Player Pivot",  KeyCode.Backslash, KeyCode.LeftControl);
            Settings.AddButton("Dump CORRIS FSM to CSV",    () => DumpToCSV("CORRIS"));
            Settings.AddButton("Dump BACHGLOTZ FSM to CSV", () => DumpToCSV("BACHGLOTZ(1905kg)"));
        }

        private void EnsureXmlExists()
        {
            string p = XmlPath;
            if (!File.Exists(p))
            {
                WriteDefaultXml(p);
                ModConsole.Log("MyMWCMod1: Created default monitors config at " + p);
            }
        }

        private void LoadDrivetrainSettings()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(XmlPath);

            foreach (XmlNode monNode in doc.DocumentElement.ChildNodes)
            {
                if (monNode.NodeType != XmlNodeType.Element) continue;
                XmlElement monEl = (XmlElement)monNode;

                XmlElement drivetrainEl = (XmlElement)monEl.SelectSingleNode("Drivetrain");
                if (drivetrainEl == null) continue;

                foreach (XmlNode settingNode in drivetrainEl.ChildNodes)
                {
                    if (settingNode.NodeType != XmlNodeType.Element) continue;
                    XmlElement s = (XmlElement)settingNode;

                    string id       = s.GetAttribute("id");
                    string type     = s.GetAttribute("type");
                    string label    = s.GetAttribute("label");
                    string defVal   = s.GetAttribute("default");

                    if (type == "checkbox")
                    {
                        bool defBool;
                        bool.TryParse(defVal, out defBool);
                        _checkboxSettings[id] = Settings.AddCheckBox(id, label, defBool);
                    }
                    else if (type == "slider")
                    {
                        float min, max, def;
                        float.TryParse(s.GetAttribute("min"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out min);
                        float.TryParse(s.GetAttribute("max"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out max);
                        float.TryParse(defVal,                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out def);
                        _sliderSettings[id] = Settings.AddSlider(id, label, min, max, def);
                    }
                }
            }
        }

        private void Mod_OnLoad()
        {
            SetupMonitors();
            PivotResetConfig.Init(_pivotResetConfigs, XmlPath);
        }

        private void Mod_Update()
        {
            if (_pivotSaveKey.GetKeybindDown())  { PivotResetConfig.SaveCurrentPivot();  return; }
            if (_pivotResetKey.GetKeybindDown()) { PivotResetConfig.ResetCurrentPivot(); return; }
        }

        private void Mod_FixedUpdate()
        {
            foreach (ComponentMonitor m in _monitors)
                m.ApplyReduction();
            foreach (DrivetrainMonitor m in _drivetrainMonitors)
                m.Apply();
        }

        private ComponentMonitor SetupComponentMonitor(XmlElement el, string label, string goPath)
        {
            string fsmName   = el.GetAttribute("fsmName");
            string fsmFloat  = el.GetAttribute("fsmFloat");
            string dirStr    = el.GetAttribute("direction");
            string factorStr = el.GetAttribute("factor");

            WearDirection direction = dirStr == "Increases"
                ? WearDirection.Increases
                : WearDirection.Decreases;

            float factor;
            if (!float.TryParse(factorStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out factor))
                factor = 0.01f;

            FsmFloat fsmFloatVar = FindFsmFloat(goPath, fsmName, fsmFloat, label);
            if (fsmFloatVar == null) return null;

            return new ComponentMonitor
            {
                Label     = label,
                Value     = fsmFloatVar,
                Previous  = fsmFloatVar.Value,
                Direction = direction,
                Factor    = factor
            };
        }

        private void SetupDrivetrain(string path, string label, XmlElement drivetrainEl)
        {
            GameObject go = GameObject.Find(path);
            if (go == null)
            {
                ModConsole.Error("FAILED TO FIND " + label + "!!!");
                return;
            }

            Drivetrain drivetrain = go.GetComponent<Drivetrain>();
            if (drivetrain == null) return;

            ModConsole.Log("MyMWCMod1: Drivetrain setup for " + label);

            DrivetrainMonitor monitor = new DrivetrainMonitor { Label = label, Drivetrain = drivetrain };

            foreach (XmlNode settingNode in drivetrainEl.ChildNodes)
            {
                if (settingNode.NodeType != XmlNodeType.Element) continue;
                XmlElement s    = (XmlElement)settingNode;
                string     id   = s.GetAttribute("id");
                string     type = s.GetAttribute("type");

                if (type == "slider")
                {
                    SettingsSlider            slider;
                    Action<Drivetrain, float> setter;
                    if (_sliderSettings.TryGetValue(id, out slider) &&
                        _drivetrainFloatSetters.TryGetValue(id, out setter))
                        setter(drivetrain, (float)slider.GetValue());
                }
                else if (type == "checkbox")
                {
                    SettingsCheckBox         cb;
                    Action<Drivetrain, bool> setter;
                    if (_checkboxSettings.TryGetValue(id, out cb) &&
                        _drivetrainBoolSetters.TryGetValue(id, out setter))
                    {
                        XmlNodeList condNodes = s.SelectNodes("Condition");
                        if (condNodes.Count == 0)
                        {
                            // No conditions — apply once at load, same as sliders
                            setter(drivetrain, cb.GetValue());
                        }
                        else
                        {
                            DrivetrainBoolSetting boolSetting = new DrivetrainBoolSetting { Checkbox = cb, Setter = setter };

                            foreach (XmlNode condNode in condNodes)
                            {
                                XmlElement condEl = (XmlElement)condNode;
                                string condPath = condEl.GetAttribute("path");
                                string condFsm  = condEl.GetAttribute("fsmName");
                                string condBool = condEl.GetAttribute("fsmBool");
                                if (string.IsNullOrEmpty(condPath) || string.IsNullOrEmpty(condFsm) || string.IsNullOrEmpty(condBool))
                                {
                                    ModConsole.Error($"MyMWCMod1: Condition for '{id}' is missing required attributes (path/fsmName/fsmBool) — condition skipped.");
                                    continue;
                                }
                                string condLabel = id + ".Condition";
                                ConditionRef cond = new ConditionRef(condPath, condFsm, condBool, condLabel);
                                cond.Evaluate(); // attempt early resolution — logs success if object already exists
                                if (!cond.IsResolved)
                                    ModConsole.Log($"MyMWCMod1: Condition '{condBool}' found at load — will retry at runtime.");
                                boolSetting.Conditions.Add(cond);
                            }

                            monitor.BoolSettings.Add(boolSetting);
                        }
                    }
                }
            }

            if (monitor.BoolSettings.Count > 0)
                _drivetrainMonitors.Add(monitor);
        }

        private void SetupMonitors()
        {
            string xmlPath = XmlPath;
            EnsureXmlExists(); // no-op if already created in Mod_Settings
            _monitors = LoadMonitorsFromXml(xmlPath);
            ModConsole.Log("MyMWCMod1: Loaded " + _monitors.Count + " monitors from " + xmlPath);
        }

        private List<ComponentMonitor> LoadMonitorsFromXml(string path)
        {
            var result = new List<ComponentMonitor>();
            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                XmlElement el = (XmlElement)node;

                string label  = el.GetAttribute("label");
                string goPath = el.GetAttribute("path");

                bool isContainer = false;

                XmlElement pivotEl = (XmlElement)el.SelectSingleNode("PivotReset");
                if (pivotEl      != null) { _pivotResetConfigs.Add(PivotResetConfig.LoadFromXml(pivotEl)); isContainer = true; }

                XmlElement drivetrainEl = (XmlElement)el.SelectSingleNode("Drivetrain");
                if (drivetrainEl != null) { SetupDrivetrain(goPath, label, drivetrainEl); isContainer = true; }

                if (isContainer) continue;

                ComponentMonitor monitor = SetupComponentMonitor(el, label, goPath);
                if (monitor != null) result.Add(monitor);
            }

            return result;
        }

        private void WriteDefaultXml(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            const string xml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- MyMWCMod1 monitor configuration. direction: Increases | Decreases. factor: 0.0-1.0 (0.01 = 1% of normal wear rate). -->
<Monitors>
  <Monitor label=""OilLevel""    fsmName=""Data"" fsmFloat=""OilLevel""    direction=""Decreases"" factor=""0.0001"" path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilpan/Oilpan(VINXX)"" />
  <Monitor label=""BrakeFluidF"" fsmName=""Data"" fsmFloat=""BrakeFluidF"" direction=""Decreases"" factor=""0.0001"" path=""CORRIS/Assemblies/VINP_BrakeMasterCylinder/Brake Master Cylinder(VINXX)"" />
  <Monitor label=""OilFiltDirt"" fsmName=""Data"" fsmFloat=""Dirt""        direction=""Increases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilfilter"" />
  <Monitor label=""WearBulbL""   fsmName=""Data"" fsmFloat=""WearBulb""    direction=""Decreases"" factor=""0.01""   path=""CORRIS/Assemblies/VINP_HeadlightLeft/Head Light Assembly(VINXX)"" />
  <Monitor label=""WearBulbR""   fsmName=""Data"" fsmFloat=""WearBulb""    direction=""Decreases"" factor=""0.01""   path=""CORRIS/Assemblies/VINP_HeadlightRight/Head Light Assembly(VINXX)"" />
  <Monitor label=""SparkPlug1""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug1"" />
  <Monitor label=""SparkPlug2""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug2"" />
  <Monitor label=""SparkPlug3""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug3"" />
  <Monitor label=""SparkPlug4""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug4"" />
  <Monitor label=""Alternator""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Alternator"" />
  <Monitor label=""Heaterbox""   fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/Assemblies/VINP_Heaterbox/Heater Box(VINXX)"" />
  <Monitor label=""Waterpump""   fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Waterpump"" />
  <Monitor label=""Headgasket""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Headgasket"" />
  <Monitor label=""TimingBelt""  fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_TimingBelt"" />
  <Monitor label=""FanBelt""     fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_FanBelt"" />
  <Monitor label=""Oilpump""     fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilpump"" />
  <Monitor label=""RearAxle""    fsmName=""Data"" fsmFloat=""Wear""        direction=""Decreases"" factor=""0.01""   path=""CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RL/wheel_spindle_rl/VINP_RearAxle/Rear Axle (EB) (VINXX)"" />

  <Monitor label=""MACHTWAGEN"" path=""JOBS/TAXIJOB/MACHTWAGEN"">
    <Drivetrain>
      <Setting id=""autoTransmission"" type=""checkbox"" label=""Automated Manual Transmission (AMT)"" default=""true"" />
      <Setting id=""shiftUpRPM""       type=""slider""   label=""Shift Up RPM""                        min=""1000"" max=""8000"" default=""3500"" />
      <Setting id=""shiftDownRPM""     type=""slider""   label=""Shift Down RPM""                      min=""500""  max=""7000"" default=""1700"" />
    </Drivetrain>
    <PivotReset vehicleName=""Taxi""
      playerPath=""JOBS/TAXIJOB/MACHTWAGEN/Functions/PlayerTrigger/DriverHeadPivot/CameraPivotPLR/Pivot/PLAYER""
      posX=""0.005122664"" posY=""-0.6894007"" posZ=""0.1324202""
      rotX=""0""           rotY=""359.5581""   rotZ=""0""/>
  </Monitor>

  <Monitor label=""CORRIS"" path=""CORRIS"">
    <Drivetrain>
      <Setting id=""canStall"" type=""checkbox"" label=""Corris Engine can stall"" default=""false"">
        <Condition fsmName=""Power""     fsmBool=""ElectricsOK""  path=""CORRIS/Simulation/Electricity"" />
        <Condition fsmName=""FuelLine""  fsmBool=""FuelOK""       path=""CORRIS/Simulation/Engine/Fuel"" />
        <Condition fsmName=""Cylinders"" fsmBool=""CombustionOK"" path=""CORRIS/Simulation/Engine/Combustion"" />
      </Setting>
    </Drivetrain>
    <PivotReset vehicleName=""Corris""
      playerPath=""CORRIS/Functions/DriverHeadPivot/CameraPivotPLR/SeatPivot/PLAYER""
      posX=""-0.01190625"" posY=""-0.6566017"" posZ=""0.2135472""
      rotX=""0""           rotY=""0.6978999""  rotZ=""0""/>
  </Monitor>

  <Monitor label=""GIFU(650/350psi)"" path=""GIFU(650/350psi)"">
    <PivotReset vehicleName=""Gifu""
      playerPath=""GIFU(650/350psi)/LOD/DriverHeadPivot/CameraPivotPLR/Pivot/PLAYER""
      posX=""-0.0178838093"" posY=""-0.416051507"" posZ=""0.0354102328""
      rotX=""0""             rotY=""0.258155435""  rotZ=""0""/>
  </Monitor>

  <Monitor label=""GIFU(750/450psi)"" path=""GIFU(750/450psi)"">
    <PivotReset vehicleName=""Gifu""
      playerPath=""GIFU(750/450psi)/LOD/DriverHeadPivot/CameraPivotPLR/Pivot/PLAYER""
      posX=""-0.0178838093"" posY=""-0.416051507"" posZ=""0.0354102328""
      rotX=""0""             rotY=""0.258155435""  rotZ=""0""/>
  </Monitor>
</Monitors>";

            File.WriteAllText(path, xml, new System.Text.UTF8Encoding(false));
        }

        private void DumpToCSV(string rootName)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null)
            {
                ModConsole.Error($"[MWC Dumper] Could not find {rootName}");
                return;
            }

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("GameObject Path;FSM Name;Type;Variable Name;Value");
            RecursiveCSV(root.transform, csv);

            string fileName = "MWC_FSM_Dump_" + rootName + ".csv";
            File.WriteAllText(fileName, csv.ToString());
            ModConsole.Log("Dump complete: " + fileName + " saved to game folder.");
        }

        private void RecursiveCSV(Transform current, StringBuilder csv)
        {
            string path = GetGameObjectPath(current.gameObject);
            PlayMakerFSM[] fsms = current.GetComponents<PlayMakerFSM>();

            if (fsms.Length > 0)
                foreach (PlayMakerFSM fsm in fsms)
                    AppendFsmRows(csv, path, fsm);
            else
                csv.AppendLine("\"" + path + "\";None;None;None;0");

            foreach (Transform child in current)
                RecursiveCSV(child, csv);
        }

        private void AppendFsmRows(StringBuilder csv, string path, PlayMakerFSM fsm)
        {
            string safePath = "\"" + path + "\"";
            string safeFsm  = "\"" + fsm.FsmName + "\"";
            FsmVariables vars = fsm.FsmVariables;

            if (vars.FloatVariables.Length == 0 && vars.IntVariables.Length == 0 && vars.BoolVariables.Length == 0)
            {
                csv.AppendLine(safePath + ";" + safeFsm + ";N/A;N/A;0");
                return;
            }

            foreach (FsmFloat fv in vars.FloatVariables)
                csv.AppendLine(safePath + ";" + safeFsm + ";Float;\"" + fv.Name + "\";" + fv.Value);
            foreach (FsmInt iv in vars.IntVariables)
                csv.AppendLine(safePath + ";" + safeFsm + ";Int;\"" + iv.Name + "\";" + iv.Value);
            foreach (FsmBool bv in vars.BoolVariables)
                csv.AppendLine(safePath + ";" + safeFsm + ";Bool;\"" + bv.Name + "\";" + bv.Value);
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform t = obj.transform;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private FsmFloat FindFsmFloat(string objectName, string fsmName, string floatName, string logLabel)
        {
            GameObject obj = GameObject.Find(objectName);
            if (obj == null)
            {
                ModConsole.Error($"FAILED TO FIND object for {logLabel}!!!");
                return null;
            }

            foreach (PlayMakerFSM fsm in obj.GetComponentsInChildren<PlayMakerFSM>())
            {
                if (fsm.FsmName != fsmName) continue;
                FsmFloat result = fsm.FsmVariables.FindFsmFloat(floatName);
                if (result != null)
                {
                    ModConsole.Log($"{logLabel} '{floatName}' = {result.Value}");
                    return result;
                }
            }

            ModConsole.Error($"FAILED TO FIND FsmFloat '{floatName}' in any FSM '{fsmName}' on {logLabel}!!!");
            return null;
        }
    }
}
