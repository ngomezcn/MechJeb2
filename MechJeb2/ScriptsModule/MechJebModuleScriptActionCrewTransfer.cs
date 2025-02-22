﻿using System.Collections.Generic;
using UnityEngine;

namespace MuMech
{
    public class MechJebModuleScriptActionCrewTransfer : MechJebModuleScriptAction
    {
        public static    string       NAME                = "CrewTransfer";
        private readonly List<Part>   crewableParts       = new List<Part>();
        private readonly List<string> crewablePartsNamesS = new List<string>();

        [Persistent(pass = (int)Pass.Type)]
        private EditableInt selectedPartIndexS = 0;

        [Persistent(pass = (int)Pass.Type)]
        private EditableInt selectedPartIndexT = 0;

        private readonly List<string> crewablePartsNamesT = new List<string>();

        [Persistent(pass = (int)Pass.Type)]
        private EditableInt selectedKerbal = 0;

        [Persistent(pass = (int)Pass.Type)]
        private uint selectedPartSFlightID;

        [Persistent(pass = (int)Pass.Type)]
        private uint selectedPartTFlightID;

        [Persistent(pass = (int)Pass.Type)]
        private string selectedKerbalName;

        private readonly List<ProtoCrewMember> kerbalsList  = new List<ProtoCrewMember>();
        private readonly List<string>          kerbalsNames = new List<string>();
        private          bool                  partHighlightedS;
        private          bool                  partHighlightedT;

        public MechJebModuleScriptActionCrewTransfer(MechJebModuleScript scriptModule, MechJebCore core, MechJebModuleScriptActionsList actionsList) :
            base(scriptModule, core, actionsList, NAME)
        {
            crewableParts.Clear();
            crewablePartsNamesS.Clear();
            crewablePartsNamesT.Clear();
            kerbalsNames.Clear();
            kerbalsList.Clear();
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel.state != Vessel.State.DEAD)
                {
                    foreach (Part part in vessel.Parts)
                    {
                        if (part.CrewCapacity > 0)
                        {
                            crewableParts.Add(part);
                            crewablePartsNamesS.Add(part.partInfo.title);
                            crewablePartsNamesT.Add(part.partInfo.title);
                        }
                    }

                    foreach (ProtoCrewMember kerbal in vessel.GetVesselCrew())
                    {
                        kerbalsNames.Add(kerbal.name);
                        kerbalsList.Add(kerbal);
                    }
                }
            }
        }

        private void MoveKerbal(Part source, Part target, ProtoCrewMember kerbal)
        {
            RemoveCrew(kerbal, source, false);

            AddCrew(target, kerbal, false);

            // RemoveCrew works fine alone and AddCrew works fine alone, but if you combine them, it seems you must give KSP a moment to sort it all out,
            // so delay the remaining steps of the transfer process.
            //ManifestBehaviour.BeginDelayedCrewTransfer(source, target, kerbal);
            //CrewTransfer crewTransfer = new CrewTransfer(source, target, kerbal);
        }

        private void AddCrew(Part part, ProtoCrewMember kerbal, bool fireVesselUpdate)
        {
            part.AddCrewmember(kerbal);

            kerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
            if (kerbal.seat != null)
                kerbal.seat.SpawnCrew();

            GameEvents.onVesselChange.Fire(FlightGlobals.ActiveVessel);
        }

        private void RemoveCrew(ProtoCrewMember member, Part part, bool fireVesselUpdate)
        {
            part.RemoveCrewmember(member);
            member.seat         = null;
            member.rosterStatus = ProtoCrewMember.RosterStatus.Available;

            GameEvents.onVesselChange.Fire(FlightGlobals.ActiveVessel);
        }

        public override void activateAction()
        {
            base.activateAction();
            if (crewableParts[selectedPartIndexT].protoModuleCrew.Count < crewableParts[selectedPartIndexT].CrewCapacity)
            {
                MoveKerbal(crewableParts[selectedPartIndexS], crewableParts[selectedPartIndexT], kerbalsList[selectedKerbal]);
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
            GUILayout.Label("Tra.");
            selectedKerbal = GuiUtils.ComboBox.Box(selectedKerbal, kerbalsNames.ToArray(), kerbalsNames);
            GUILayout.Label("Fr.");
            selectedPartIndexS = GuiUtils.ComboBox.Box(selectedPartIndexS, crewablePartsNamesS.ToArray(), crewablePartsNamesS);
            if (!partHighlightedS)
            {
                if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view", true), GUILayout.ExpandWidth(false)))
                {
                    partHighlightedS = true;
                    crewableParts[selectedPartIndexS].SetHighlight(true, false);
                }
            }
            else
            {
                if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view_a", true), GUILayout.ExpandWidth(false)))
                {
                    partHighlightedS = false;
                    crewableParts[selectedPartIndexS].SetHighlight(false, false);
                }
            }

            GUILayout.Label("To");
            selectedPartIndexT = GuiUtils.ComboBox.Box(selectedPartIndexT, crewablePartsNamesT.ToArray(), crewablePartsNamesT);
            if (!partHighlightedT)
            {
                if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view", true), GUILayout.ExpandWidth(false)))
                {
                    partHighlightedT = true;
                    crewableParts[selectedPartIndexT].SetHighlight(true, false);
                }
            }
            else
            {
                if (GUILayout.Button(GameDatabase.Instance.GetTexture("MechJeb2/Icons/view_a", true), GUILayout.ExpandWidth(false)))
                {
                    partHighlightedT = false;
                    crewableParts[selectedPartIndexT].SetHighlight(false, false);
                }
            }

            if (selectedPartIndexS < crewableParts.Count)
            {
                selectedPartSFlightID = crewableParts[selectedPartIndexS].flightID;
            }

            if (selectedPartIndexT < crewableParts.Count)
            {
                selectedPartTFlightID = crewableParts[selectedPartIndexT].flightID;
            }

            if (selectedKerbal < kerbalsList.Count)
            {
                selectedKerbalName = kerbalsList[selectedKerbal].name;
            }

            postWindowGUI(windowID);
        }

        public override void postLoad(ConfigNode node)
        {
            if (selectedPartSFlightID != 0 && selectedPartTFlightID != 0 &&
                selectedKerbalName !=
                null) //We check if a previous flightID was set on the parts. When switching MechJeb Cores and performing save/load of the script, the port order may change so we try to rely on the flight ID to select the right part.
            {
                int i = 0;
                foreach (Part part in crewableParts)
                {
                    if (part.flightID == selectedPartSFlightID)
                    {
                        selectedPartIndexS = i;
                    }

                    if (part.flightID == selectedPartTFlightID)
                    {
                        selectedPartIndexT = i;
                    }

                    i++;
                }

                i = 0;
                foreach (string kerbalName in kerbalsNames)
                {
                    if (kerbalName.CompareTo(selectedKerbalName) == 0)
                    {
                        selectedKerbal = i;
                    }

                    i++;
                }
            }
        }
    }
}
