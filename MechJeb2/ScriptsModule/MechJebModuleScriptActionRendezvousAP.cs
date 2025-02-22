﻿using UnityEngine;

namespace MuMech
{
    public class MechJebModuleScriptActionRendezvousAP : MechJebModuleScriptAction
    {
        public static string NAME = "RendezvousAP";

        [Persistent(pass = (int)Pass.Type)]
        private readonly EditableDouble desiredDistance = 100;

        [Persistent(pass = (int)Pass.Type)]
        private readonly EditableDouble maxPhasingOrbits = 5;

        [Persistent(pass = (int)Pass.Type)]
        private bool autowarp;

        private readonly MechJebModuleRendezvousAutopilot       autopilot;
        private readonly MechJebModuleRendezvousAutopilotWindow module;

        public MechJebModuleScriptActionRendezvousAP(MechJebModuleScript scriptModule, MechJebCore core, MechJebModuleScriptActionsList actionsList) :
            base(scriptModule, core, actionsList, NAME)
        {
            autopilot = core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
            module    = core.GetComputerModule<MechJebModuleRendezvousAutopilotWindow>();
            readModuleConfiguration();
        }

        public override void readModuleConfiguration()
        {
            autowarp = core.node.autowarp;
        }

        public override void writeModuleConfiguration()
        {
            core.node.autowarp = autowarp;
        }

        public override void activateAction()
        {
            base.activateAction();

            writeModuleConfiguration();
            autopilot.users.Add(module);
            autopilot.enabled = true;

            endAction();
        }

        public override void endAction()
        {
            base.endAction();

            autopilot.users.Remove(module);
        }

        public override void WindowGUI(int windowID)
        {
            preWindowGUI(windowID);
            base.WindowGUI(windowID);
            GUILayout.Label("Rendezvous Autopilot");
            if (autopilot != null)
            {
                if (!autopilot.enabled)
                {
                    GuiUtils.SimpleTextBox("final distance:", autopilot.desiredDistance, "m");
                    GuiUtils.SimpleTextBox("Max # of phasing orb.:", autopilot.maxPhasingOrbits);

                    if (autopilot.maxPhasingOrbits < 5)
                    {
                        GUILayout.Label("Max # of phasing orb. must be at least 5.", GuiUtils.yellowLabel);
                    }
                }
                else
                {
                    GUILayout.Label("Status: " + autopilot.status);
                }
            }

            postWindowGUI(windowID);
        }

        public override void afterOnFixedUpdate()
        {
            if (isStarted() && !isExecuted() && autopilot.enabled == false)
            {
                endAction();
            }
        }
    }
}
