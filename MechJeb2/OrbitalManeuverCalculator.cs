﻿using System;
using System.Diagnostics;
using MechJebLib.Core;
using MechJebLib.Core.TwoBody;
using MechJebLib.Primitives;
using MuMech.MechJebLib.Maneuvers;
using Smooth.Pools;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace MuMech
{
    public static class OrbitalManeuverCalculator
    {
        //Computes the speed of a circular orbit of a given radius for a given body.
        public static double CircularOrbitSpeed(CelestialBody body, double radius)
        {
            //v = sqrt(GM/r)
            return Math.Sqrt(body.gravParameter / radius);
        }

        //Computes the deltaV of the burn needed to circularize an orbit at a given UT.
        public static Vector3d DeltaVToCircularize(Orbit o, double UT)
        {
            Vector3d desiredVelocity = CircularOrbitSpeed(o.referenceBody, o.Radius(UT)) * o.Horizontal(UT);
            Vector3d actualVelocity = o.WorldOrbitalVelocityAtUT(UT);
            return desiredVelocity - actualVelocity;
        }

        //Computes the deltaV of the burn needed to set a given PeR and ApR at at a given UT.
        public static Vector3d DeltaVToEllipticize(Orbit o, double UT, double newPeR, double newApR)
        {
            double radius = o.Radius(UT);

            //sanitize inputs
            newPeR = MuUtils.Clamp(newPeR, 0 + 1, radius - 1);
            newApR = Math.Max(newApR, radius + 1);

            double GM = o.referenceBody.gravParameter;
            double E = -GM / (newPeR + newApR); //total energy per unit mass of new orbit
            double L = Math.Sqrt(Math.Abs((Math.Pow(E * (newApR - newPeR), 2) - GM * GM) / (2 * E))); //angular momentum per unit mass of new orbit
            double kineticE = E + GM / radius; //kinetic energy (per unit mass) of new orbit at UT
            double horizontalV = L / radius; //horizontal velocity of new orbit at UT
            double verticalV = Math.Sqrt(Math.Abs(2 * kineticE - horizontalV * horizontalV)); //vertical velocity of new orbit at UT

            Vector3d actualVelocity = o.WorldOrbitalVelocityAtUT(UT);

            //untested:
            verticalV *= Math.Sign(Vector3d.Dot(o.Up(UT), actualVelocity));

            Vector3d desiredVelocity = horizontalV * o.Horizontal(UT) + verticalV * o.Up(UT);
            return desiredVelocity - actualVelocity;
        }

        //Computes the delta-V of the burn required to attain a given periapsis, starting from
        //a given orbit and burning at a given UT.
        public static Vector3d DeltaVToChangePeriapsis(Orbit o, double ut, double newPeR)
        {
            double radius = o.Radius(ut);

            //sanitize input
            newPeR = MuUtils.Clamp(newPeR, 0 + 1, radius - 1);

            (V3 r, V3 v) = o.RightHandedStateVectorsAtUT(ut);

            V3 dv = ChangeOrbitalElement.DeltaV(o.referenceBody.gravParameter, r, v, newPeR, ChangeOrbitalElement.Type.PERIAPSIS);

            return dv.V3ToWorld();
        }

        //Computes the delta-V of the burn at a given UT required to change an orbits apoapsis to a given value.
        //Note that you can pass in a negative apoapsis if the desired final orbit is hyperbolic
        public static Vector3d DeltaVToChangeApoapsis(Orbit o, double ut, double newApR)
        {
            double radius = o.Radius(ut);

            //sanitize input
            if (newApR > 0) newApR = Math.Max(newApR, radius + 1);

            (V3 r, V3 v) = o.RightHandedStateVectorsAtUT(ut);

            V3 dv = ChangeOrbitalElement.DeltaV(o.referenceBody.gravParameter, r, v, newApR, ChangeOrbitalElement.Type.APOAPSIS);

            return dv.V3ToWorld();
        }

        public static Vector3d DeltaVToChangeEccentricity(Orbit o, double ut, double newEcc)
        {
            //sanitize input
            if (newEcc < 0) newEcc = 0;

            (V3 r, V3 v) = o.RightHandedStateVectorsAtUT(ut);

            V3 dv = ChangeOrbitalElement.DeltaV(o.referenceBody.gravParameter, r, v, newEcc, ChangeOrbitalElement.Type.ECC);

            return dv.V3ToWorld();
        }

        //Computes the heading of the ground track of an orbit with a given inclination at a given latitude.
        //Both inputs are in degrees.
        //Convention: At equator, inclination    0 => heading 90 (east)
        //                        inclination   90 => heading 0  (north)
        //                        inclination  -90 => heading 180 (south)
        //                        inclination ±180 => heading 270 (west)
        //Returned heading is in degrees and in the range 0 to 360.
        //If the given latitude is too large, so that an orbit with a given inclination never attains the
        //given latitude, then this function returns either 90 (if -90 < inclination < 90) or 270.
        public static double HeadingForInclination(double inclinationDegrees, double latitudeDegrees)
        {
            double cosDesiredSurfaceAngle = Math.Cos(inclinationDegrees * UtilMath.Deg2Rad) / Math.Cos(latitudeDegrees * UtilMath.Deg2Rad);
            if (Math.Abs(cosDesiredSurfaceAngle) > 1.0)
            {
                //If inclination < latitude, we get this case: the desired inclination is impossible
                if (Math.Abs(MuUtils.ClampDegrees180(inclinationDegrees)) < 90) return 90;
                return 270;
            }

            double angleFromEast = UtilMath.Rad2Deg * Math.Acos(cosDesiredSurfaceAngle); //an angle between 0 and 180
            if (inclinationDegrees < 0) angleFromEast *= -1;
            //now angleFromEast is between -180 and 180

            return MuUtils.ClampDegrees360(90 - angleFromEast);
        }

        //See #676
        //Computes the heading for a ground launch at the specified latitude accounting for the body rotation.
        //Both inputs are in degrees.
        //Convention: At equator, inclination    0 => heading 90 (east)
        //                        inclination   90 => heading 0  (north)
        //                        inclination  -90 => heading 180 (south)
        //                        inclination ±180 => heading 270 (west)
        //Returned heading is in degrees and in the range 0 to 360.
        //If the given latitude is too large, so that an orbit with a given inclination never attains the
        //given latitude, then this function returns either 90 (if -90 < inclination < 90) or 270.
        public static double HeadingForLaunchInclination(Vessel vessel, VesselState vesselState, double inclinationDegrees)
        {
            CelestialBody body = vessel.mainBody;
            double latitudeDegrees = vesselState.latitude;
            double orbVel = CircularOrbitSpeed(body, vesselState.altitudeASL + body.Radius);
            double headingOne = HeadingForInclination(inclinationDegrees, latitudeDegrees) * UtilMath.Deg2Rad;
            double headingTwo = HeadingForInclination(-inclinationDegrees, latitudeDegrees) * UtilMath.Deg2Rad;
            double now = Planetarium.GetUniversalTime();
            Orbit o = vessel.orbit;

            Vector3d north = vesselState.north;
            Vector3d east = vesselState.east;

            var actualHorizontalVelocity = Vector3d.Exclude(o.Up(now), o.WorldOrbitalVelocityAtUT(now));
            Vector3d desiredHorizontalVelocityOne = orbVel * (Math.Sin(headingOne) * east + Math.Cos(headingOne) * north);
            Vector3d desiredHorizontalVelocityTwo = orbVel * (Math.Sin(headingTwo) * east + Math.Cos(headingTwo) * north);

            Vector3d deltaHorizontalVelocityOne = desiredHorizontalVelocityOne - actualHorizontalVelocity;
            Vector3d deltaHorizontalVelocityTwo = desiredHorizontalVelocityTwo - actualHorizontalVelocity;

            Vector3d desiredHorizontalVelocity;
            Vector3d deltaHorizontalVelocity;

            if (vesselState.speedSurfaceHorizontal < 200)
            {
                // at initial launch we have to head the direction the user specifies (90 north instead of -90 south).
                // 200 m/s of surface velocity also defines a 'grace period' where someone can catch a rocket that they meant
                // to launch at -90 and typed 90 into the inclination box fast after it started to initiate the turn.
                // if the rocket gets outside of the 200 m/s surface velocity envelope, then there is no way to tell MJ to
                // take a south travelling rocket and turn north or vice versa.
                desiredHorizontalVelocity = desiredHorizontalVelocityOne;
                deltaHorizontalVelocity   = deltaHorizontalVelocityOne;
            }
            else
            {
                // now in order to get great circle tracks correct we pick the side which gives the lowest delta-V, which will get
                // ground tracks that cross the maximum (or minimum) latitude of a great circle correct.
                if (deltaHorizontalVelocityOne.magnitude < deltaHorizontalVelocityTwo.magnitude)
                {
                    desiredHorizontalVelocity = desiredHorizontalVelocityOne;
                    deltaHorizontalVelocity   = deltaHorizontalVelocityOne;
                }
                else
                {
                    desiredHorizontalVelocity = desiredHorizontalVelocityTwo;
                    deltaHorizontalVelocity   = deltaHorizontalVelocityTwo;
                }
            }

            // if you circularize in one burn, towards the end deltaHorizontalVelocity will whip around, but we want to
            // fall back to tracking desiredHorizontalVelocity
            if (Vector3d.Dot(desiredHorizontalVelocity.normalized, deltaHorizontalVelocity.normalized) < 0.90)
            {
                // it is important that we do NOT do the fracReserveDV math here, we want to ignore the deltaHV entirely at ths point
                return MuUtils.ClampDegrees360(UtilMath.Rad2Deg *
                                               Math.Atan2(Vector3d.Dot(desiredHorizontalVelocity, east),
                                                   Vector3d.Dot(desiredHorizontalVelocity, north)));
            }

            return MuUtils.ClampDegrees360(UtilMath.Rad2Deg *
                                           Math.Atan2(Vector3d.Dot(deltaHorizontalVelocity, east), Vector3d.Dot(deltaHorizontalVelocity, north)));
        }

        //Computes the delta-V of the burn required to change an orbit's inclination to a given value
        //at a given UT. If the latitude at that time is too high, so that the desired inclination
        //cannot be attained, the burn returned will achieve as low an inclination as possible (namely, inclination = latitude).
        //The input inclination is in degrees.
        //Note that there are two orbits through each point with a given inclination. The convention used is:
        //   - first, clamp newInclination to the range -180, 180
        //   - if newInclination > 0, do the cheaper burn to set that inclination
        //   - if newInclination < 0, do the more expensive burn to set that inclination
        public static Vector3d DeltaVToChangeInclination(Orbit o, double UT, double newInclination)
        {
            double latitude = o.referenceBody.GetLatitude(o.WorldPositionAtUT(UT));
            double desiredHeading = HeadingForInclination(newInclination, latitude);
            var actualHorizontalVelocity = Vector3d.Exclude(o.Up(UT), o.WorldOrbitalVelocityAtUT(UT));
            Vector3d eastComponent = actualHorizontalVelocity.magnitude * Math.Sin(UtilMath.Deg2Rad * desiredHeading) * o.East(UT);
            Vector3d northComponent = actualHorizontalVelocity.magnitude * Math.Cos(UtilMath.Deg2Rad * desiredHeading) * o.North(UT);
            if (Vector3d.Dot(actualHorizontalVelocity, northComponent) < 0) northComponent *= -1;
            if (MuUtils.ClampDegrees180(newInclination) < 0) northComponent                *= -1;
            Vector3d desiredHorizontalVelocity = eastComponent + northComponent;
            return desiredHorizontalVelocity - actualHorizontalVelocity;
        }

        //Computes the delta-V and time of a burn to match planes with the target orbit. The output burnUT
        //will be equal to the time of the first ascending node with respect to the target after the given UT.
        //Throws an ArgumentException if o is hyperbolic and doesn't have an ascending node relative to the target.
        public static Vector3d DeltaVAndTimeToMatchPlanesAscending(Orbit o, Orbit target, double UT, out double burnUT)
        {
            burnUT = o.TimeOfAscendingNode(target, UT);
            var desiredHorizontal = Vector3d.Cross(target.OrbitNormal(), o.Up(burnUT));
            var actualHorizontalVelocity = Vector3d.Exclude(o.Up(burnUT), o.WorldOrbitalVelocityAtUT(burnUT));
            Vector3d desiredHorizontalVelocity = actualHorizontalVelocity.magnitude * desiredHorizontal;
            return desiredHorizontalVelocity - actualHorizontalVelocity;
        }

        //Computes the delta-V and time of a burn to match planes with the target orbit. The output burnUT
        //will be equal to the time of the first descending node with respect to the target after the given UT.
        //Throws an ArgumentException if o is hyperbolic and doesn't have a descending node relative to the target.
        public static Vector3d DeltaVAndTimeToMatchPlanesDescending(Orbit o, Orbit target, double UT, out double burnUT)
        {
            burnUT = o.TimeOfDescendingNode(target, UT);
            var desiredHorizontal = Vector3d.Cross(target.OrbitNormal(), o.Up(burnUT));
            var actualHorizontalVelocity = Vector3d.Exclude(o.Up(burnUT), o.WorldOrbitalVelocityAtUT(burnUT));
            Vector3d desiredHorizontalVelocity = actualHorizontalVelocity.magnitude * desiredHorizontal;
            return desiredHorizontalVelocity - actualHorizontalVelocity;
        }

        //Computes the dV of a Hohmann transfer burn at time UT that will put the apoapsis or periapsis
        //of the transfer orbit on top of the target orbit.
        //The output value apsisPhaseAngle is the phase angle between the transferring vessel and the
        //target object as the transferring vessel crosses the target orbit at the apoapsis or periapsis
        //of the transfer orbit.
        //Actually, it's not exactly the phase angle. It's a sort of mean anomaly phase angle. The
        //difference is not important for how this function is used by DeltaVAndTimeForHohmannTransfer.
        private static Vector3d DeltaVAndApsisPhaseAngleOfHohmannTransfer(Orbit o, Orbit target, double UT, out double apsisPhaseAngle)
        {
            Vector3d apsisDirection = -o.WorldBCIPositionAtUT(UT);
            double desiredApsis = target.RadiusAtTrueAnomaly(UtilMath.Deg2Rad * target.TrueAnomalyFromVector(apsisDirection));

            Vector3d dV;
            if (desiredApsis > o.ApR)
            {
                dV = DeltaVToChangeApoapsis(o, UT, desiredApsis);
                Orbit transferOrbit = o.PerturbedOrbit(UT, dV);
                double transferApTime = transferOrbit.NextApoapsisTime(UT);
                Vector3d transferApDirection =
                    transferOrbit.WorldBCIPositionAtApoapsis(); // getRelativePositionAtUT was returning NaNs! :(((((
                double targetTrueAnomaly = target.TrueAnomalyFromVector(transferApDirection);
                double meanAnomalyOffset = 360 * (target.TimeOfTrueAnomaly(targetTrueAnomaly, UT) - transferApTime) / target.period;
                apsisPhaseAngle = meanAnomalyOffset;
            }
            else
            {
                dV = DeltaVToChangePeriapsis(o, UT, desiredApsis);
                Orbit transferOrbit = o.PerturbedOrbit(UT, dV);
                double transferPeTime = transferOrbit.NextPeriapsisTime(UT);
                Vector3d transferPeDirection =
                    transferOrbit.WorldBCIPositionAtPeriapsis(); // getRelativePositionAtUT was returning NaNs! :(((((
                double targetTrueAnomaly = target.TrueAnomalyFromVector(transferPeDirection);
                double meanAnomalyOffset = 360 * (target.TimeOfTrueAnomaly(targetTrueAnomaly, UT) - transferPeTime) / target.period;
                apsisPhaseAngle = meanAnomalyOffset;
            }

            apsisPhaseAngle = MuUtils.ClampDegrees180(apsisPhaseAngle);

            return dV;
        }

        //Computes the time and dV of a Hohmann transfer injection burn such that at apoapsis the transfer
        //orbit passes as close as possible to the target.
        //The output burnUT will be the first transfer window found after the given UT.
        //Assumes o and target are in approximately the same plane, and orbiting in the same direction.
        //Also assumes that o is a perfectly circular orbit (though result should be OK for small eccentricity).
        public static Vector3d DeltaVAndTimeForHohmannTransfer(Orbit o, Orbit target, double UT, out double burnUT)
        {
            //We do a binary search for the burn time that zeros out the phase angle between the
            //transferring vessel and the target at the apsis of the transfer orbit.
            double synodicPeriod = o.SynodicPeriod(target);

            double lastApsisPhaseAngle;
            Vector3d immediateBurnDV = DeltaVAndApsisPhaseAngleOfHohmannTransfer(o, target, UT, out lastApsisPhaseAngle);

            double minTime = UT;
            double maxTime = UT + 1.5 * synodicPeriod;

            //first find roughly where the zero point is
            const int numDivisions = 30;
            double dt = (maxTime - minTime) / numDivisions;
            for (int i = 1; i <= numDivisions; i++)
            {
                double t = minTime + dt * i;

                double apsisPhaseAngle;
                DeltaVAndApsisPhaseAngleOfHohmannTransfer(o, target, t, out apsisPhaseAngle);

                if (Math.Abs(apsisPhaseAngle) < 90 && Math.Sign(lastApsisPhaseAngle) != Math.Sign(apsisPhaseAngle))
                {
                    minTime = t - dt;
                    maxTime = t;
                    break;
                }

                if (i == 1 && Math.Abs(lastApsisPhaseAngle) < 0.5 && Math.Sign(lastApsisPhaseAngle) == Math.Sign(apsisPhaseAngle))
                {
                    //In this case we are JUST passed the center of the transfer window, but probably we
                    //can still do the transfer just fine. Don't do a search, just return an immediate burn
                    burnUT = UT;
                    return immediateBurnDV;
                }

                lastApsisPhaseAngle = apsisPhaseAngle;

                if (i == numDivisions)
                {
                    throw new ArgumentException("OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer: couldn't find the transfer window!!");
                }
            }

            burnUT = 0;
            Func<double, object, double> f = delegate(double testTime, object ign)
            {
                double testApsisPhaseAngle;
                DeltaVAndApsisPhaseAngleOfHohmannTransfer(o, target, testTime, out testApsisPhaseAngle);
                return testApsisPhaseAngle;
            };
            try { burnUT = BrentRoot.Solve(f, maxTime, minTime, null); }
            catch (TimeoutException) { Debug.Log("[MechJeb] Brents method threw a timeout error (supressed)"); }

            Vector3d burnDV = DeltaVAndApsisPhaseAngleOfHohmannTransfer(o, target, burnUT, out _);

            return burnDV;
        }

        // Computes the delta-V of a burn at a given time that will put an object with a given orbit on a
        // course to intercept a target at a specific interceptUT.
        //
        // offsetDistance: this is used by the Rendezvous Autopilot and is only going to be valid over very short distances
        // shortway: the shortway parameter to feed into the Lambert solver
        //
        public static (Vector3d v1, Vector3d v2) DeltaVToInterceptAtTime(Orbit o, double t0, Orbit target, double dt,
            double offsetDistance = 0, bool shortway = true)
        {
            (V3 ri, V3 vi) = o.RightHandedStateVectorsAtUT(t0);
            (V3 rf, V3 vf) = target.RightHandedStateVectorsAtUT(t0 + dt);

            (V3 transferVi, V3 transferVf) =
                Gooding.Solve(o.referenceBody.gravParameter, ri, vi, rf, shortway ? dt : -dt, 0);

            if (offsetDistance != 0)
            {
                rf -= offsetDistance * V3.Cross(vf, rf).normalized;
                (transferVi, transferVf) = Gooding.Solve(o.referenceBody.gravParameter, ri, vi, rf,
                    shortway ? dt : -dt, 0);
            }

            return ((transferVi - vi).V3ToWorld(), (vf - transferVf).V3ToWorld());
        }

        // Lambert Solver Driver function.
        //
        // This uses Shepperd's method instead of using KSP's Orbit class.
        //
        // The reference time is usually 'now' or the first time the burn can start.
        //
        // GM       - grav parameter of the celestial
        // pos      - position of the source orbit at a reference time
        // vel      - velocity of the source orbit at a reference time
        // tpos     - position of the target orbit at a reference time
        // tvel     - velocity of the target orbit at a reference time
        // DT       - time of the burn in seconds after the reference time
        // TT       - transfer time of the burn in seconds after the burn time
        // secondDV - the second burn dV
        // returns  - the first burn dV
        //
        private static Vector3d DeltaVToInterceptAtTime(double GM, Vector3d pos, Vector3d vel, Vector3d tpos, Vector3d tvel, double dt, double tt,
            out Vector3d secondDV, bool posigrade = true)
        {
            // advance the source orbit to ref + DT
            (V3 pos1, V3 vel1) = Shepperd.Solve(GM, dt, pos.ToV3(), vel.ToV3());

            // advance the target orbit to ref + DT + TT
            (V3 pos2, V3 vel2) = Shepperd.Solve(GM, dt + tt, tpos.ToV3(), tvel.ToV3());

            (V3 transferVi, V3 transferVf) = Gooding.Solve(GM, pos1, vel1, pos2, posigrade ? tt : -tt, 0);

            secondDV = (vel2 - transferVf).ToVector3d();

            return (transferVi - vel1).ToVector3d();
        }

        // This does a line-search to find the burnUT for the cheapest course correction that will intercept exactly
        public static Vector3d DeltaVAndTimeForCheapestCourseCorrection(Orbit o, double UT, Orbit target, out double burnUT)
        {
            double closestApproachTime = o.NextClosestApproachTime(target, UT + 2); //+2 so that closestApproachTime is definitely > UT

            burnUT           = UT;
            (Vector3d dV, _) = DeltaVToInterceptAtTime(o, burnUT, target, closestApproachTime - burnUT);

            // FIXME: replace with BrentRoot's 1-d minimization algorithm
            const int fineness = 20;
            for (double step = 0.5; step < fineness; step += 1.0)
            {
                double testUT = UT + (closestApproachTime - UT) * step / fineness;
                (Vector3d testDV, _) = DeltaVToInterceptAtTime(o, testUT, target, closestApproachTime - testUT);

                if (testDV.magnitude < dV.magnitude)
                {
                    dV     = testDV;
                    burnUT = testUT;
                }
            }

            return dV;
        }

        // This is the entry point for the course-correction to a target orbit which is a celestial
        public static Vector3d DeltaVAndTimeForCheapestCourseCorrection(Orbit o, double UT, Orbit target, CelestialBody targetBody, double finalPeR,
            out double burnUT)
        {
            Vector3d collisionDV = DeltaVAndTimeForCheapestCourseCorrection(o, UT, target, out burnUT);
            Orbit collisionOrbit = o.PerturbedOrbit(burnUT, collisionDV);
            double collisionUT = collisionOrbit.NextClosestApproachTime(target, burnUT);
            Vector3d collisionPosition = target.WorldPositionAtUT(collisionUT);
            Vector3d collisionRelVel = collisionOrbit.WorldOrbitalVelocityAtUT(collisionUT) - target.WorldOrbitalVelocityAtUT(collisionUT);

            double soiEnterUT = collisionUT - targetBody.sphereOfInfluence / collisionRelVel.magnitude;
            Vector3d soiEnterRelVel = collisionOrbit.WorldOrbitalVelocityAtUT(soiEnterUT) - target.WorldOrbitalVelocityAtUT(soiEnterUT);

            double E = 0.5 * soiEnterRelVel.sqrMagnitude -
                       targetBody.gravParameter / targetBody.sphereOfInfluence; //total orbital energy on SoI enter
            double finalPeSpeed =
                Math.Sqrt(2 * (E + targetBody.gravParameter / finalPeR)); //conservation of energy gives the orbital speed at finalPeR.
            double desiredImpactParameter =
                finalPeR * finalPeSpeed / soiEnterRelVel.magnitude; //conservation of angular momentum gives the required impact parameter

            Vector3d displacementDir = Vector3d.Cross(collisionRelVel, o.OrbitNormal()).normalized;
            Vector3d interceptTarget = collisionPosition + desiredImpactParameter * displacementDir;

            (V3 velAfterBurn, _) = Gooding.Solve(o.referenceBody.gravParameter, o.WorldBCIPositionAtUT(burnUT).ToV3(),
                o.WorldOrbitalVelocityAtUT(burnUT).ToV3(), (interceptTarget - o.referenceBody.position).ToV3(), collisionUT - burnUT, 0);

            Vector3d deltaV = velAfterBurn.ToVector3d() - o.WorldOrbitalVelocityAtUT(burnUT);
            return deltaV;
        }

        // This is the entry point for the course-correction to a target orbit which is not a celestial
        public static Vector3d DeltaVAndTimeForCheapestCourseCorrection(Orbit o, double UT, Orbit target, double caDistance, out double burnUT)
        {
            Vector3d collisionDV = DeltaVAndTimeForCheapestCourseCorrection(o, UT, target, out burnUT);
            Orbit collisionOrbit = o.PerturbedOrbit(burnUT, collisionDV);
            double collisionUT = collisionOrbit.NextClosestApproachTime(target, burnUT);
            Vector3d targetPos = target.WorldPositionAtUT(collisionUT);

            Vector3d interceptTarget = targetPos + target.NormalPlus(collisionUT) * caDistance;

            (V3 velAfterBurn, _) = Gooding.Solve(o.referenceBody.gravParameter, o.WorldBCIPositionAtUT(burnUT).ToV3(),
                o.WorldOrbitalVelocityAtUT(burnUT).ToV3(),
                (interceptTarget - o.referenceBody.position).ToV3(), collisionUT - burnUT, 0);

            Vector3d deltaV = velAfterBurn.ToVector3d() - o.WorldOrbitalVelocityAtUT(burnUT);
            return deltaV;
        }

        //Computes the time and delta-V of an ejection burn to a Hohmann transfer from one planet to another.
        //It's assumed that the initial orbit around the first planet is circular, and that this orbit
        //is in the same plane as the orbit of the first planet around the sun. It's also assumed that
        //the target planet has a fairly low relative inclination with respect to the first planet. If the
        //inclination change is nonzero you should also do a mid-course correction burn, as computed by
        //DeltaVForCourseCorrection (a function that has been removed due to being unused).
        public static Vector3d DeltaVAndTimeForInterplanetaryTransferEjection(Orbit o, double UT, Orbit target, bool syncPhaseAngle,
            out double burnUT)
        {
            Orbit planetOrbit = o.referenceBody.orbit;

            //Compute the time and dV for a Hohmann transfer where we pretend that we are the planet we are orbiting.
            //This gives us the "ideal" deltaV and UT of the ejection burn, if we didn't have to worry about waiting for the right
            //ejection angle and if we didn't have to worry about the planet's gravity dragging us back and increasing the required dV.
            double idealBurnUT;
            Vector3d idealDeltaV;

            if (syncPhaseAngle)
            {
                //time the ejection burn to intercept the target
                idealDeltaV = DeltaVAndTimeForHohmannTransfer(planetOrbit, target, UT, out idealBurnUT);
            }
            else
            {
                //don't time the ejection burn to intercept the target; we just care about the final peri/apoapsis
                idealBurnUT = UT;
                if (target.semiMajorAxis < planetOrbit.semiMajorAxis)
                    idealDeltaV  = DeltaVToChangePeriapsis(planetOrbit, idealBurnUT, target.semiMajorAxis);
                else idealDeltaV = DeltaVToChangeApoapsis(planetOrbit, idealBurnUT, target.semiMajorAxis);
            }

            //Compute the actual transfer orbit this ideal burn would lead to.
            Orbit transferOrbit = planetOrbit.PerturbedOrbit(idealBurnUT, idealDeltaV);

            //Now figure out how to approximately eject from our current orbit into the Hohmann orbit we just computed.

            //Assume we want to exit the SOI with the same velocity as the ideal transfer orbit at idealUT -- i.e., immediately
            //after the "ideal" burn we used to compute the transfer orbit. This isn't quite right.
            //We intend to eject from our planet at idealUT and only several hours later will we exit the SOI. Meanwhile
            //the transfer orbit will have acquired a slightly different velocity, which we should correct for. Maybe
            //just add in (1/2)(sun gravity)*(time to exit soi)^2 ? But how to compute time to exit soi? Or maybe once we
            //have the ejection orbit we should just move the ejection burn back by the time to exit the soi?
            Vector3d soiExitVelocity = idealDeltaV;
            //project the desired exit direction into the current orbit plane to get the feasible exit direction
            Vector3d inPlaneSoiExitDirection = Vector3d.Exclude(o.OrbitNormal(), soiExitVelocity).normalized;

            //compute the angle by which the trajectory turns between periapsis (where we do the ejection burn)
            //and SOI exit (approximated as radius = infinity)
            double soiExitEnergy = 0.5 * soiExitVelocity.sqrMagnitude - o.referenceBody.gravParameter / o.referenceBody.sphereOfInfluence;
            double ejectionRadius = o.semiMajorAxis; //a guess, good for nearly circular orbits

            double ejectionKineticEnergy = soiExitEnergy + o.referenceBody.gravParameter / ejectionRadius;
            double ejectionSpeed = Math.Sqrt(2 * ejectionKineticEnergy);

            //construct a sample ejection orbit
            Vector3d ejectionOrbitInitialVelocity = ejectionSpeed * (Vector3d)o.referenceBody.transform.right;
            Vector3d ejectionOrbitInitialPosition = o.referenceBody.position + ejectionRadius * (Vector3d)o.referenceBody.transform.up;
            Orbit sampleEjectionOrbit = MuUtils.OrbitFromStateVectors(ejectionOrbitInitialPosition, ejectionOrbitInitialVelocity, o.referenceBody, 0);
            double ejectionOrbitDuration = sampleEjectionOrbit.NextTimeOfRadius(0, o.referenceBody.sphereOfInfluence);
            Vector3d ejectionOrbitFinalVelocity = sampleEjectionOrbit.WorldOrbitalVelocityAtUT(ejectionOrbitDuration);

            double turningAngle = Math.Abs(Vector3d.Angle(ejectionOrbitInitialVelocity, ejectionOrbitFinalVelocity));

            //rotate the exit direction by 90 + the turning angle to get a vector pointing to the spot in our orbit
            //where we should do the ejection burn. Then convert this to a true anomaly and compute the time closest
            //to planetUT at which we will pass through that true anomaly.
            Vector3d ejectionPointDirection = Quaternion.AngleAxis(-(float)(90 + turningAngle), o.OrbitNormal()) * inPlaneSoiExitDirection;
            double ejectionTrueAnomaly = o.TrueAnomalyFromVector(ejectionPointDirection);
            burnUT = o.TimeOfTrueAnomaly(ejectionTrueAnomaly, idealBurnUT - o.period);

            if (idealBurnUT - burnUT > o.period / 2 || burnUT < UT)
            {
                burnUT += o.period;
            }

            //rotate the exit direction by the turning angle to get a vector pointing to the spot in our orbit
            //where we should do the ejection burn
            Vector3d ejectionBurnDirection = Quaternion.AngleAxis(-(float)turningAngle, o.OrbitNormal()) * inPlaneSoiExitDirection;
            Vector3d ejectionVelocity = ejectionSpeed * ejectionBurnDirection;

            Vector3d preEjectionVelocity = o.WorldOrbitalVelocityAtUT(burnUT);

            return ejectionVelocity - preEjectionVelocity;
        }

        public struct LambertProblem
        {
            public Vector3d pos,  vel;  // position + velocity of source orbit at reference time
            public Vector3d tpos, tvel; // position + velocity of target orbit at reference time
            public double   GM;
            public bool     shortway;
            public bool     intercept_only; // omit the second burn from the cost
        }

        // x[0] is the burn time before/after zeroUT
        // x[1] is the time of the transfer
        //
        // f[1] is the cost of the burn
        //
        // prob.shortway is which lambert solution to find
        // prob.intercept_only omits adding the second burn to the cost
        //
        public static void LambertCost(double[] x, double[] f, object obj)
        {
            var prob = (LambertProblem)obj;
            Vector3d secondBurn;

            try
            {
                f[0] = DeltaVToInterceptAtTime(prob.GM, prob.pos, prob.vel, prob.tpos, prob.tvel, x[0], x[1], out secondBurn, prob.shortway)
                    .magnitude;
                if (!prob.intercept_only)
                {
                    f[0] += secondBurn.magnitude;
                }
            }
            catch (Exception)
            {
                // need Sqrt of MaxValue so least-squares can square it without an infinity
                f[0] = Math.Sqrt(double.MaxValue);
            }

            if (!f[0].IsFinite())
                f[0] = Math.Sqrt(double.MaxValue);
        }

        // Levenburg-Marquardt local optimization of a two burn transfer.
        public static Vector3d DeltaVAndTimeForBiImpulsiveTransfer(double GM, Vector3d pos, Vector3d vel, Vector3d tpos, Vector3d tvel, double DT,
            double TT, out double burnDT, out double burnTT, out double burnCost, double minDT = double.NegativeInfinity,
            double maxDT = double.PositiveInfinity, double maxTT = double.PositiveInfinity, double maxDTplusT = double.PositiveInfinity,
            bool intercept_only = false, double eps = 1e-9, int maxIter = 100, bool shortway = false)
        {
            double[] x = { DT, TT };
            double[] scale = new double[2];

            if (maxDT != double.PositiveInfinity && maxTT != double.PositiveInfinity)
            {
                scale[0] = maxDT;
                scale[1] = maxTT;
            }
            else
            {
                scale[0] = DT;
                scale[1] = TT;
            }

            // absolute final time constraint: x[0] + x[1] <= maxUTplusT
            double[,] C = { { 1, 1, maxDTplusT } };
            int[] CT = { -1 };

            double[] bndl = { minDT, 0 };
            double[] bndu = { maxDT, maxTT };

            alglib.minlmstate state;
            var rep = new alglib.minlmreport();
            alglib.minlmcreatev(1, x, 0.000001, out state);
            alglib.minlmsetscale(state, scale);
            alglib.minlmsetbc(state, bndl, bndu);
            if (maxDTplusT != double.PositiveInfinity)
                alglib.minlmsetlc(state, C, CT);
            alglib.minlmsetcond(state, eps, maxIter);

            var prob = new LambertProblem();

            prob.pos            = pos;
            prob.vel            = vel;
            prob.tpos           = tpos;
            prob.tvel           = tvel;
            prob.GM             = GM;
            prob.shortway       = shortway;
            prob.intercept_only = intercept_only;

            alglib.minlmoptimize(state, LambertCost, null, prob);
            alglib.minlmresultsbuf(state, ref x, rep);
            Debug.Log("iter = " + rep.iterationscount);
            if (rep.terminationtype < 0)
            {
                // FIXME: we should not accept this result
                Debug.Log("MechJeb Lambert Transfer minlmoptimize termination code: " + rep.terminationtype);
            }

            //Debug.Log("DeltaVAndTimeForBiImpulsiveTransfer: x[0] = " + x[0] + " x[1] = " + x[1]);

            double[] fout = new double[1];
            LambertCost(x, fout, prob);
            burnCost = fout[0];

            burnDT = x[0];
            burnTT = x[1];

            Vector3d secondBurn; // ignored
            return DeltaVToInterceptAtTime(prob.GM, prob.pos, prob.vel, prob.tpos, prob.tvel, x[0], x[1], out secondBurn, prob.shortway);
        }

        public static double acceptanceProbabilityForBiImpulsive(double currentCost, double newCost, double temp)
        {
            if (newCost < currentCost)
                return 1.0;
            return Math.Exp((currentCost - newCost) / temp);
        }

        // Basin-Hopping algorithm global search for a two burn transfer (Note this says "Annealing" but it was converted to Basin-Hopping)
        //
        // FIXME: there's some very confusing nomenclature between DeltaVAndTimeForBiImpulsiveTransfer and this
        //        the minUT/maxUT values here are zero-centered on this methods UT.  the minUT/maxUT parameters to
        //        the other method are proper UT times and not zero centered at all.
        public static Vector3d DeltaVAndTimeForBiImpulsiveAnnealed(Orbit o, Orbit target, double UT, out double bestUT, double minDT = 0.0,
            double maxDT = double.PositiveInfinity, bool intercept_only = false, bool fixed_ut = false)
        {
            Debug.Log("origin = " + o.MuString());
            Debug.Log("target = " + target.MuString());

            Vector3d pos = o.WorldBCIPositionAtUT(UT);
            Vector3d vel = o.WorldOrbitalVelocityAtUT(UT);
            Vector3d tpos = target.WorldBCIPositionAtUT(UT);
            Vector3d tvel = target.WorldOrbitalVelocityAtUT(UT);
            double GM = o.referenceBody.gravParameter;

            double MAXTEMP = 10000;
            double temp = MAXTEMP;
            double coolingRate = 0.01;

            double bestTT = 0;
            double bestDT = 0;
            double bestCost = double.MaxValue;
            Vector3d bestBurnVec = Vector3d.zero;
            bool bestshortway = false;

            var random = new Random();

            double maxDTplusT = double.PositiveInfinity;

            // min transfer time must be > 0 (no teleportation)
            double minTT = 1e-15;

            // update the patched conic prediction for hyperbolic orbits (important *not* to do this for mutated planetary orbits, since we will
            // get encouters, but we need the patchEndTransition for hyperbolic orbits).
            if (target.eccentricity >= 1.0)
                target.CalculateNextOrbit();

            if (maxDT == double.PositiveInfinity)
                maxDT = 1.5 * o.SynodicPeriod(target);

            // figure the max transfer time of a Hohmann orbit using the SMAs of the two orbits instead of the radius (as a guess), multiplied by 2
            double a = (Math.Abs(o.semiMajorAxis) + Math.Abs(target.semiMajorAxis)) / 2;
            double maxTT = Math.PI * Math.Sqrt(a * a * a / o.referenceBody.gravParameter); // FIXME: allow tweaking

            Debug.Log("[MechJeb] DeltaVAndTimeForBiImpulsiveAnnealed Check1: minDT = " + minDT + " maxDT = " + maxDT + " maxTT = " + maxTT +
                      " maxDTplusT = " + maxDTplusT);
            Debug.Log("[MechJeb] DeltaVAndTimeForBiImpulsiveAnnealed target.patchEndTransition = " + target.patchEndTransition);

            if (target.patchEndTransition != Orbit.PatchTransitionType.FINAL && target.patchEndTransition != Orbit.PatchTransitionType.INITIAL)
            {
                // reset the guess to search for start times out to the end of the target orbit
                maxDT = target.EndUT - UT;
                // longest possible transfer time would leave now and arrive at the target patch end
                maxTT = Math.Min(maxTT, target.EndUT - UT);
                // constraint on DT + TT <= maxDTplusT to arrive before the target orbit ends
                maxDTplusT = Math.Min(maxDTplusT, target.EndUT - UT);
            }

            Debug.Log("[MechJeb] DeltaVAndTimeForBiImpulsiveAnnealed o.patchEndTransition = " + o.patchEndTransition);

            // if our orbit ends, search for start times all the way to the end, but don't violate maxDTplusT if its set
            if (o.patchEndTransition != Orbit.PatchTransitionType.FINAL && o.patchEndTransition != Orbit.PatchTransitionType.INITIAL)
            {
                maxDT = Math.Min(o.EndUT - UT, maxDTplusT);
            }

            // user requested a burn at a specific time
            if (fixed_ut)
            {
                maxDT = 0;
                minDT = 0;
            }

            Debug.Log("[MechJeb] DeltaVAndTimeForBiImpulsiveAnnealed Check2: minDT = " + minDT + " maxDT = " + maxDT + " maxTT = " + maxTT +
                      " maxDTplusT = " + maxDTplusT);

            double currentCost = double.MaxValue;
            double currentDT = maxDT / 2;
            double currentTT = maxTT / 2;

            var stopwatch = new Stopwatch();

            int n = 0;

            stopwatch.Start();
            while (temp > 1000)
            {
                double burnDT, burnTT, burnCost;

                // shrink the neighborhood based on temp
                double windowDT = temp / MAXTEMP * (maxDT - minDT);
                double windowTT = temp / MAXTEMP * (maxTT - minTT);
                double windowminDT = currentDT - windowDT;
                windowminDT = windowminDT < minDT ? minDT : windowminDT;
                double windowmaxDT = currentDT + windowDT;
                windowmaxDT = windowmaxDT > maxDT ? maxDT : windowmaxDT;
                double windowminTT = currentTT - windowTT;
                windowminTT = windowminTT < minTT ? minTT : windowminTT;
                double windowmaxTT = currentTT + windowTT;
                windowmaxTT = windowmaxTT > maxTT ? maxTT : windowmaxTT;

                // compute the neighbor
                double nextDT = random.NextDouble() * (windowmaxDT - windowminDT) + windowminDT;
                double nextTT = random.NextDouble() * (windowmaxTT - windowminTT) + windowminTT;
                nextTT = Math.Min(nextTT, maxDTplusT - nextDT);

                // just randomize the shortway
                bool nextshortway = random.NextDouble() > 0.5;

                //Debug.Log("nextDT = " + nextDT + " nextTT = " + nextTT);
                Vector3d burnVec = DeltaVAndTimeForBiImpulsiveTransfer(GM, pos, vel, tpos, tvel, nextDT, nextTT, out burnDT, out burnTT, out burnCost,
                    minDT, maxDT, maxTT, maxDTplusT, intercept_only, shortway: nextshortway);

                //Debug.Log("burnDT = " + burnDT + " burnTT = " + burnTT + " cost = " + burnCost + " bestCost = " + bestCost);

                if (burnCost < bestCost)
                {
                    bestDT       = burnDT;
                    bestTT       = burnTT;
                    bestshortway = nextshortway;
                    bestCost     = burnCost;
                    bestBurnVec  = burnVec;
                    currentDT    = bestDT;
                    currentTT    = bestTT;
                    currentCost  = bestCost;
                }
                else if (acceptanceProbabilityForBiImpulsive(currentCost, burnCost, temp) > random.NextDouble())
                {
                    currentDT   = burnDT;
                    currentTT   = burnTT;
                    currentCost = burnCost;
                }

                temp *= 1 - coolingRate;

                n++;
            }

            stopwatch.Stop();

            Debug.Log("MechJeb DeltaVAndTimeForBiImpulsiveAnnealed N = " + n + " time = " + stopwatch.Elapsed);

            bestUT = UT + bestDT;

            Debug.Log("Annealing results burnUT = " + bestUT + " zero'd burnUT = " + bestDT + " TT = " + bestTT + " Cost = " + bestCost +
                      " shortway= " + bestshortway);

            return bestBurnVec;
        }

        public static (Vector3d dv, double dt) DeltaVAndTimeForMoonReturnEjection(Orbit o, double ut, double targetPrimaryRadius)
        {
            CelestialBody moon = o.referenceBody;
            CelestialBody primary = moon.referenceBody;
            (V3 moonR0, V3 moonV0) = moon.orbit.RightHandedStateVectorsAtUT(ut);
            double moonSOI = moon.sphereOfInfluence;
            (V3 r0, V3 v0) = o.RightHandedStateVectorsAtUT(ut);

            double dtmin = o.eccentricity >= 1 ? 0 : double.NegativeInfinity;

            (V3 dv, double dt, double newPeR) = ReturnFromMoon.NextManeuver(primary.gravParameter, moon.gravParameter, moonR0,
                moonV0, moonSOI, r0, v0, targetPrimaryRadius, 0, dtmin);

            Debug.Log($"Solved PeR from calcluator: {newPeR}");

            return (dv.V3ToWorld(), ut + dt);
        }

        //Computes the delta-V of the burn at a given time required to zero out the difference in orbital velocities
        //between a given orbit and a target.
        public static Vector3d DeltaVToMatchVelocities(Orbit o, double UT, Orbit target)
        {
            return target.WorldOrbitalVelocityAtUT(UT) - o.WorldOrbitalVelocityAtUT(UT);
        }

        // Compute the delta-V of the burn at the givent time required to enter an orbit with a period of (resonanceDivider-1)/resonanceDivider of the starting orbit period
        public static Vector3d DeltaVToResonantOrbit(Orbit o, double UT, double f)
        {
            double a = o.ApR;
            double p = o.PeR;

            // Thanks wolframAlpha for the Math
            // x = (a^3 f^2 + 3 a^2 f^2 p + 3 a f^2 p^2 + f^2 p^3)^(1/3)-a
            double x = Math.Pow(
                Math.Pow(a, 3) * Math.Pow(f, 2) + 3 * Math.Pow(a, 2) * Math.Pow(f, 2) * p + 3 * a * Math.Pow(f, 2) * Math.Pow(p, 2) +
                Math.Pow(f, 2) * Math.Pow(p, 3), 1d / 3) - a;

            if (x < 0)
                return Vector3d.zero;

            if (f > 1)
                return DeltaVToChangeApoapsis(o, UT, x);
            return DeltaVToChangePeriapsis(o, UT, x);
        }

        // Compute the angular distance between two points on a unit sphere
        public static double Distance(double lat_a, double long_a, double lat_b, double long_b)
        {
            // Using Great-Circle Distance 2nd computational formula from http://en.wikipedia.org/wiki/Great-circle_distance
            // Note the switch from degrees to radians and back
            double lat_a_rad = UtilMath.Deg2Rad * lat_a;
            double lat_b_rad = UtilMath.Deg2Rad * lat_b;
            double long_diff_rad = UtilMath.Deg2Rad * (long_b - long_a);

            return UtilMath.Rad2Deg * Math.Atan2(Math.Sqrt(Math.Pow(Math.Cos(lat_b_rad) * Math.Sin(long_diff_rad), 2) +
                                                           Math.Pow(
                                                               Math.Cos(lat_a_rad) * Math.Sin(lat_b_rad) - Math.Sin(lat_a_rad) * Math.Cos(lat_b_rad) *
                                                               Math.Cos(long_diff_rad), 2)),
                Math.Sin(lat_a_rad) * Math.Sin(lat_b_rad) + Math.Cos(lat_a_rad) * Math.Cos(lat_b_rad) * Math.Cos(long_diff_rad));
        }

        // Compute an angular heading from point a to point b on a unit sphere
        public static double Heading(double lat_a, double long_a, double lat_b, double long_b)
        {
            // Using Great-Circle Navigation formula for initial heading from http://en.wikipedia.org/wiki/Great-circle_navigation
            // Note the switch from degrees to radians and back
            // Original equation returns 0 for due south, increasing clockwise. We add 180 and clamp to 0-360 degrees to map to compass-type headings
            double lat_a_rad = UtilMath.Deg2Rad * lat_a;
            double lat_b_rad = UtilMath.Deg2Rad * lat_b;
            double long_diff_rad = UtilMath.Deg2Rad * (long_b - long_a);

            return MuUtils.ClampDegrees360(180.0 / Math.PI * Math.Atan2(
                Math.Sin(long_diff_rad),
                Math.Cos(lat_a_rad) * Math.Tan(lat_b_rad) - Math.Sin(lat_a_rad) * Math.Cos(long_diff_rad)));
        }

        //Computes the deltaV of the burn needed to set a given LAN at a given UT.
        public static Vector3d DeltaVToShiftLAN(Orbit o, double UT, double newLAN)
        {
            Vector3d pos = o.WorldPositionAtUT(UT);
            // Burn position in the same reference frame as LAN
            double burn_latitude = o.referenceBody.GetLatitude(pos);
            double burn_longitude = o.referenceBody.GetLongitude(pos) + o.referenceBody.rotationAngle;

            const double target_latitude = 0; // Equator
            double target_longitude = 0;      // Prime Meridian

            // Select the location of either the descending or ascending node.
            // If the descending node is closer than the ascending node, or there is no ascending node, target the reverse of the newLAN
            // Otherwise target the newLAN
            if (o.AscendingNodeEquatorialExists() && o.DescendingNodeEquatorialExists())
            {
                if (o.TimeOfDescendingNodeEquatorial(UT) < o.TimeOfAscendingNodeEquatorial(UT))
                {
                    // DN is closer than AN
                    // Burning for the AN would entail flipping the orbit around, and would be very expensive
                    // therefore, burn for the corresponding Longitude of the Descending Node
                    target_longitude = MuUtils.ClampDegrees360(newLAN + 180.0);
                }
                else
                {
                    // DN is closer than AN
                    target_longitude = MuUtils.ClampDegrees360(newLAN);
                }
            }
            else if (o.AscendingNodeEquatorialExists() && !o.DescendingNodeEquatorialExists())
            {
                // No DN
                target_longitude = MuUtils.ClampDegrees360(newLAN);
            }
            else if (!o.AscendingNodeEquatorialExists() && o.DescendingNodeEquatorialExists())
            {
                // No AN
                target_longitude = MuUtils.ClampDegrees360(newLAN + 180.0);
            }
            else
            {
                throw new ArgumentException("OrbitalManeuverCalculator.DeltaVToShiftLAN: No Equatorial Nodes");
            }

            double desiredHeading = MuUtils.ClampDegrees360(Heading(burn_latitude, burn_longitude, target_latitude, target_longitude));
            var actualHorizontalVelocity = Vector3d.Exclude(o.Up(UT), o.WorldOrbitalVelocityAtUT(UT));
            Vector3d eastComponent = actualHorizontalVelocity.magnitude * Math.Sin(UtilMath.Deg2Rad * desiredHeading) * o.East(UT);
            Vector3d northComponent = actualHorizontalVelocity.magnitude * Math.Cos(UtilMath.Deg2Rad * desiredHeading) * o.North(UT);
            Vector3d desiredHorizontalVelocity = eastComponent + northComponent;
            return desiredHorizontalVelocity - actualHorizontalVelocity;
        }

        public static Vector3d DeltaVForSemiMajorAxis(Orbit o, double ut, double newSMA)
        {
            (V3 r, V3 v) = o.RightHandedStateVectorsAtUT(ut);

            V3 dv = ChangeOrbitalElement.DeltaV(o.referenceBody.gravParameter, r, v, newSMA, ChangeOrbitalElement.Type.SMA);

            return dv.V3ToWorld();
        }

        public static Vector3d DeltaVToShiftNodeLongitude(Orbit o, double UT, double newNodeLong)
        {
            // Get the location underneath the burn location at the current moment.
            // Note that this does NOT account for the rotation of the body that will happen between now
            // and when the vessel reaches the apoapsis.
            Vector3d pos = o.WorldPositionAtUT(UT);
            double burnRadius = o.Radius(UT);
            double oppositeRadius = 0;

            // Back out the rotation of the body to calculate the longitude of the apoapsis when the vessel reaches the node
            double degreeRotationToNode = (UT - Planetarium.GetUniversalTime()) * 360 / o.referenceBody.rotationPeriod;
            double NodeLongitude = o.referenceBody.GetLongitude(pos) - degreeRotationToNode;

            double LongitudeOffset = NodeLongitude - newNodeLong; // Amount we need to shift the Ap's longitude

            // Calculate a semi-major axis that gives us an orbital period that will rotate the body to place
            // the burn location directly over the newNodeLong longitude, over the course of one full orbit.
            // N tracks the number of full body rotations desired in a vessal orbit.
            // If N=0, we calculate the SMA required to let the body rotate less than a full local day.
            // If the resulting SMA would drop us under the 5x time warp limit, we deem it to be too low, and try again with N+1.
            // In other words, we allow the body to rotate more than 1 day, but less then 2 days.
            // As long as the resulting SMA is below the 5x limit, we keep increasing N until we find a viable solution.
            // This may place the apside out the sphere of influence, however.
            // TODO: find the cheapest SMA, instead of the smallest
            int N = -1;
            double target_sma = 0;

            while (oppositeRadius - o.referenceBody.Radius < o.referenceBody.timeWarpAltitudeLimits[4] && N < 20)
            {
                N++;
                double target_period = o.referenceBody.rotationPeriod * (LongitudeOffset / 360 + N);
                target_sma = Math.Pow(o.referenceBody.gravParameter * target_period * target_period / (4 * Math.PI * Math.PI), 1.0 / 3.0); // cube roo
                oppositeRadius = 2 * target_sma - burnRadius;
            }

            return DeltaVForSemiMajorAxis(o, UT, target_sma);
        }

        //
        // Global OrbitPool for re-using Orbit objects
        //

        public static readonly Pool<Orbit> OrbitPool = new Pool<Orbit>(createOrbit, resetOrbit);
        private static         Orbit       createOrbit()       { return new Orbit(); }
        private static         void        resetOrbit(Orbit o) { }

        private static readonly PatchedConics.SolverParameters solverParameters = new PatchedConics.SolverParameters();

        // Runs the PatchedConicSolver to do initial value "shooting" given an initial orbit, a maneuver dV and UT to execute, to a target Celestial's SOI
        //
        // initial   : initial parkig orbit
        // target    : the Body whose SOI we are shooting towards
        // dV        : the dV of the manuever off of the parking orbit
        // burnUT    : the time of the maneuver off of the parking orbit
        // arrivalUT : this is really more of an upper clamp on the simulation so that if we miss and never hit the body SOI it stops
        // intercept : this is the final computed intercept orbit, it should be in the SOI of the target body, but if it never hits it then the
        //             e.g. heliocentric orbit is returned instead, so the caller needs to check.
        //
        // FIXME: NREs when there's no next patch
        // FIXME: duplicates code with OrbitExtensions.CalculateNextOrbit()
        //
        public static void PatchedConicInterceptBody(Orbit initial, CelestialBody target, Vector3d dV, double burnUT, double arrivalUT,
            out Orbit intercept)
        {
            Orbit orbit = OrbitPool.Borrow();
            orbit.UpdateFromStateVectors(initial.getRelativePositionAtUT(burnUT), initial.getOrbitalVelocityAtUT(burnUT) + dV.xzy,
                initial.referenceBody, burnUT);
            orbit.StartUT = burnUT;
            orbit.EndUT   = orbit.eccentricity >= 1.0 ? orbit.period : burnUT + orbit.period;
            Orbit next_orbit = OrbitPool.Borrow();

            bool ok = PatchedConics.CalculatePatch(orbit, next_orbit, burnUT, solverParameters, null);
            while (ok && orbit.referenceBody != target && orbit.EndUT < arrivalUT)
            {
                OrbitPool.Release(orbit);
                orbit      = next_orbit;
                next_orbit = OrbitPool.Borrow();

                ok = PatchedConics.CalculatePatch(orbit, next_orbit, orbit.StartUT, solverParameters, null);
            }

            intercept = orbit;
            intercept.UpdateFromOrbitAtUT(orbit, arrivalUT, orbit.referenceBody);
            OrbitPool.Release(orbit);
            OrbitPool.Release(next_orbit);
        }

        // Takes an e.g. heliocentric orbit and a target planet celestial and finds the time of the SOI intercept.
        //
        //
        //
        public static void SOI_intercept(Orbit transfer, CelestialBody target, double UT1, double UT2, out double UT)
        {
            if (transfer.referenceBody != target.orbit.referenceBody)
                throw new ArgumentException("[MechJeb] SOI_intercept: transfer orbit must be in the same SOI as the target celestial");
            Func<double, object, double> f = delegate(double UT, object ign)
            {
                return (transfer.getRelativePositionAtUT(UT) - target.orbit.getRelativePositionAtUT(UT)).magnitude - target.sphereOfInfluence;
            };
            UT = 0;
            try { UT = BrentRoot.Solve(f, UT1, UT2, null); }
            catch (TimeoutException) { Debug.Log("[MechJeb] Brents method threw a timeout error (supressed)"); }
        }
    }
}
