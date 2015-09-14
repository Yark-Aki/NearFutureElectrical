﻿/// FissionReactor
/// ---------------------------------------------------
/// Fission Generator!
///

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NearFutureElectrical
{
    public class FissionReactor: ModuleResourceConverter
    {
        public struct ResourceBaseRatio
        {
            public string ResourceName;
            public double ResourceRatio;

            public ResourceBaseRatio(string name, double ratio)
            {
                ResourceName = name;
                ResourceRatio = ratio;
            }
         }

        /// CONFIGURABLE FIELDS
        // ----------------------

        // Whether to use a staging icon or not
        [KSPField(isPersistant = false)]
        public bool UseStagingIcon = true;

        // Force activate on load or not
        [KSPField(isPersistant = false)]
        public bool UseForcedActivation = true;

        // Heat generation at full power
        [KSPField(isPersistant = false)]
        public float HeatGeneration;

        // Nominal reactor temperature (where the reactor should live)
        [KSPField(isPersistant = false)]
        public float NominalTemperature = 900f;

        // Critical reactor temperature (core damage after this)
        [KSPField(isPersistant = false)]
        public float CriticalTemperature = 1400f;

        // Current reactor power setting (0-100, tweakable)
        [KSPField(isPersistant = true, guiActive = true, guiName = "Power Setting"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float CurrentPowerPercent = 50f;

        // Curve relating available power to temperature. Generally should be of the form
        // AmbientTemp  0
        // NominalTemp RatedReactorOutput
        // MaxTemp BonusReactorOutput
        KSPField(isPersistant = false)]
        public FloatCurve PowerCurve = new FloatCurve();

        // amount of heating power available from reactor currently
        [KSPField(isPersistant = true)]
        public float AvailablePower = 0f;

        // Name of the fuel
        [KSPField(isPersistant = false)]
        public string FuelName = "EnrichedUranium";

        // name of the overheat animation
        [KSPField(isPersistant = false)]
        public string OverheatAnimation;

        // REPAIR VARIABLES
        // integrity of the core
        [KSPField(isPersistant = true)]
        public float CoreIntegrity = 100f;

        // Rate the core is damaged, in % per S per K
        [KSPField(isPersistant = false)]
        public float CoreDamageRate = 0.005f;

        // Engineer level to repair the core
        [KSPField(isPersistant = false)]
        public int EngineerLevelForRepair = 5;

        [KSPField(isPersistant = false)]
        public float MaxRepairPercent = 75;

        [KSPField(isPersistant = false)]
        public float MinRepairPercent = 10;

        [KSPField(isPersistant = false)]
        public float MaxTempForRepair = 325;

        // Try to fix the reactor
        [KSPEvent(externalToEVAOnly = true, guiActiveUnfocused = true, unfocusedRange = 3.5f, guiName = "Repair Reactor")]
        public void RepairReactor()
        {
            if (TryRepairReactor)
            {
              DoReactorRepair();
            }
        }

        /// PRIVATE VARIABLES
        /// ----------------------
        // the info staging box
        private VInfoBox infoBox;

        private AnimationState[] overheatStates;

        // base paramters
        private List<ResourceBaseRatio> inputs;
        private List<ResourceBaseRatio> outputs;

        /// UI FIELDS
        /// --------------------
        // Fuel Status string
        [KSPField(isPersistant = false, guiActive = true, guiName = "Core Life")]
        public string FuelStatus;

        // Reactor Status string
        [KSPField(isPersistant = false, guiActive = true, guiName = "Thermal Output")]
        public string ThermalOutput;

        // Reactor Status string
        [KSPField(isPersistant = false, guiActive = true, guiName = "Reactor Temperature")]
        public string ReactorTemp;

        // integrity of the core
        [KSPField(isPersistant = false, guiActive = true, guiName = "Core Health")]
        public string CoreStatus;

        public override string GetInfo()
        {
            double baseRate = 0d;
            foreach (ResourceRatio input in inputList)
            {
                if (input.ResourceName == FuelName)
                    baseRate = input.Ratio;
            }
            return base.GetInfo() +
                String.Format("Heat Production: {0:F2} kW", HeatGeneration) + "\n"
                + String.Format("Optimal Temperature: {0:F0} K", NominalTemperature) + "\n"
                + String.Format("Critical Temperature: {0:F0} K", CriticalTemperature) + "\n"
                + "Estimated Core Life: " +
                FindTimeRemaining(this.part.Resources.Get(PartResourceLibrary.Instance.GetDefinition(FuelName).id).amount,baseRate) ;
        }

        private void SetupResourceRatios()
        {

            inputs = new List<ResourceBaseRatio>();
            outputs = new List<ResourceBaseRatio>();

            foreach (ResourceRatio input in inputList)
            {
                inputs.Add(new ResourceBaseRatio(input.ResourceName, input.Ratio));
            }
            foreach (ResourceRatio output in outputList)
            {
                outputs.Add(new ResourceBaseRatio(output.ResourceName, output.Ratio));
            }
        }

        public override void OnStart(PartModule.StartState state)
        {
            if (UseStagingIcon)
                this.part.stagingIcon = "FUEL_TANK";
            else
                Utils.LogWarn("Fission Reactor: Staging Icon Disabled!");

            if (state != StartState.Editor)
            {
                SetupResourceRatios();
                // Set up staging icon heat bar
                if (UseStagingIcon)
                {
                    infoBox = this.part.stackIcon.DisplayInfo();
                    infoBox.SetMsgBgColor(XKCDColors.RedOrange);
                    infoBox.SetMsgTextColor(XKCDColors.Orange);
                    infoBox.SetLength(1.0f);
                    infoBox.SetValue(0.0f);
                    infoBox.SetMessage("Meltdwn");
                    infoBox.SetProgressBarBgColor(XKCDColors.RedOrange);
                    infoBox.SetProgressBarColor(XKCDColors.Orange);
                }

                if (OverheatAnimation != "")
                {
                    overheatStates = Utils.SetUpAnimation(OverheatAnimation, this.part);


                }
                if (UseForcedActivation)
                    this.part.force_activate();
            }
        }

        private void FixedUpdate()
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                // Update reactor temp readout
                ReactorTemp = String.Format("{0:F1} K", part.temperature);

                // Update reactor core integrity readout
                if (CoreIntegrity > 0)
                    CoreStatus = String.Format("{0:F2} %", CoreIntegrity);
                else
                    CoreStatus = "Complete Meltdown";

                // Create and distribute heat
                CreateAndDistributeHeat();
                // Handle core damage tracking and effects
                HandleCoreDamage();


                // IF REACTOR ON
                // =============
                if (base.ModuleIsActive())
                {
                    // Get current resource consumption
                    double rate = 0d;
                    foreach (ResourceRatio input in inputList)
                    {
                        if (input.ResourceName == FuelName)
                            rate = input.Ratio;
                    }

                    // add reactor heat
                    float heatAddedByReactor = HeatGeneration * CurrentPowerPercent / 100f;

                    // REPLACE with 1.1 method
                    this.part.AddThermalFlux(heatAddedByReactor);

                    // Recalculate fuel use Ratio
                    // Fuel use is proportional to current power + inverse of current core integrity
                    RecalculateRatios((CurrentPowerPercent + 2f*(100f-CoreIntegrity) ) / 100f);

                    // Update UI
                    // current core life
                    FuelStatus = FindTimeRemaining(
                      this.part.Resources.Get(PartResourceLibrary.Instance.GetDefinition(FuelName).id).amount,
                      rate);
                }
                // IF REACTOR OFF
                // =============
                else
                {
                    // Update UI
                    if (CoreIntegrity <= 0f)
                    {
                        FuelStatus = "Core Destroyed";
                        ThermalOutput = "Core Destroyed";
                    }
                    else
                    {
                        FuelStatus = "Reactor Offline";
                        if (AvailablePower <= 10f)
                        {
                            ThermalOutput = String.Format("{0:F0} kW", AvailablePower);
                        } else
                        {
                          ThermalOutput = "Reactor Offline";
                        }
                    }
                }
            }
        }

        // Create heat based on reactor temp and distribute it
        private void CreateAndDistributeHeat()
        {
          // Calculate the power from the core temperature
          AvailablePower = PowerCurve.Evaluate(part.temperature);

          // Update UI
          ThermalOutput = String.Format("{0:F0} kW", AvailablePower);

          Utils.Log("FissionReactor: has " + AvailablePower.ToString() +" kW to distribute");

          // Get consumers and sort by priority
          List<FissionConsumer> consumers = this.GetComponents<FissionConsumer>();
          List<FissionConsumer> sortedConsumers = consumers.OrderBy(o>o.Priority).ToList();

          // allocate power to all consumers
          foreach (FissionConsumer consumer in consumers)
          {
            if (consumer.Status)
            {
              float usage = TryConsumeHeat(consumer.HeatUsed)
              consumer.CurrentHeatUsed = usage;
              AvailablePower = availablePower - usage;
              if (AvailablePower <= 0f)
                AvailablePower = 0f;
              Utils.Log ("FissionReactor: Consumer used "+ usage.ToString()+ " kW, " +AvailablePower.ToString() +" remaining");
            }
          }
        }

        // A consumer tries to get heat from the pool
        private float TryConsumeHeat(float powerRequired)
        {
          if (AvailablePower >= powerRequired)
            return powerRequired;
          else
            return Mathf.Clamp(AvailablePower-powerRequired,0f,10000000f);
        }

        // track and set core damage
        private void HandleCoreDamage()
        {
          // Update reactor damage
          float critExceedance = (float)part.temperature - CriticalTemperature;

          // If overheated too much, damage the core
          if (critExceedance > 0f)
          {
              // core is damaged by Rate * temp exceedance * time
              CoreIntegrity = Mathf.MoveTowards(CoreIntegrity, 0f, CoreDamageRate * critExceedance * TimeWarp.fixedDeltaTime);
          }

          // Calculate percent exceedance of nominal temp
          float tempNetScale = 1f - Mathf.Clamp01((float)((part.temperature - NominalTemperature) / (part.maxTemp - NominalTemperature)));

          // update staging bar if in use
          if (UseStagingIcon)
              infoBox.SetValue(1f-tempNetScale);

          if (OverheatAnimation != "")
          {
              foreach (AnimationState cState in overheatStates)
              {
                  cState.normalizedTime = 1f - tempNetScale;
              }
          }
        }

        // Set ModuleResourceConverter ratios based on an input scale
        private void RecalculateRatios(float fuelInputScale)
        {
            foreach (ResourceRatio input in inputList)
            {
                foreach (ResourceBaseRatio baseInput in inputs)
                {
                    if (baseInput.ResourceName == input.ResourceName)
                    {
                        input.Ratio = baseInput.ResourceRatio * fuelInputScale;
                    }
                }
            }
            foreach (ResourceRatio output in outputList)
            {
                foreach (ResourceBaseRatio baseOutput in outputs)
                {
                    if (baseOutput.ResourceName == output.ResourceName)
                    {
                          output.Ratio = baseOutput.ResourceRatio * fuelInputScale;
                    }
                }
            }
        }


        // ####################################
        // Repairing
        // ####################################

        public bool TryRepairReactor()
        {
          if (CoreIntegrity <= MinRepairPercent)
          {
              ScreenMessages.PostScreenMessage(new ScreenMessage("Reactor core is too damaged to repair.", 5.0f, ScreenMessageStyle.UPPER_CENTER));
              return false;
          }
          if (!CheckEVAEngineerLevel(EngineerLevelForRepair))
          {
              ScreenMessages.PostScreenMessage(new ScreenMessage(String.Format("Reactor core repair requires a Level {0:F0} Engineer."), 5.0f, ScreenMessageStyle.UPPER_CENTER));
              return false;
          }
          if (base.ModuleIsActive())
          {
              ScreenMessages.PostScreenMessage(new ScreenMessage("Cannot repair reactor core while running! Seriously!",
                  5.0f, ScreenMessageStyle.UPPER_CENTER));
              return false;
          }
          if (part.temperature > MaxTempForRepair)
          {
              ScreenMessages.PostScreenMessage(new ScreenMessage(String.Format("The reactor must be below {0:F0} K to initiate repair!", MaxTempForRepair), 5.0f, ScreenMessageStyle.UPPER_CENTER));
              return false;
          }
          if (CoreIntegrity >= MaxRepairPercent)
          {
              ScreenMessages.PostScreenMessage(new ScreenMessage(String.Format("Reactor core is already at maximum field repairable integrity ({0:F0})", MaxRepairPercent),
                  5.0f, ScreenMessageStyle.UPPER_CENTER));
              return false;
          }
          return true;
        }

        // Repair the reactor to max Repair percent
        public void DoReactorRepair()
        {
            this.CoreIntegrity = MaxRepairPercent;
            ScreenMessages.PostScreenMessage(new ScreenMessage(String.Format("Reactor repaired to {0:F0}%!", MaxRepairPercent), 5.0f, ScreenMessageStyle.UPPER_CENTER));
        }

        // Check the current EVA engineer's level
        private bool CheckEVAEngineerLevel(int level)
        {
            ProtoCrewMember kerbal = FlightGlobals.ActiveVessel.rootPart.protoModuleCrew[0];
            if (kerbal.experienceTrait.Title == "Engineer" && kerbal.experienceLevel >= level)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // ####################################
        // Refuelling
        // ####################################

        // Finds time remaining at specified fuel burn rates
        public string FindTimeRemaining(double amount, double rate)
        {
            if (rate < 0.0000001)
            {
                return "A long time!";
            }
            double remaining = amount / rate;
            TimeSpan t = TimeSpan.FromSeconds(remaining);

            if (remaining >= 0)
            {
                return Utils.FormatTimeString(remaining);
            }
            {
                return "No fuel remaining";
            }
        }
    }
}
