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

            private GameObject   _cachedGO;  // set once GameObject.Find succeeds
            private PlayMakerFSM _cachedFsm; // set once FSM-by-name scan succeeds
            private FsmBool      _resolved;  // set once FsmVariables lookup succeeds

            public ConditionRef(string path, string fsmName, string varName, string logLabel)
            {
                _path = path; _fsmName = fsmName; _varName = varName; _logLabel = logLabel;
            }

            public bool IsResolved => _resolved != null;

            public bool Evaluate()
            {
                // Hot path: bool already cached
                if (_resolved != null) return _resolved.Value;

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

        private Dictionary<string, SettingsCheckBox> _checkboxSettings = new Dictionary<string, SettingsCheckBox>();
        private Dictionary<string, SettingsSlider>   _sliderSettings   = new Dictionary<string, SettingsSlider>();

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.FixedUpdate, Mod_FixedUpdate);
            SetupFunction(Setup.ModSettings, Mod_Settings);
        }

        private void Mod_Settings()
        {
            EnsureXmlExists();
            LoadDrivetrainSettings();
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
        }

        private void Mod_FixedUpdate()
        {
            foreach (ComponentMonitor m in _monitors)
                m.ApplyReduction();
            foreach (DrivetrainMonitor m in _drivetrainMonitors)
                m.Apply();
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
                        DrivetrainBoolSetting boolSetting = new DrivetrainBoolSetting { Checkbox = cb, Setter = setter };

                        foreach (XmlNode condNode in s.SelectNodes("Condition"))
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
                                ModConsole.Log($"MyMWCMod1: Condition '{condBool}' on '{condPath}' not found at load — will retry at runtime.");
                            boolSetting.Conditions.Add(cond);
                        }

                        monitor.BoolSettings.Add(boolSetting);
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

            XmlElement root = doc.DocumentElement; // <Monitors>

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                XmlElement el = (XmlElement)node;

                string label     = el.GetAttribute("label");
                string goPath    = el.GetAttribute("path");

                XmlElement drivetrainEl = (XmlElement)el.SelectSingleNode("Drivetrain");
                if (drivetrainEl != null)
                {
                    SetupDrivetrain(goPath, label, drivetrainEl);
                    continue; // not an FSM monitor
                }

                string fsmName    = el.GetAttribute("fsmName");
                string fsmFloat   = el.GetAttribute("fsmFloat");
                string dirStr     = el.GetAttribute("direction");
                string factorStr  = el.GetAttribute("factor");

                WearDirection direction = dirStr == "Increases"
                    ? WearDirection.Increases
                    : WearDirection.Decreases;

                float factor;
                if (!float.TryParse(factorStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out factor))
                    factor = 0.01f;

                FsmFloat fsmFloatVar = FindFsmFloat(goPath, fsmName, fsmFloat, label);
                if (fsmFloatVar == null) continue;

                result.Add(new ComponentMonitor
                {
                    Label     = label,
                    Value     = fsmFloatVar,
                    Previous  = fsmFloatVar.Value,
                    Direction = direction,
                    Factor    = factor
                });
            }

            return result;
        }

        private void WriteDefaultXml(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, IndentChars = "\t" };
            using (XmlWriter w = XmlWriter.Create(path, settings))
            {
                w.WriteStartDocument();
                w.WriteComment(" MyMWCMod1 monitor configuration. " +
                               "direction: Increases | Decreases. " +
                               "factor: 0.0-1.0 (0.01 = 1% of normal wear rate). ");
                w.WriteStartElement("Monitors");

                WriteMonitor(w, "OilLevel",    "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilpan/Oilpan(VINXX)",                                                               "Data", "OilLevel",    "Decreases", 0.0001f);
                WriteMonitor(w, "BrakeFluidF", "CORRIS/Assemblies/VINP_BrakeMasterCylinder/Brake Master Cylinder(VINXX)",                                                                                   "Data", "BrakeFluidF", "Decreases", 0.0001f);
                WriteMonitor(w, "OilFiltDirt", "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilfilter",                                                                           "Data", "Dirt",        "Increases", 0.01f);
                WriteMonitor(w, "WearBulbL",   "CORRIS/Assemblies/VINP_HeadlightLeft/Head Light Assembly(VINXX)",                                                                                            "Data", "WearBulb",    "Decreases", 0.01f);
                WriteMonitor(w, "WearBulbR",   "CORRIS/Assemblies/VINP_HeadlightRight/Head Light Assembly(VINXX)",                                                                                           "Data", "WearBulb",    "Decreases", 0.01f);
                WriteMonitor(w, "SparkPlug1",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug1",                                  "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "SparkPlug2",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug2",                                  "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "SparkPlug3",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug3",                                  "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "SparkPlug4",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug4",                                  "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "Alternator",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Alternator",                                                                         "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "Heaterbox",   "CORRIS/Assemblies/VINP_Heaterbox/Heater Box(VINXX)",                                                                                                         "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "Waterpump",   "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Waterpump",                                                                           "Data", "Wear",        "Decreases", 0.01f);
                WriteMonitor(w, "Headgasket",  "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Headgasket",                                                                         "Data", "Wear",        "Decreases", 0.01f);

                // MACHTWAGEN drivetrain — settings defined here drive the in-game UI sliders/checkboxes
                w.WriteStartElement("Monitor");
                w.WriteAttributeString("label", "MACHTWAGEN");
                w.WriteAttributeString("path",  "JOBS/TAXIJOB/MACHTWAGEN");
                w.WriteStartElement("Drivetrain");
                WriteSetting(w, "autoTransmission", "checkbox", "Automated Manual Transmission (AMT)", null,   null,   "true");
                WriteSetting(w, "shiftUpRPM",       "slider",   "Shift Up RPM",                        "1000", "8000", "3500");
                WriteSetting(w, "shiftDownRPM",     "slider",   "Shift Down RPM",                      "500",  "7000", "1700");
                w.WriteEndElement(); // </Drivetrain>
                w.WriteEndElement(); // </Monitor>

                w.WriteStartElement("Monitor");
                w.WriteAttributeString("label", "CORRIS");
                w.WriteAttributeString("path",  "CORRIS");
                w.WriteStartElement("Drivetrain");
                w.WriteStartElement("Setting");
                w.WriteAttributeString("id",      "canStall");
                w.WriteAttributeString("type",    "checkbox");
                w.WriteAttributeString("label",   "Corris Engine can stall");
                w.WriteAttributeString("default", "false");
                w.WriteStartElement("Condition");
                w.WriteAttributeString("path",    "CORRIS/Simulation/Electricity");
                w.WriteAttributeString("fsmName", "Power");
                w.WriteAttributeString("fsmBool", "ElectricsOK");
                w.WriteEndElement(); // </Condition>
                w.WriteStartElement("Condition");
                w.WriteAttributeString("path",    "CORRIS/Simulation/Engine/Fuel");
                w.WriteAttributeString("fsmName", "FuelLine");
                w.WriteAttributeString("fsmBool", "FuelOK");
                w.WriteEndElement(); // </Condition>
                w.WriteStartElement("Condition");
                w.WriteAttributeString("path",    "CORRIS/Simulation/Engine/Combustion");
                w.WriteAttributeString("fsmName", "Cylinders");
                w.WriteAttributeString("fsmBool", "CombustionOK");
                w.WriteEndElement(); // </Condition>
                w.WriteEndElement(); // </Setting>
                w.WriteEndElement(); // </Drivetrain>
                w.WriteEndElement(); // </Monitor>

                w.WriteEndElement(); // </Monitors>
                w.WriteEndDocument();
            }
        }

        private static void WriteMonitor(XmlWriter w, string label, string path,
            string fsmName, string fsmFloat, string direction, float factor)
        {
            w.WriteStartElement("Monitor");
            w.WriteAttributeString("label",     label);
            w.WriteAttributeString("path",      path);
            w.WriteAttributeString("fsmName",   fsmName);
            w.WriteAttributeString("fsmFloat",  fsmFloat);
            w.WriteAttributeString("direction", direction);
            w.WriteAttributeString("factor",    factor.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndElement();
        }

        private static void WriteSetting(XmlWriter w, string id, string type, string label,
            string min, string max, string defaultVal)
        {
            w.WriteStartElement("Setting");
            w.WriteAttributeString("id",      id);
            w.WriteAttributeString("type",    type);
            w.WriteAttributeString("label",   label);
            if (min != null) w.WriteAttributeString("min", min);
            if (max != null) w.WriteAttributeString("max", max);
            w.WriteAttributeString("default", defaultVal);
            w.WriteEndElement();
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
            string fullPath = GetGameObjectPath(current.gameObject);
            PlayMakerFSM[] fsms = current.GetComponents<PlayMakerFSM>();

            if (fsms.Length > 0)
            {
                foreach (PlayMakerFSM fsm in fsms)
                {
                    string safePath = "\"" + fullPath + "\"";
                    string safeFsm  = "\"" + fsm.FsmName + "\"";
                    bool anyVar = false;

                    foreach (FsmFloat fv in fsm.FsmVariables.FloatVariables)
                    {
                        csv.AppendLine(safePath + ";" + safeFsm + ";Float;\"" + fv.Name + "\";" + fv.Value);
                        anyVar = true;
                    }
                    foreach (FsmInt iv in fsm.FsmVariables.IntVariables)
                    {
                        csv.AppendLine(safePath + ";" + safeFsm + ";Int;\"" + iv.Name + "\";" + iv.Value);
                        anyVar = true;
                    }
                    foreach (FsmBool bv in fsm.FsmVariables.BoolVariables)
                    {
                        csv.AppendLine(safePath + ";" + safeFsm + ";Bool;\"" + bv.Name + "\";" + bv.Value);
                        anyVar = true;
                    }

                    if (!anyVar)
                        csv.AppendLine(safePath + ";" + safeFsm + ";N/A;N/A;0");
                }
            }
            else
            {
                csv.AppendLine("\"" + fullPath + "\";None;None;None;0");
            }

            foreach (Transform child in current)
                RecursiveCSV(child, csv);
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
