using HutongGames.PlayMaker;
using MSCLoader;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MyMWCMod1
{
    public class MyMWCMod1 : Mod
    {
        public override string ID => "MyMWCMod1"; // Your (unique) mod ID
        public override string Name => "MyMWCMod1"; // Your mod name
        public override string Author => "JWE"; // Name of the Author (your name)
        public override string Version => "1.0"; // Version
        public override string Description => ""; // Short description of your mod
        public override Game SupportedGames => Game.MyWinterCar;

        // Game object paths
        private const string Path_Taxi      = "JOBS/TAXIJOB/MACHTWAGEN";
        private const string Path_Oilpan    = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilpan/Oilpan(VINXX)";
        private const string Path_OilFilter = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilfilter";
        private const string Path_BulbLeft   = "CORRIS/Assemblies/VINP_HeadlightLeft/Head Light Assembly(VINXX)";
        private const string Path_BulbRight  = "CORRIS/Assemblies/VINP_HeadlightRight/Head Light Assembly(VINXX)";
        private const string Path_Alternator  = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Alternator";
        private const string Path_BrakeMaster = "CORRIS/Assemblies/VINP_BrakeMasterCylinder/Brake Master Cylinder(VINXX)";
        private const string Path_Heaterbox   = "CORRIS/Assemblies/VINP_Heaterbox/Heater Box(VINXX)";
        private const string Path_SparkPlug1 = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug1";
        private const string Path_SparkPlug2 = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug2";
        private const string Path_SparkPlug3 = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug3";
        private const string Path_SparkPlug4 = "CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/VINP_Sparkplug4";

        // FSM names and variable names
        private const string FsmName_Data    = "Data";
        private const string FsmVar_OilLevel = "OilLevel";
        private const string FsmVar_Dirt     = "Dirt";
        private const string FsmVar_WearBulb = "WearBulb";
        private const string FsmVar_Wear        = "Wear";
        private const string FsmVar_BrakeFluidF = "BrakeFluidF";

        // Wear reduction factor applied each FixedUpdate tick (1% of delta survives)
        private const float WearReductionFactor = 0.01f;

        private enum WearDirection { Increases, Decreases }

        private class ComponentMonitor
        {
            public FsmFloat Value;
            public float    Previous;

            public void ApplyReduction(float factor, WearDirection direction)
            {
                if (Value == null) return;

                bool conditionMet = direction == WearDirection.Increases
                    ? Value.Value > Previous
                    : Value.Value < Previous;

                if (conditionMet)
                    Value.Value = Previous + (Value.Value - Previous) * factor;

                Previous = Value.Value;
            }
        }

        private static GameObject       machtwg;
        private static Drivetrain       drivetrain;
        private static ComponentMonitor _oilFiltDirt = new ComponentMonitor();
        private static ComponentMonitor _oilLevel    = new ComponentMonitor();
        private static ComponentMonitor _wearBulbL    = new ComponentMonitor();
        private static ComponentMonitor _wearBulbR    = new ComponentMonitor();
        private static ComponentMonitor _alternator   = new ComponentMonitor();
        private static ComponentMonitor _brakeFluidF  = new ComponentMonitor();
        private static ComponentMonitor _heaterbox    = new ComponentMonitor();
        private static ComponentMonitor _sparkPlug1   = new ComponentMonitor();
        private static ComponentMonitor _sparkPlug2   = new ComponentMonitor();
        private static ComponentMonitor _sparkPlug3   = new ComponentMonitor();
        private static ComponentMonitor _sparkPlug4   = new ComponentMonitor();

        private SettingsCheckBox autoTransmission;
        private SettingsSlider   shiftUpRPMSetting;
        private SettingsSlider   shiftDownRPMSetting;

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.FixedUpdate, Mod_FixedUpdate);
            SetupFunction(Setup.ModSettings, Mod_Settings);
        }

        private void Mod_Settings()
        {
            autoTransmission    = Settings.AddCheckBox("autoTransmission", "Automated Manual Transmission (AMT)", true);
            shiftUpRPMSetting   = Settings.AddSlider("shiftUpRPM", "Shift Up RPM", 1000f, 8000f, 3500f);
            shiftDownRPMSetting = Settings.AddSlider("shiftDownRPM", "Shift Down RPM", 500f, 7000f, 1700f);
            Settings.AddButton("Dump CORRIS FSM to CSV", () => DumpToCSV("CORRIS"));
            Settings.AddButton("Dump BACHGLOTZ FSM to CSV", () => DumpToCSV("BACHGLOTZ(1905kg)"));
        }

        private void Mod_OnLoad()
        {
            SetupDrivetrain();
            SetupMonitors();
        }

        private void Mod_FixedUpdate()
        {
            _oilFiltDirt.ApplyReduction(WearReductionFactor, WearDirection.Increases);
            _oilLevel.ApplyReduction(WearReductionFactor,    WearDirection.Decreases);
            _wearBulbL.ApplyReduction(WearReductionFactor,   WearDirection.Decreases);
            _wearBulbR.ApplyReduction(WearReductionFactor,   WearDirection.Decreases);
            _sparkPlug1.ApplyReduction(WearReductionFactor,  WearDirection.Decreases);
            _sparkPlug2.ApplyReduction(WearReductionFactor,  WearDirection.Decreases);
            _sparkPlug3.ApplyReduction(WearReductionFactor,  WearDirection.Decreases);
            _sparkPlug4.ApplyReduction(WearReductionFactor,  WearDirection.Decreases);
            _alternator.ApplyReduction(WearReductionFactor,  WearDirection.Decreases);
            _brakeFluidF.ApplyReduction(WearReductionFactor, WearDirection.Decreases);
            _heaterbox.ApplyReduction(WearReductionFactor,   WearDirection.Decreases);
        }

        private void SetupDrivetrain()
        {
            machtwg = GameObject.Find(Path_Taxi);
            if (machtwg == null)
            {
                ModConsole.Error("FAILED TO FIND Taxi!!!");
                return;
            }

            drivetrain = machtwg.GetComponent<Drivetrain>();
            if (drivetrain != null)
            {
                drivetrain.automatic    = autoTransmission.GetValue();
                drivetrain.shiftUpRPM   = (float)shiftUpRPMSetting.GetValue();
                drivetrain.shiftDownRPM = (float)shiftDownRPMSetting.GetValue();
            }
        }

        private void SetupMonitors()
        {
            BindMonitor(_oilLevel,    Path_Oilpan,     FsmVar_OilLevel,    "OilLevel");
            BindMonitor(_oilFiltDirt, Path_OilFilter,  FsmVar_Dirt,        "OilFiltDirt");
            BindMonitor(_wearBulbL,   Path_BulbLeft,   FsmVar_WearBulb,    "WearBulbLeft");
            BindMonitor(_wearBulbR,   Path_BulbRight,  FsmVar_WearBulb,    "WearBulbRight");
            BindMonitor(_sparkPlug1,  Path_SparkPlug1, FsmVar_Wear,        "SparkPlug1");
            BindMonitor(_sparkPlug2,  Path_SparkPlug2, FsmVar_Wear,        "SparkPlug2");
            BindMonitor(_sparkPlug3,  Path_SparkPlug3, FsmVar_Wear,        "SparkPlug3");
            BindMonitor(_sparkPlug4,  Path_SparkPlug4, FsmVar_Wear,        "SparkPlug4");
            BindMonitor(_alternator,  Path_Alternator, FsmVar_Wear,        "Alternator");
            BindMonitor(_brakeFluidF, Path_BrakeMaster, FsmVar_BrakeFluidF, "BrakeFluidF");
            BindMonitor(_heaterbox,   Path_Heaterbox,  FsmVar_Wear,        "Heaterbox");
        }

        private void BindMonitor(ComponentMonitor monitor, string path, string fsmVar, string label)
        {
            FsmFloat f = FindFsmFloat(path, FsmName_Data, fsmVar, label);
            if (f != null)
            {
                monitor.Value    = f;
                monitor.Previous = f.Value;
            }
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
            csv.AppendLine("GameObject Path;FSM Name;Float Variable Name;Float Value");
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
                    FsmFloat[] floatVars = fsm.FsmVariables.FloatVariables;
                    if (floatVars.Length > 0)
                    {
                        foreach (FsmFloat fv in floatVars)
                        {
                            string safePath = $"\"{fullPath}\"";
                            string safeFsm  = $"\"{fsm.FsmName}\"";
                            string safeVar  = $"\"{fv.Name}\"";
                            csv.AppendLine($"{safePath};{safeFsm};{safeVar};{fv.Value}");
                        }
                    }
                    else
                    {
                        csv.AppendLine($"\"{fullPath}\";\"{fsm.FsmName}\";N/A;0");
                    }
                }
            }
            else
            {
                csv.AppendLine($"\"{fullPath}\";None;None;0");
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
                ModConsole.Error($"FAILED TO FIND {logLabel}!!!");
                return null;
            }

            PlayMakerFSM fsm = obj.GetComponentsInChildren<PlayMakerFSM>().ToList()
                .Find((PlayMakerFSM f) => f.FsmName == fsmName);

            if (fsm != null)
            {
                FsmFloat result = fsm.FsmVariables.FindFsmFloat(floatName);
                ModConsole.Log($"{logLabel} {result.Value}");
                return result;
            }

            return null;
        }
    }
}
