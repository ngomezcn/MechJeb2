﻿using System;
using System.Linq;
using UnityEngine;

namespace MuMech
{
    public class MechJebModuleScriptActionWarp : MechJebModuleScriptAction
    {
        public static string NAME = "Warp";

        public enum WarpTarget { Periapsis, Apoapsis, Node, SoI, Time, PhaseAngleT, SuicideBurn, AtmosphericEntry }

        private static readonly string[] warpTargetStrings =
        {
            "periapsis", "apoapsis", "maneuver node", "SoI transition", "Time", "Phase angle", "suicide burn", "atmospheric entry"
        };

        [Persistent(pass = (int)Pass.Type)]
        public WarpTarget warpTarget = WarpTarget.Periapsis;

        [Persistent(pass = (int)Pass.Type)]
        private readonly EditableDouble phaseAngle = 0;

        [Persistent(pass = (int)Pass.Type)]
        public EditableTime leadTime = 0;

        [Persistent(pass = (int)Pass.Type)]
        private readonly EditableTime timeOffset = 0;

        private          double targetUT;
        private          bool   warping;
        private          int    spendTime;
        private readonly int    initTime = 5; //Add a 5s timer after the action to allow time for physics to update before next action
        private          float  startTime;

        public MechJebModuleScriptActionWarp(MechJebModuleScript scriptModule, MechJebCore core, MechJebModuleScriptActionsList actionsList) : base(
            scriptModule, core, actionsList, NAME)
        {
        }

        public override void activateAction()
        {
            base.activateAction();
            warping = true;
            Orbit orbit = scriptModule.orbit;
            VesselState vesselState = scriptModule.vesselState;
            Vessel vessel = FlightGlobals.ActiveVessel;

            switch (warpTarget)
            {
                case WarpTarget.Periapsis:
                    targetUT = orbit.NextPeriapsisTime(vesselState.time);
                    break;

                case WarpTarget.Apoapsis:
                    if (orbit.eccentricity < 1) targetUT = orbit.NextApoapsisTime(vesselState.time);
                    break;

                case WarpTarget.SoI:
                    if (orbit.patchEndTransition != Orbit.PatchTransitionType.FINAL) targetUT = orbit.EndUT;
                    break;

                case WarpTarget.Node:
                    if (vessel.patchedConicsUnlocked() && vessel.patchedConicSolver.maneuverNodes.Any())
                        targetUT = vessel.patchedConicSolver.maneuverNodes[0].UT;
                    break;

                case WarpTarget.Time:
                    targetUT = vesselState.time + timeOffset;
                    break;

                case WarpTarget.PhaseAngleT:
                    if (core.target.NormalTargetExists)
                    {
                        Orbit reference;
                        if (core.target.TargetOrbit.referenceBody == orbit.referenceBody)
                            reference = orbit; // we orbit arround the same body
                        else
                            reference = orbit.referenceBody.orbit;
                        // From Kerbal Alarm Clock
                        double angleChangePerSec = 360 / core.target.TargetOrbit.period - 360 / reference.period;
                        double currentAngle = reference.PhaseAngle(core.target.TargetOrbit, vesselState.time);
                        double angleDigff = currentAngle - phaseAngle;
                        if (angleDigff > 0 && angleChangePerSec > 0)
                            angleDigff -= 360;
                        if (angleDigff < 0 && angleChangePerSec < 0)
                            angleDigff += 360;
                        double TimeToTarget = Math.Floor(Math.Abs(angleDigff / angleChangePerSec));
                        targetUT = vesselState.time + TimeToTarget;
                    }

                    break;

                case WarpTarget.AtmosphericEntry:
                    try
                    {
                        targetUT = vessel.orbit.NextTimeOfRadius(vesselState.time,
                            vesselState.mainBody.Radius + vesselState.mainBody.RealMaxAtmosphereAltitude());
                    }
                    catch
                    {
                        warping = false;
                    }

                    break;

                case WarpTarget.SuicideBurn:
                    try
                    {
                        targetUT = OrbitExtensions.SuicideBurnCountdown(orbit, vesselState, vessel) + vesselState.time;
                    }
                    catch
                    {
                        warping = false;
                    }

                    break;

                default:
                    targetUT = vesselState.time;
                    break;
            }
        }

        public override void endAction()
        {
            base.endAction();
        }

        public override void WindowGUI(int windowID)
        {
            preWindowGUI(windowID);
            base.WindowGUI(windowID);

            GUILayout.Label("Warp to: ", GUILayout.ExpandWidth(false));
            warpTarget = (WarpTarget)GuiUtils.ComboBox.Box((int)warpTarget, warpTargetStrings, this);

            if (warpTarget == WarpTarget.Time)
            {
                GUILayout.Label("Warp for: ", GUILayout.ExpandWidth(true));
                timeOffset.text = GUILayout.TextField(timeOffset.text, GUILayout.Width(100));
            }
            else if (warpTarget == WarpTarget.PhaseAngleT)
            {
                // I wonder if I should check for target that don't make sense
                if (!core.target.NormalTargetExists)
                    GUILayout.Label("You need a target");
                else
                    GuiUtils.SimpleTextBox("Phase Angle:", phaseAngle, "º", 60);
            }

            if (!warping)
            {
                GuiUtils.SimpleTextBox("Lead time: ", leadTime, "");
            }

            if (warping)
            {
                if (GUILayout.Button("Abort"))
                {
                    onAbord();
                }
            }

            if (warping)
                GUILayout.Label("Warping to " + (leadTime > 0 ? GuiUtils.TimeToDHMS(leadTime) + " before " : "") +
                                warpTargetStrings[(int)warpTarget] + ".");

            if (isStarted() && !isExecuted() && startTime > 0)
            {
                GUILayout.Label(" waiting " + spendTime + "s");
            }

            postWindowGUI(windowID);
        }

        public override void afterOnFixedUpdate()
        {
            //Check the end of the action
            if (isStarted() && !isExecuted() && !warping && startTime == 0f)
            {
                startTime = Time.time;
            }

            if (isStarted() && !isExecuted() && startTime > 0)
            {
                spendTime = initTime - (int)Math.Round(Time.time - startTime); //Add the end action timer
                if (spendTime <= 0)
                {
                    endAction();
                }
            }

            if (!warping) return;

            if (warpTarget == WarpTarget.SuicideBurn)
            {
                try
                {
                    targetUT = OrbitExtensions.SuicideBurnCountdown(scriptModule.orbit, scriptModule.vesselState, scriptModule.vessel) +
                               scriptModule.vesselState.time;
                }
                catch
                {
                    warping = false;
                }
            }

            double target = targetUT - leadTime;

            if (target < scriptModule.vesselState.time + 1)
            {
                core.warp.MinimumWarp(true);
                warping = false;
            }
            else
            {
                core.warp.WarpToUT(target);
            }
        }

        public override void onAbord()
        {
            warping = false;
            core.warp.MinimumWarp(true);
            base.onAbord();
        }
    }
}
