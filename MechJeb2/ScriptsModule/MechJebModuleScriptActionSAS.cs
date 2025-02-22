﻿using System.Collections.Generic;
using UnityEngine;

namespace MuMech
{
    public class MechJebModuleScriptActionSAS : MechJebModuleScriptAction
    {
        public static string NAME = "SAS";

        private readonly List<Part>   commandParts      = new List<Part>();
        private readonly List<string> commandPartsNames = new List<string>();

        [Persistent(pass = (int)Pass.Type)]
        private int actionType;

        [Persistent(pass = (int)Pass.Type)]
        private bool onActiveVessel = true;

        [Persistent(pass = (int)Pass.Type)]
        private EditableInt selectedPartIndex = 0;

        [Persistent(pass = (int)Pass.Type)]
        private uint selectedPartFlightID;

        private          bool         partHighlighted;
        private readonly List<string> actionTypes = new List<string>();

        public MechJebModuleScriptActionSAS(MechJebModuleScript scriptModule, MechJebCore core, MechJebModuleScriptActionsList actionsList) : base(
            scriptModule, core, actionsList, NAME)
        {
            actionTypes.Clear();
            actionTypes.Add("Enable");
            actionTypes.Add("Disable");
            commandParts.Clear();
            commandPartsNames.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.state != Vessel.State.DEAD)
                {
                    foreach (Part part in vessel.Parts)
                    {
                        if (part.HasModule<ModuleCommand>() && !part.name.Contains("mumech"))
                        {
                            commandParts.Add(part);
                            commandPartsNames.Add(part.name);
                        }
                    }
                }
            }
        }

        public override void activateAction()
        {
            base.activateAction();
            Vessel vessel;
            if (onActiveVessel)
            {
                vessel = FlightGlobals.ActiveVessel;
            }
            else
            {
                vessel = commandParts[selectedPartIndex].vessel;
            }

            if (actionType == 0)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }
            else
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }

            endAction();
        }

        public override void endAction()
        {
            base.endAction();
        }

        public override void WindowGUI(int windowID)
        {
            preWindowGUI(windowID);
            base.WindowGUI(windowID);
            GUILayout.Label("SAS");
            actionType     = GuiUtils.ComboBox.Box(actionType, actionTypes.ToArray(), actionTypes);
            onActiveVessel = GUILayout.Toggle(onActiveVessel, "On active Vessel");
            if (!onActiveVessel)
            {
                selectedPartIndex = GuiUtils.ComboBox.Box(selectedPartIndex, commandPartsNames.ToArray(), commandPartsNames);
                if (commandParts[selectedPartIndex] != null)
                {
                    if (!partHighlighted)
                    {
                        if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view", true), GUILayout.ExpandWidth(false)))
                        {
                            partHighlighted = true;
                            commandParts[selectedPartIndex].SetHighlight(true, false);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view_a", true), GUILayout.ExpandWidth(false)))
                        {
                            partHighlighted = false;
                            commandParts[selectedPartIndex].SetHighlight(false, false);
                        }
                    }
                }
            }

            if (selectedPartIndex < commandParts.Count)
            {
                selectedPartFlightID = commandParts[selectedPartIndex].flightID;
            }

            postWindowGUI(windowID);
        }

        public override void postLoad(ConfigNode node)
        {
            if (selectedPartFlightID !=
                0) //We check if a previous flightID was set on the parts. When switching MechJeb Cores and performing save/load of the script, the port order may change so we try to rely on the flight ID to select the right part.
            {
                int i = 0;
                foreach (Part part in commandParts)
                {
                    if (part.flightID == selectedPartFlightID)
                    {
                        selectedPartIndex = i;
                    }

                    i++;
                }
            }
        }
    }
}
