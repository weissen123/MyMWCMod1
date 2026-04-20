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

            public static ComponentMonitor LoadFromXml(XmlElement el, string label, string goPath)
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

                FsmFloat value = FindFsmFloat(goPath, fsmName, fsmFloat, label);
                if (value == null) return null;

                return new ComponentMonitor
                {
                    Label     = label,
                    Value     = value,
                    Previous  = value.Value,
                    Direction = direction,
                    Factor    = factor
                };
            }

            private static FsmFloat FindFsmFloat(string objectName, string fsmName, string floatName, string logLabel)
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

            private static readonly List<ComponentMonitor> _instances = new List<ComponentMonitor>();

            public static void Reset() { _instances.Clear(); }
            public static void Add(ComponentMonitor monitor) { _instances.Add(monitor); }

            public static void ApplyAll()
            {
                foreach (ComponentMonitor m in _instances)
                    m.ApplyReduction();
            }
        }

        private class PivotResetConfig
        {
            public string  VehicleName;
            public string  GameObjectPath;
            public Vector3 LocalPosition;
            public Vector3 LocalEulerAngles;

            private GameObject ActiveGO; // set by Resolve(); guaranteed non-null when config is non-null

            private static readonly List<PivotResetConfig> _configs = new List<PivotResetConfig>();
            private static FsmString                        _vehicleString;
            private static string                           _xmlPath;

            public static void Reset() { _configs.Clear(); }
            public static void Add(PivotResetConfig config) { _configs.Add(config); }

            public static void Init(string xmlPath)
            {
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

        private class DrivetrainMonitor
        {
            private interface ICondition
            {
                bool   IsResolved { get; }
                string LogPrefix  { get; } // e.g. "canStall.Condition 'ElectricsOK'"
                bool   Evaluate();
            }

            private class FsmBoolCondition : ICondition
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

                public FsmBoolCondition(string path, string fsmName, string varName, string logLabel)
                {
                    _path = path; _fsmName = fsmName; _varName = varName; _logLabel = logLabel;
                }

                public bool   IsResolved => _resolved != null;
                public string LogPrefix  => _logLabel + " '" + _varName + "'";

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

            private class ComponentFloatCondition : ICondition
            {
                private readonly string _path;
                private readonly string _compName;
                private readonly string _fieldName;
                private readonly float  _minFloat;
                private readonly string _logLabel;

                private const float RetryInterval = 1f;

                private Component                   _cachedComp;
                private System.Reflection.FieldInfo _cachedField;
                private float                       _nextRetryTime;

                public ComponentFloatCondition(string path, string compName, string fieldName, float minFloat, string logLabel)
                {
                    _path = path; _compName = compName; _fieldName = fieldName; _minFloat = minFloat; _logLabel = logLabel;
                }

                public bool   IsResolved => _cachedField != null;
                public string LogPrefix  => _logLabel + " '" + _fieldName + "'";

                public bool Evaluate()
                {
                    // Hot path: field already cached
                    if (_cachedField != null) return (float)_cachedField.GetValue(_cachedComp) >= _minFloat;

                    // Rate-limit retries to once per second
                    if (UnityEngine.Time.fixedTime < _nextRetryTime) return false;
                    _nextRetryTime = UnityEngine.Time.fixedTime + RetryInterval;

                    // Stage 1: find the GameObject and component
                    if (_cachedComp == null)
                    {
                        GameObject go = GameObject.Find(_path);
                        if (go == null) return false;
                        _cachedComp = go.GetComponent(_compName);
                        if (_cachedComp == null) return false;
                    }

                    // Stage 2: find the float field via reflection
                    _cachedField = _cachedComp.GetType().GetField(
                        _fieldName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (_cachedField != null)
                    {
                        ModConsole.Log(_logLabel + " '" + _fieldName + "' resolved = " + (float)_cachedField.GetValue(_cachedComp));
                        return (float)_cachedField.GetValue(_cachedComp) >= _minFloat;
                    }

                    return false;
                }
            }

            private class DrivetrainBoolSetting
            {
                public SettingsCheckBox          Checkbox;
                public System.Reflection.FieldInfo Field;
                public readonly List<ICondition> Conditions = new List<ICondition>(); // empty = always apply

                public void Apply(Drivetrain drivetrain)
                {
                    foreach (ICondition c in Conditions)
                        if (!c.Evaluate()) return;
                    Field.SetValue(drivetrain, Checkbox.GetValue());
                }
            }

            public string     Label;
            public Drivetrain Drivetrain;
            private readonly List<DrivetrainBoolSetting> BoolSettings = new List<DrivetrainBoolSetting>();

            private static readonly List<DrivetrainMonitor>                       _instances        = new List<DrivetrainMonitor>();
            private static readonly Dictionary<string, SettingsCheckBox> _checkboxSettings = new Dictionary<string, SettingsCheckBox>();
            private static readonly Dictionary<string, SettingsSlider>  _sliderSettings   = new Dictionary<string, SettingsSlider>();

            public static void Reset() { _instances.Clear(); }
            public static void Add(DrivetrainMonitor monitor) { if (monitor != null) _instances.Add(monitor); }

            public static void ApplyAll()
            {
                foreach (DrivetrainMonitor m in _instances)
                    m.Apply();
            }

            public static void RegisterSettings(string xmlPath)
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(xmlPath);

                foreach (XmlNode monNode in doc.DocumentElement.ChildNodes)
                {
                    if (monNode.NodeType != XmlNodeType.Element) continue;
                    XmlElement drivetrainEl = (XmlElement)((XmlElement)monNode).SelectSingleNode("Drivetrain");
                    if (drivetrainEl == null) continue;

                    foreach (XmlNode settingNode in drivetrainEl.ChildNodes)
                    {
                        if (settingNode.NodeType != XmlNodeType.Element) continue;
                        XmlElement s    = (XmlElement)settingNode;
                        string     id   = s.GetAttribute("id");
                        string     type = s.GetAttribute("type");
                        if (type == "checkbox") { RegisterCheckboxSetting(s, id); continue; }
                        if (type == "slider")   { RegisterSliderSetting(s, id);   continue; }
                    }
                }
            }

            private static void RegisterCheckboxSetting(XmlElement s, string id)
            {
                bool defBool;
                bool.TryParse(s.GetAttribute("default"), out defBool);
                _checkboxSettings[id] = Settings.AddCheckBox(id, s.GetAttribute("label"), defBool);
            }

            private static void RegisterSliderSetting(XmlElement s, string id)
            {
                var ns = System.Globalization.NumberStyles.Float;
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                float min, max, def;
                float.TryParse(s.GetAttribute("min"),     ns, ic, out min);
                float.TryParse(s.GetAttribute("max"),     ns, ic, out max);
                float.TryParse(s.GetAttribute("default"), ns, ic, out def);
                _sliderSettings[id] = Settings.AddSlider(id, s.GetAttribute("label"), min, max, def);
            }

            public static DrivetrainMonitor LoadFromXml(string path, string label, XmlElement drivetrainEl)
            {
                GameObject go = GameObject.Find(path);
                if (go == null) { ModConsole.Error("FAILED TO FIND " + label + "!!!"); return null; }
                Drivetrain drivetrain = go.GetComponent<Drivetrain>();
                if (drivetrain == null) return null;
                ModConsole.Log("MyMWCMod1: Drivetrain setup for " + label);

                DrivetrainMonitor monitor = new DrivetrainMonitor { Label = label, Drivetrain = drivetrain };
                foreach (XmlNode settingNode in drivetrainEl.ChildNodes)
                {
                    if (settingNode.NodeType != XmlNodeType.Element) continue;
                    XmlElement s    = (XmlElement)settingNode;
                    string     id   = s.GetAttribute("id");
                    string     type = s.GetAttribute("type");
                    if (type == "slider")   { monitor.ApplySliderSetting(s, id);   continue; }
                    if (type == "checkbox") { monitor.ApplyCheckboxSetting(s, id); continue; }
                }
                XmlElement statsEl = (XmlElement)drivetrainEl.SelectSingleNode("Statistics");
                if (statsEl != null)
                    DrivetrainStatisticsCollector.Add(
                        DrivetrainStatisticsCollector.LoadFromXml(statsEl, drivetrain, label));

                XmlElement tcEl = (XmlElement)drivetrainEl.SelectSingleNode("TorqueConverter");
                if (tcEl != null)
                    TorqueConverterSimulator.Add(
                        TorqueConverterSimulator.LoadFromXml(tcEl, drivetrain, label));

                return monitor.BoolSettings.Count > 0 ? monitor : null;
            }

            public void Apply()
            {
                foreach (DrivetrainBoolSetting s in BoolSettings)
                    s.Apply(Drivetrain);
            }

            private System.Reflection.FieldInfo ResolveField(string id)
            {
                System.Reflection.FieldInfo fi = Drivetrain.GetType().GetField(
                    id,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (fi == null)
                    ModConsole.Error("MyMWCMod1: field '" + id + "' not found on Drivetrain.");
                return fi;
            }

            private void ApplySliderSetting(XmlElement s, string id)
            {
                SettingsSlider slider;
                if (!_sliderSettings.TryGetValue(id, out slider)) return;

                System.Reflection.FieldInfo fi = ResolveField(id);
                if (fi == null) return;

                fi.SetValue(Drivetrain, (float)slider.GetValue());
            }

            private void ApplyCheckboxSetting(XmlElement s, string id)
            {
                SettingsCheckBox cb;
                if (!_checkboxSettings.TryGetValue(id, out cb)) return;

                System.Reflection.FieldInfo fi = ResolveField(id);
                if (fi == null) return;

                XmlNodeList condNodes = s.SelectNodes("Condition");
                if (condNodes.Count == 0) { fi.SetValue(Drivetrain, cb.GetValue()); return; }

                BoolSettings.Add(BuildConditionedSetting(condNodes, id, cb, fi));
            }

            private DrivetrainBoolSetting BuildConditionedSetting(XmlNodeList condNodes, string id,
                SettingsCheckBox cb, System.Reflection.FieldInfo field)
            {
                DrivetrainBoolSetting boolSetting = new DrivetrainBoolSetting { Checkbox = cb, Field = field };
                foreach (XmlNode condNode in condNodes)
                {
                    ICondition cond = BuildCondition((XmlElement)condNode, id);
                    if (cond == null) continue;
                    cond.Evaluate();
                    if (!cond.IsResolved)
                        ModConsole.Log(cond.LogPrefix + " not resolved at load — will retry at runtime.");
                    boolSetting.Conditions.Add(cond);
                }
                return boolSetting;
            }

            private static ICondition BuildCondition(XmlElement condEl, string id)
            {
                string path     = condEl.GetAttribute("path");
                string varFloat = condEl.GetAttribute("varFloat");
                string fsmBool  = condEl.GetAttribute("fsmBool");

                if (!string.IsNullOrEmpty(varFloat)) return BuildComponentFloatCondition(condEl, path, id);
                if (!string.IsNullOrEmpty(fsmBool))  return BuildFsmBoolCondition(condEl, path, id);

                ModConsole.Error("MyMWCMod1: Condition for '" + id + "' has neither fsmBool nor varFloat — condition skipped.");
                return null;
            }

            private static ICondition BuildFsmBoolCondition(XmlElement condEl, string path, string id)
            {
                string fsmName = condEl.GetAttribute("fsmName");
                string fsmBool = condEl.GetAttribute("fsmBool");
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(fsmName) || string.IsNullOrEmpty(fsmBool))
                {
                    ModConsole.Error("MyMWCMod1: FsmBool condition for '" + id + "' is missing required attributes — condition skipped.");
                    return null;
                }
                return new FsmBoolCondition(path, fsmName, fsmBool, id + ".Condition");
            }

            private static ICondition BuildComponentFloatCondition(XmlElement condEl, string path, string id)
            {
                string compName = condEl.GetAttribute("CompName");
                string varFloat = condEl.GetAttribute("varFloat");
                string minStr   = condEl.GetAttribute("minFloat");
                float  minFloat;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(compName) || string.IsNullOrEmpty(varFloat)
                    || !float.TryParse(minStr, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out minFloat))
                {
                    ModConsole.Error("MyMWCMod1: ComponentFloat condition for '" + id + "' is missing required attributes — condition skipped.");
                    return null;
                }
                return new ComponentFloatCondition(path, compName, varFloat, minFloat, id + ".Condition");
            }
        }

        private class DrivetrainStatisticsCollector
        {
            private string                        _goName;
            private string                        _fileNameBase;
            private KeyCode                       _keyCode;
            private System.Reflection.FieldInfo[] _fields;
            private string[]                      _fieldNames;
            private System.Reflection.FieldInfo[] _liveFields;
            private string[]                      _liveFieldNames;
            private Drivetrain                    _drivetrain;

            private bool          _collecting;
            private float         _startTime;
            private float         _nextCollectTime;
            private StringBuilder _csv;
            private GUIStyle      _overlayStyle;

            private static readonly List<DrivetrainStatisticsCollector> _instances
                = new List<DrivetrainStatisticsCollector>();

            public static void Reset() { _instances.Clear(); }
            public static void Add(DrivetrainStatisticsCollector c) { if (c != null) _instances.Add(c); }

            public static void UpdateAll()  { foreach (DrivetrainStatisticsCollector c in _instances) c.CheckToggle(); }
            public static void CollectAll() { foreach (DrivetrainStatisticsCollector c in _instances) c.Collect(); }
            public static void DrawAll()    { foreach (DrivetrainStatisticsCollector c in _instances) c.DrawOverlay(); }

            public static DrivetrainStatisticsCollector LoadFromXml(XmlElement statsEl, Drivetrain drivetrain, string goName)
            {
                string fileNameBase = statsEl.GetAttribute("fileName");
                string keyCodeStr   = statsEl.GetAttribute("KeyCode");

                if (string.IsNullOrEmpty(fileNameBase) || string.IsNullOrEmpty(keyCodeStr))
                {
                    ModConsole.Error("MyMWCMod1: <Statistics> for '" + goName + "' missing fileName or KeyCode — skipped.");
                    return null;
                }

                KeyCode keyCode;
                try { keyCode = (KeyCode)System.Enum.Parse(typeof(KeyCode), keyCodeStr, true); }
                catch
                {
                    ModConsole.Error("MyMWCMod1: <Statistics> for '" + goName + "' invalid KeyCode '" + keyCodeStr + "' — skipped.");
                    return null;
                }

                var fieldList     = new List<System.Reflection.FieldInfo>();
                var nameList      = new List<string>();
                var liveFieldList = new List<System.Reflection.FieldInfo>();
                var liveNameList  = new List<string>();
                foreach (XmlNode child in statsEl.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
                    XmlElement statEl   = (XmlElement)child;
                    string     fieldName = statEl.GetAttribute("field");
                    System.Reflection.FieldInfo fi = drivetrain.GetType().GetField(
                        fieldName,
                        System.Reflection.BindingFlags.Public   |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (fi == null)
                    {
                        ModConsole.Error("MyMWCMod1: <Statistics> field '" + fieldName + "' not found on Drivetrain for '" + goName + "' — skipped.");
                        continue;
                    }
                    fieldList.Add(fi);
                    nameList.Add(fieldName);
                    if (!string.IsNullOrEmpty(statEl.GetAttribute("live")))
                    {
                        liveFieldList.Add(fi);
                        liveNameList.Add(fieldName);
                    }
                }

                if (fieldList.Count == 0)
                {
                    ModConsole.Error("MyMWCMod1: <Statistics> for '" + goName + "' has no valid fields — skipped.");
                    return null;
                }

                return new DrivetrainStatisticsCollector
                {
                    _goName          = goName,
                    _fileNameBase    = fileNameBase,
                    _keyCode         = keyCode,
                    _fields          = fieldList.ToArray(),
                    _fieldNames      = nameList.ToArray(),
                    _liveFields      = liveFieldList.ToArray(),
                    _liveFieldNames  = liveNameList.ToArray(),
                    _drivetrain      = drivetrain,
                };
            }

            private void CheckToggle()
            {
                if (!Input.GetKeyDown(_keyCode)) return;
                if (_collecting) StopCollecting(); else StartCollecting();
            }

            private void StartCollecting()
            {
                _csv = new StringBuilder();
                _csv.Append("Time(s)");
                foreach (string n in _fieldNames) { _csv.Append(";"); _csv.Append(n); }
                _csv.AppendLine();
                _startTime        = Time.fixedTime;
                _nextCollectTime  = 0f;
                _collecting       = true;
                ModConsole.Log("MyMWCMod1: Started collecting statistics for " + _goName + ".");
            }

            private void StopCollecting()
            {
                _collecting = false;
                WriteCSV();
            }

            private void Collect()
            {
                if (!_collecting) return;
                if (Time.fixedTime < _nextCollectTime) return;
                _nextCollectTime = Time.fixedTime + 0.1f;
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                _csv.Append((Time.fixedTime - _startTime).ToString("G", ic));
                foreach (System.Reflection.FieldInfo fi in _fields)
                {
                    _csv.Append(";");
                    _csv.Append(Convert.ToSingle(fi.GetValue(_drivetrain)).ToString("G", ic));
                }
                _csv.AppendLine();
            }

            private void WriteCSV()
            {
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName  = _fileNameBase + "_" + timestamp + ".csv";
                File.WriteAllText(fileName, _csv.ToString());
                ModConsole.Log("MyMWCMod1: Statistics written to " + fileName + ".");
            }

            private void DrawOverlay()
            {
                if (!_collecting) return;
                if (_overlayStyle == null)
                {
                    _overlayStyle           = new GUIStyle(GUI.skin.label);
                    _overlayStyle.alignment = TextAnchor.UpperCenter;
                }
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new StringBuilder("Collecting statistics for " + _goName);
                for (int i = 0; i < _liveFields.Length; i++)
                {
                    sb.Append("\n").Append(_liveFieldNames[i]).Append(": ")
                      .Append(Convert.ToSingle(_liveFields[i].GetValue(_drivetrain)).ToString("F3", ic));
                }
                float height = 20f + _liveFields.Length * 20f;
                GUI.Label(new Rect(0, 0, Screen.width, height), sb.ToString(), _overlayStyle);
            }
        }

        private class TorqueConverterSimulator
        {
            private string                 _goName;
            private Drivetrain             _drivetrain;
            private float                  _tStall;
            private float                  _wStall;
            private float                  _rStall;
            private Dictionary<int, float> _gearRatios;

            private System.Reflection.FieldInfo _fEngineAngularVelo;
            private System.Reflection.FieldInfo _fDifferentialSpeed;
            private System.Reflection.FieldInfo _fGear;
            private System.Reflection.FieldInfo _fNetTorque;
            private System.Reflection.FieldInfo _fFinalDriveRatio;

            private bool     _hasData;
            private float    _lastNetTorque;
            private float    _lastTOut;
            private float    _lastNuRatio;
            private float    _lastR;
            private float    _lastOriginalFinalDriveRatio;
            private float    _lastUpdatedFinalDriveRatio;
            private GUIStyle _overlayStyle;

            private static readonly List<TorqueConverterSimulator> _instances
                = new List<TorqueConverterSimulator>();

            public static void Reset()    { _instances.Clear(); }
            public static void Add(TorqueConverterSimulator s) { if (s != null) _instances.Add(s); }
            public static void ApplyAll() { foreach (TorqueConverterSimulator s in _instances) s.Apply(); }
            public static void DrawAll()  { foreach (TorqueConverterSimulator s in _instances) s.DrawOverlay(); }

            public static TorqueConverterSimulator LoadFromXml(XmlElement el, Drivetrain drivetrain, string goName)
            {
                var ns = System.Globalization.NumberStyles.Float;
                var ic = System.Globalization.CultureInfo.InvariantCulture;

                float tStall, wStall, rStall;
                if (!float.TryParse(el.GetAttribute("tStall"), ns, ic, out tStall) ||
                    !float.TryParse(el.GetAttribute("wStall"), ns, ic, out wStall) ||
                    !float.TryParse(el.GetAttribute("rStall"), ns, ic, out rStall))
                {
                    ModConsole.Error("MyMWCMod1: <TorqueConverter> for '" + goName + "' missing or invalid tStall/wStall/rStall — skipped.");
                    return null;
                }

                var gearRatios = new Dictionary<int, float>();
                foreach (XmlNode child in el.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element) continue;
                    XmlElement gearEl = (XmlElement)child;
                    int   gear;
                    float ratio;
                    if (int.TryParse(gearEl.GetAttribute("gear"), out gear) &&
                        float.TryParse(gearEl.GetAttribute("ratio"), ns, ic, out ratio))
                        gearRatios[gear] = ratio;
                    else
                        ModConsole.Error("MyMWCMod1: <GearRatio> for '" + goName + "' invalid gear/ratio — skipped.");
                }

                if (gearRatios.Count == 0)
                {
                    ModConsole.Error("MyMWCMod1: <TorqueConverter> for '" + goName + "' has no valid gear ratios — skipped.");
                    return null;
                }

                System.Reflection.BindingFlags bf =
                    System.Reflection.BindingFlags.Public   |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance;
                Type dt = drivetrain.GetType();

                System.Reflection.FieldInfo fEngineAngularVelo = dt.GetField("engineAngularVelo", bf);
                System.Reflection.FieldInfo fDifferentialSpeed = dt.GetField("differentialSpeed", bf);
                System.Reflection.FieldInfo fGear              = dt.GetField("gear",              bf);
                System.Reflection.FieldInfo fNetTorque         = dt.GetField("netTorque",         bf);
                System.Reflection.FieldInfo fFinalDriveRatio   = dt.GetField("finalDriveRatio",   bf);

                if (fEngineAngularVelo == null || fDifferentialSpeed == null ||
                    fGear             == null  || fNetTorque         == null  ||
                    fFinalDriveRatio  == null)
                {
                    ModConsole.Error("MyMWCMod1: <TorqueConverter> for '" + goName + "' could not resolve one or more Drivetrain fields — skipped.");
                    return null;
                }

                return new TorqueConverterSimulator
                {
                    _goName             = goName,
                    _drivetrain         = drivetrain,
                    _tStall             = tStall,
                    _wStall             = wStall,
                    _rStall             = rStall,
                    _gearRatios         = gearRatios,
                    _fEngineAngularVelo = fEngineAngularVelo,
                    _fDifferentialSpeed = fDifferentialSpeed,
                    _fGear              = fGear,
                    _fNetTorque         = fNetTorque,
                    _fFinalDriveRatio   = fFinalDriveRatio,
                };
            }

            private void Apply()
            {
                _hasData = false;
                float wIn = (float)_fEngineAngularVelo.GetValue(_drivetrain);
                if (wIn <= 0f) return;

                int   gear      = (int)_fGear.GetValue(_drivetrain);
                float baseRatio;
                if (!_gearRatios.TryGetValue(gear, out baseRatio)) return;

                float wOut   = (float)_fDifferentialSpeed.GetValue(_drivetrain) * baseRatio;
                float nu     = wOut / wIn;
                float wRatio = wIn / _wStall;
                float tDrag  = _tStall * wRatio * wRatio * (1f - nu);
                float R      = nu < 0.9f
                             ? _rStall - (_rStall - 1f) * (nu / 0.9f)
                             : 1.0f;

                _lastNuRatio                = wIn / wOut;
                _lastR                      = R;
                _lastTOut                   = tDrag * R;
                _lastNetTorque              = (float)_fNetTorque.GetValue(_drivetrain);
                _lastOriginalFinalDriveRatio = (float)_fFinalDriveRatio.GetValue(_drivetrain);
                _lastUpdatedFinalDriveRatio  = baseRatio * R;
                _hasData                    = true;

                // disabled: writing finalDriveRatio locks ω_out/ω_in, making TCC always applied
                //_fFinalDriveRatio.SetValue(_drivetrain, _lastUpdatedFinalDriveRatio);
            }

            private void DrawOverlay()
            {
                if (!_hasData) return;
                if (_overlayStyle == null)
                {
                    _overlayStyle           = new GUIStyle(GUI.skin.label);
                    _overlayStyle.alignment = TextAnchor.UpperLeft;
                }
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string text = _goName + " TC:"
                    + "\nnetTorque: "     + _lastNetTorque.ToString("F2", ic)
                    + "  T_out: "         + _lastTOut.ToString("F2", ic)
                    + "\nω_in/ω_out: "    + _lastNuRatio.ToString("F3", ic)
                    + "  R(ν): "          + _lastR.ToString("F3", ic)
                    + "\nfinalDriveRatio: " + _lastOriginalFinalDriveRatio.ToString("F4", ic)
                    + "  → "              + _lastUpdatedFinalDriveRatio.ToString("F4", ic);
                GUI.Label(new Rect(10, 10, 500, 80), text, _overlayStyle);
            }
        }

        private class GameObjectCsvDumper
        {
            public static void Dump(string rootName)
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

            private static void RecursiveCSV(Transform current, StringBuilder csv)
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

            private static void AppendFsmRows(StringBuilder csv, string path, PlayMakerFSM fsm)
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

        }

        private class DrivetrainCsvDumper
        {
            public static void Dump(string rootName)
            {
                GameObject root = GameObject.Find(rootName);
                if (root == null) { ModConsole.Error("[MWC Dumper] Could not find " + rootName); return; }

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("GameObject Path;Field Name;Field Type;Access;Scope;Value");

                foreach (Drivetrain dt in root.GetComponentsInChildren<Drivetrain>())
                    AppendRows(csv, dt);

                string fileName = "MWC_Drivetrain_Dump_" + rootName.Replace("/", "_") + ".csv";
                File.WriteAllText(fileName, csv.ToString());
                ModConsole.Log("Dump complete: " + fileName + " saved to game folder.");
            }

            private static void AppendRows(StringBuilder csv, Drivetrain dt)
            {
                string path = "\"" + GetGameObjectPath(dt.gameObject) + "\"";
                foreach (System.Reflection.FieldInfo fi in dt.GetType().GetFields(
                    System.Reflection.BindingFlags.Public    |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance  |
                    System.Reflection.BindingFlags.Static))
                {
                    string access = fi.IsPublic           ? "Public"            :
                                    fi.IsPrivate          ? "Private"           :
                                    fi.IsFamily           ? "Protected"         :
                                    fi.IsAssembly         ? "Internal"          :
                                                            "ProtectedInternal";
                    string scope = fi.IsStatic ? "Static" : "Instance";
                    object val   = fi.GetValue(dt);
                    csv.AppendLine(path + ";\"" + fi.Name + "\";" + fi.FieldType.Name + ";" + access + ";" + scope + ";" + val);
                }
            }
        }

        private static string GetGameObjectPath(GameObject obj)
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

        private SettingsKeybind _pivotResetKey;
        private SettingsKeybind _pivotSaveKey;

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.Update, Mod_Update);
            SetupFunction(Setup.FixedUpdate, Mod_FixedUpdate);
            SetupFunction(Setup.ModSettings, Mod_Settings);
            SetupFunction(Setup.OnGUI, Mod_OnGUI);
        }

        private void Mod_Settings()
        {
            EnsureXmlExists();
            DrivetrainMonitor.RegisterSettings(XmlPath);
            _pivotResetKey = Keybind.Add("pivotReset", "Reset Player Pivot", KeyCode.Backslash);
            _pivotSaveKey  = Keybind.Add("savePivot",  "Save Player Pivot",  KeyCode.Backslash, KeyCode.LeftControl);
            Settings.AddButton("Dump CORRIS FSM to CSV",         () => GameObjectCsvDumper.Dump("CORRIS"));
            Settings.AddButton("Dump BACHGLOTZ FSM to CSV",      () => GameObjectCsvDumper.Dump("BACHGLOTZ(1905kg)"));
            Settings.AddButton("Dump CORRIS Drivetrain to CSV",   () => DrivetrainCsvDumper.Dump("CORRIS"));
            Settings.AddButton("Dump MACHTWAGEN Drivetrain to CSV", () => DrivetrainCsvDumper.Dump("JOBS/TAXIJOB/MACHTWAGEN"));
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

        private void Mod_OnLoad()
        {
            SetupMonitors();
            PivotResetConfig.Init(XmlPath);
        }

        private void Mod_Update()
        {
            DrivetrainStatisticsCollector.UpdateAll();
            if (_pivotSaveKey.GetKeybindDown())  { PivotResetConfig.SaveCurrentPivot();  return; }
            if (_pivotResetKey.GetKeybindDown()) { PivotResetConfig.ResetCurrentPivot(); return; }
        }

        private void Mod_FixedUpdate()
        {
            ComponentMonitor.ApplyAll();
            DrivetrainMonitor.ApplyAll();
            TorqueConverterSimulator.ApplyAll();
            DrivetrainStatisticsCollector.CollectAll();
        }

        private void Mod_OnGUI()
        {
            DrivetrainStatisticsCollector.DrawAll();
            TorqueConverterSimulator.DrawAll();
        }

        private void SetupMonitors()
        {
            ComponentMonitor.Reset();
            DrivetrainMonitor.Reset();
            DrivetrainStatisticsCollector.Reset();
            TorqueConverterSimulator.Reset();
            PivotResetConfig.Reset();
            string xmlPath = XmlPath;
            EnsureXmlExists();
            XmlDocument doc = new XmlDocument();
            doc.Load(xmlPath);

            int componentCount = 0;
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                XmlElement el = (XmlElement)node;

                string label  = el.GetAttribute("label");
                string goPath = el.GetAttribute("path");

                bool isContainer = false;

                XmlElement pivotEl = (XmlElement)el.SelectSingleNode("PivotReset");
                if (pivotEl != null)
                {
                    PivotResetConfig.Add(PivotResetConfig.LoadFromXml(pivotEl));
                    isContainer = true;
                }

                XmlElement drivetrainEl = (XmlElement)el.SelectSingleNode("Drivetrain");
                if (drivetrainEl != null)
                {
                    DrivetrainMonitor.Add(DrivetrainMonitor.LoadFromXml(goPath, label, drivetrainEl));
                    isContainer = true;
                }

                if (isContainer) continue;

                ComponentMonitor monitor = ComponentMonitor.LoadFromXml(el, label, goPath);
                if (monitor != null) { ComponentMonitor.Add(monitor); componentCount++; }
            }

            ModConsole.Log("MyMWCMod1: Loaded " + componentCount + " component monitors from " + xmlPath);
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
      <Setting id=""automatic"" type=""checkbox"" label=""Automated Manual Transmission (AMT)"" default=""true"" />
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
        <Condition path=""CORRIS"" CompName=""Drivetrain"" varFloat=""rpm"" minFloat=""400"" />
      </Setting>
      <Statistics fileName=""MWC_Drivetrain_Stat_CORRIS"" KeyCode=""KeypadEnter"">
        <Statistic field=""gear"" />
        <Statistic field=""throttle"" />
        <Statistic field=""rpm""                live=""X"" />
        <Statistic field=""engineAngularVelo""  live=""X"" />
        <Statistic field=""clutchSpeed""        live=""X"" />
        <Statistic field=""differentialSpeed""  live=""X"" />
        <Statistic field=""finalDriveRatio""    live=""X"" />
        <Statistic field=""torque""             live=""X"" />
        <Statistic field=""frictionTorque""     live=""X"" />
        <Statistic field=""netTorque""          live=""X"" />
        <Statistic field=""velo"" />
        <Statistic field=""wheelTireVelo"" />
        <Statistic field=""slipRatio"" />
        <Statistic field=""currentPower"" />
        <Statistic field=""powerMultiplier"" />
      </Statistics>
      <TorqueConverter tStall=""145"" wStall=""209"" rStall=""2"">
        <GearRatio gear=""2"" ratio=""10.6116"" />
        <GearRatio gear=""3"" ratio=""6.438"" />
        <GearRatio gear=""4"" ratio=""4.44"" />
      </TorqueConverter>
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

    }
}
