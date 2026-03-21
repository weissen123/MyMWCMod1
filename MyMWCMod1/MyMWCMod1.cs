using HutongGames.PlayMaker;
using MSCLoader;
using System;
using System.Linq;
using System.Net.Sockets;
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

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.FixedUpdate, Mod_FixedUpdate); 
            SetupFunction(Setup.ModSettings, Mod_Settings);
            //SetupFunction(Setup );
        }

        private static GameObject machtwg;
        private static Drivetrain drivetrain;

        private static FsmFloat corrisOilFiltDirt;
        private static float corrisOilFiltDirt_last;
        private static FsmFloat corrisOilLevel;
        private static float corrisOilLevel_last;
        
        private static FsmFloat corrisWearBulbL;
        private static float corrisWearBulbL_last;
        private static FsmFloat corrisWearBulbR;
        private static float corrisWearBulbR_last;

        public SettingsCheckBox autoTransmission;
        public SettingsSlider   shiftUpRPMSetting;
        public SettingsSlider   shiftDownRPMSetting;

        private void Mod_Settings()
        {
            autoTransmission    = Settings.AddCheckBox("autoTransmission", "Automated Manual Transmission (AMT)", true);
            shiftUpRPMSetting   = Settings.AddSlider("shiftUpRPM", "Shift Up RPM", 1000f, 8000f, 3500f);
            shiftDownRPMSetting = Settings.AddSlider("shiftDownRPM", "Shift Down RPM", 500f, 7000f, 1700f);
        }

        private string[] EnumToStringList<T>() where T : struct
        {
            return Enum.GetNames(typeof(T));
        }

        private T StringToEnum<T>(string value) where T : struct
        {
            return (T)Enum.Parse(typeof(T), value);
        }

        private FsmFloat findFsmFloat(string ObjectName, string FsmName, string FsmFloatName, string FsmObjIdent)
        {
            GameObject objGameObject;
            FsmFloat   retObjFsmFloat;

            objGameObject = GameObject.Find(ObjectName);
            if (objGameObject == null)
            {
                ModConsole.Error($"FAILED TO FIND {FsmObjIdent}!!!");
            }
            else
            {
                PlayMakerFSM objFsm;
                objFsm = objGameObject.GetComponentsInChildren<PlayMakerFSM>().ToList()
                    .Find((PlayMakerFSM fsm) => fsm.FsmName == FsmName);

                if (objFsm != null)
                {
                    retObjFsmFloat = objFsm.FsmVariables.FindFsmFloat(FsmFloatName);
                    ModConsole.Log($"{FsmObjIdent} {retObjFsmFloat.Value}");
                    return retObjFsmFloat;
                }
            }
            return null;
        }

        private void Mod_OnLoad()
        {
            // Called once, when mod is loading after game is fully loaded
            machtwg = GameObject.Find("JOBS/TAXIJOB/MACHTWAGEN");

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

            corrisOilLevel = findFsmFloat("CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilpan/Oilpan(VINXX)",
                                          "Data", "OilLevel", "OilLevel");
            if (corrisOilLevel != null)
                corrisOilLevel_last = corrisOilLevel.Value;

            GameObject corrisOilFilt;
            corrisOilFilt = GameObject.Find("CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Oilfilter");
            if (corrisOilFilt == null)
            {
                ModConsole.Error("FAILED TO FIND OilFilter!!!");
            } else
            {
                PlayMakerFSM corrisOilFiltData;
                corrisOilFiltData = corrisOilFilt.GetComponentsInChildren<PlayMakerFSM>().ToList()
                    .Find((PlayMakerFSM fsm) => fsm.FsmName == "Data");

                if (corrisOilFiltData != null)
                {
                    corrisOilFiltDirt = corrisOilFiltData.FsmVariables.FindFsmFloat("Dirt");
                    corrisOilFiltDirt_last = corrisOilFiltDirt.Value;
                    ModConsole.Log($"OilFiltDirt {corrisOilFiltDirt_last}");
                }
                
            }

            GameObject corrisHeadlightLeft;
            corrisHeadlightLeft = GameObject.Find("CORRIS/Assemblies/VINP_HeadlightLeft/Head Light Assembly(VINXX)");
            if (corrisHeadlightLeft == null)
            {
                ModConsole.Error("FAILED TO FIND Bulb Left!!!");
            }
            else
            {
                PlayMakerFSM corrisHeadlightData;
                corrisHeadlightData = corrisHeadlightLeft.GetComponentsInChildren<PlayMakerFSM>().ToList()
                    .Find((PlayMakerFSM fsm) => fsm.FsmName == "Data");

                if (corrisHeadlightData != null)
                {
                    corrisWearBulbL = corrisHeadlightData.FsmVariables.FindFsmFloat("WearBulb");
                    corrisWearBulbL_last = corrisWearBulbL.Value;
                    ModConsole.Log($"WearBulbLeft {corrisWearBulbL_last}");
                }

            }

            GameObject corrisHeadlightRight;
            corrisHeadlightRight = GameObject.Find("CORRIS/Assemblies/VINP_HeadlightRight/Head Light Assembly(VINXX)");
            if (corrisHeadlightRight == null)
            {
                ModConsole.Error("FAILED TO FIND Bulb Right!!!");
            }
            else
            {
                PlayMakerFSM corrisHeadlightData;
                corrisHeadlightData = corrisHeadlightRight.GetComponentsInChildren<PlayMakerFSM>().ToList()
                    .Find((PlayMakerFSM fsm) => fsm.FsmName == "Data");

                if (corrisHeadlightData != null)
                {
                    corrisWearBulbR = corrisHeadlightData.FsmVariables.FindFsmFloat("WearBulb");
                    corrisWearBulbR_last = corrisWearBulbR.Value;
                    ModConsole.Log($"WearBulbRight {corrisWearBulbR_last}");
                }

            }

        }

        private void Mod_FixedUpdate()
        {
            if (corrisOilFiltDirt != null)
            {
                if (corrisOilFiltDirt.Value > corrisOilFiltDirt_last)
                {
                    corrisOilFiltDirt.Value = corrisOilFiltDirt_last + (corrisOilFiltDirt.Value - corrisOilFiltDirt_last) * 0.01f;
                }
                corrisOilFiltDirt_last = corrisOilFiltDirt.Value;
            }

            if (corrisOilLevel != null)
            {
                if (corrisOilLevel.Value < corrisOilLevel_last)
                {
                    corrisOilLevel.Value = corrisOilLevel_last + (corrisOilLevel.Value - corrisOilLevel_last) * 0.01f;
                }
                corrisOilLevel_last = corrisOilLevel.Value;
            }

            if (corrisWearBulbL != null)
            {
                if (corrisWearBulbL.Value < corrisWearBulbL_last)
                {
                    corrisWearBulbL.Value = corrisWearBulbL_last + (corrisWearBulbL.Value - corrisWearBulbL_last) * 0.01f;
                }
                corrisWearBulbL_last = corrisWearBulbL.Value;
            }

            if (corrisWearBulbR != null)
            {
                if (corrisWearBulbR.Value < corrisWearBulbR_last)
                {
                    corrisWearBulbR.Value = corrisWearBulbR_last + (corrisWearBulbR.Value - corrisWearBulbR_last) * 0.01f;
                }
                corrisWearBulbR_last = corrisWearBulbR.Value;
            }
        }
    }
}
