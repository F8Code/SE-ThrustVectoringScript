using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Render.Scene;
using VRageMath;
using static IngameScript.Program;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public class Ship
        {
            //SETTINGS START
            public bool useGravityForDownAccel = false;

            public float sideMomentumCorrectionFactor = 0.5F;
            public float downMomentumCorrectionFactor = 1F;
            public float upMomentumCorrectionFactor = 0.25F;

            public float gravityCorrectionFactor = 1F;

            public float RotorsSpinSpeedModifier(float distanceToTravel)
            {
                return distanceToTravel;
            }

            public float HingesSpinSpeedModifier(float distanceToTravel)
            {
                return distanceToTravel;
            }
            //SETTINGS END

            public IMyShipController activeCockpit;
            public List<IMyShipController> cockpits = new List<IMyShipController>();
            public List<Engine> engines = new List<Engine>();

            public float maxThrust, shipMass;
            public int thrusterCount;

            public void UpdateShipsInfo()
            {
                List<IMyShipController> underControlCockpits = cockpits.Where(cockpit => cockpit.IsUnderControl).ToList();
                if (underControlCockpits.Count == 0)
                    activeCockpit = cockpits[0];
                else
                    activeCockpit = underControlCockpits.FirstOrDefault(c => c.IsMainCockpit) ?? underControlCockpits[0];

                thrusterCount = 0;
                maxThrust = 0;
                shipMass = activeCockpit.CalculateShipMass().TotalMass;

                foreach (Engine engine in engines)
                {
                    thrusterCount += engine.thrusters.Count;
                    foreach (IMyThrust thruster in engine.thrusters)
                        maxThrust += thruster.MaxEffectiveThrust;
                }
            }

            public void FlyTo(Vector3D desiredDirection)
            {
                Vector3D targetVector = CalculateThrustDirection(desiredDirection);
                SetEnginesToVector(targetVector);
                SetThrustOverride(targetVector);
            }

            Vector3D CalculateThrustDirection(Vector3D desiredDirection)
            {
                float availableThrust = maxThrust;
                Vector3D gravity = activeCockpit.GetNaturalGravity();
 
                Vector3D entireCorrection = CalculateShipCorrection(ref availableThrust, desiredDirection, gravity);
                if (desiredDirection.Length() == 0 || availableThrust == 0)
                    return entireCorrection;

                Vector3D modifiedDesiredDirection = Vector3D.Normalize(desiredDirection) * availableThrust / shipMass;

                if (useGravityForDownAccel && Vector3D.Dot(desiredDirection, gravity) > 0)
                {
                    Vector3D desiredDirectionProjectedOnGravity = Vector3D.ProjectOnVector(ref modifiedDesiredDirection, ref gravity);
                    if (desiredDirectionProjectedOnGravity.Length() > gravity.Length())
                        modifiedDesiredDirection *= gravity.Length() / desiredDirectionProjectedOnGravity.Length();
                }

                return modifiedDesiredDirection + entireCorrection;
            }

            Vector3D CalculateShipCorrection(ref float availableThrust, Vector3D desiredDirection, Vector3D gravity)
            {
                Vector3D gravityCorrection = CalculateGravityCorrection(desiredDirection, gravity);
                Vector3D momentumCorrection = CalculateMomentumCorrection(desiredDirection);

                return CalculateEntireCorrection(ref availableThrust, gravityCorrection, momentumCorrection);
            }

            Vector3D CalculateGravityCorrection(Vector3D desiredDirection, Vector3D gravity)
            {
                if (desiredDirection.Length() == 0 || gravity.Length() == 0 || (useGravityForDownAccel && Vector3D.Dot(desiredDirection, gravity) > 0))
                    return -gravity * gravityCorrectionFactor;

                return -Vector3D.ProjectOnPlane(ref gravity, ref desiredDirection) * gravityCorrectionFactor;
            }

            Vector3D CalculateMomentumCorrection(Vector3D desiredDirection)
            {
                Vector3D momentum = activeCockpit.CubeGrid.LinearVelocity;

                if (desiredDirection.Length() == 0)
                    return -activeCockpit.CubeGrid.LinearVelocity;

                return -Vector3D.ProjectOnPlane(ref momentum, ref desiredDirection);
            }

            Vector3D CalculateEntireCorrection(ref float availableThrust, Vector3D gravityCorrection, Vector3D momentumCorrection)
            {
                if (gravityCorrection.Length() == 0 || momentumCorrection.Length() == 0)
                    return gravityCorrection * gravityCorrectionFactor + momentumCorrection * sideMomentumCorrectionFactor;

                Vector3D gravityMomentumCorrection = Vector3D.ProjectOnVector(ref momentumCorrection, ref gravityCorrection);
                Vector3D sideMomentumCorrection = (momentumCorrection - gravityMomentumCorrection) * sideMomentumCorrectionFactor;

                if (Vector3D.Dot(gravityMomentumCorrection, gravityCorrection) < 0)
                    gravityMomentumCorrection *= upMomentumCorrectionFactor;
                else
                    gravityMomentumCorrection *= downMomentumCorrectionFactor;

                Vector3D entireGravityDirCorrection = gravityMomentumCorrection + gravityCorrection;

                availableThrust = Math.Max(0, availableThrust - (float)entireGravityDirCorrection.Length() * shipMass);

                if ((float)sideMomentumCorrection.Length() * shipMass > availableThrust)
                    momentumCorrection *= availableThrust / ((float)sideMomentumCorrection.Length() * shipMass);

                availableThrust = availableThrust - (float)sideMomentumCorrection.Length() * shipMass;

                return entireGravityDirCorrection + sideMomentumCorrection;
            }

            void SetEnginesToVector(Vector3D targetDirection)
            {
                foreach (Engine engine in engines)
                {
                    engine.SetRotorsToVector(targetDirection);
                    engine.SetHingesToVector(targetDirection);
                }
            }

            void SetThrustOverride(Vector3D desiredDirection)
            {
                foreach (Engine engine in engines)
                {
                    Vector3D currDirection = engine.thrusters[0].WorldMatrix.Backward;
                    float thrustOverride = shipMass * (float)desiredDirection.Length() / thrusterCount * (float)Math.Cos(Vector3D.Angle(desiredDirection, currDirection));
                    foreach (IMyThrust thruster in engine.thrusters)
                        thruster.ThrustOverride = thrustOverride;
                }
            }
        }

        public class Engine
        {
            private Ship parentShip;

            public List<IMyMotorStator> rotors = new List<IMyMotorStator>();
            public List<IMyMotorStator> hinges = new List<IMyMotorStator>();
            public List<IMyThrust> thrusters = new List<IMyThrust>();

            public void SetParentShip(Ship ship)
            {
                parentShip = ship;
            }

            public bool IsPartConnectedWithRotor(IMyMotorStator mechanicalPart)
            {
                foreach (IMyMotorStator rotor in rotors)
                {
                    if (mechanicalPart.CubeGrid == rotor.TopGrid || rotor.CubeGrid == mechanicalPart.TopGrid)
                        return true;
                }

                return false;
            }

            public bool IsThrusterConnectedWithHinge(IMyThrust thruster)
            {
                foreach (IMyMotorStator hinge in hinges)
                {
                    if (thruster.CubeGrid == hinge.TopGrid)
                        return true;
                }

                return false;
            }

            public string DisplayRotorNames()
            {
                string rotorNames = "";
                foreach (IMyMotorStator rotor in rotors)
                    rotorNames += rotor.CustomName + ", ";
                return rotorNames.Remove(rotorNames.Length - 2, 2);
            }

            public string DisplayHingeNames()
            {
                string hingeNames = "";
                foreach (IMyMotorStator hinge in hinges)
                    hingeNames += hinge.CustomName + ", ";
                return hingeNames.Remove(hingeNames.Length - 2, 2);
            }

            public string DisplayThrusterNames()
            {
                string thrusterNames = "";
                foreach (IMyThrust thruster in thrusters)
                    thrusterNames += thruster.CustomName + ", ";
                return thrusterNames.Remove(thrusterNames.Length - 2, 2);
            }

            public void SetRotorsToVector(Vector3D targetDirection)
            {
                Vector3D thrusterDirection = thrusters[0].WorldMatrix.Backward;

                Vector3D rotorPlaneNormal = rotors[0].WorldMatrix.Up;
                Vector3D targetDirInRotorPlane = Vector3D.ProjectOnPlane(ref targetDirection, ref rotorPlaneNormal);
                Vector3D thrusterDirInRotorPlane = Vector3D.ProjectOnPlane(ref thrusterDirection, ref rotorPlaneNormal);

                double distanceToTarget = Vector3D.Angle(targetDirInRotorPlane, thrusterDirInRotorPlane) * 180 / Math.PI;
                int rotorSpinDir = Math.Sign(Vector3D.Dot(Vector3D.Cross(targetDirInRotorPlane, thrusterDirInRotorPlane), rotorPlaneNormal));
                if (rotorSpinDir == 0 && Math.Round(distanceToTarget) == 180)
                    rotorSpinDir = 1;

                foreach (IMyMotorStator rotor in rotors)
                    rotor.TargetVelocityRPM = rotorSpinDir * parentShip.RotorsSpinSpeedModifier((float)distanceToTarget) / rotors.Count;
            }

            public void SetHingesToVector(Vector3D targetDirection)
            {
                Vector3D hingePlaneNormal = hinges[0].WorldMatrix.Up;
                Vector3D targetDirInHingePlane = Vector3D.ProjectOnPlane(ref targetDirection, ref hingePlaneNormal);

                //Making all forward vector point in hinge forward direction and all backward vectors point in hinge backward direction
                if (Vector3D.Dot(targetDirInHingePlane, hinges[0].WorldMatrix.Right) > 0)
                    while (Vector3D.Dot(targetDirInHingePlane, hinges[0].WorldMatrix.Right) > 0)
                        targetDirInHingePlane = Vector3D.Cross(hingePlaneNormal, targetDirInHingePlane);
                else if (Vector3D.Dot(targetDirInHingePlane, hinges[0].WorldMatrix.Forward) >= 0)
                    targetDirInHingePlane = Vector3D.Cross(hingePlaneNormal, targetDirInHingePlane);

                double targetAngle = Vector3D.Angle(targetDirInHingePlane, hinges[0].WorldMatrix.Left) * 180 / Math.PI;
                if (thrusters[0].WorldMatrix.Forward == hinges[0].Top.WorldMatrix.Forward)
                    targetAngle += (targetAngle > 0) ? -90 : 90;

                if (Vector3D.Dot(rotors[0].WorldMatrix.Up, targetDirection) > 0)
                    targetAngle *= -1;

                double firstHingeAngleDifference = targetAngle - hinges[0].Angle * 180 / Math.PI;
                int firstHingeSpinDir = Math.Sign(firstHingeAngleDifference);
                double distanceToTarget = Math.Abs(firstHingeAngleDifference);

                foreach (IMyMotorStator hinge in hinges)
                {
                    int thisHingeSpinDir = (hinge.WorldMatrix.Forward == hinges[0].WorldMatrix.Forward) ? firstHingeSpinDir : -firstHingeSpinDir;
                    hinge.TargetVelocityRPM = thisHingeSpinDir * parentShip.HingesSpinSpeedModifier((float)distanceToTarget);
                }
            }
        }

        Ship ship = new Ship();

        public Program()
        {
            //Getting block groups
            IMyBlockGroup cockpitsGroup = GridTerminalSystem.GetBlockGroupWithName("TVE Cockpits");

            //Cockpit assignment
            List<IMyShipController> cockpits = new List<IMyShipController>();
            cockpitsGroup.GetBlocksOfType(cockpits);
            ship.cockpits = cockpits;
            CreateEngines();

            ship.UpdateShipsInfo();

            Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update100;
        }

        public void CreateEngines()
        {
            //Retrieving engine blocks
            IMyBlockGroup enginesGroup = GridTerminalSystem.GetBlockGroupWithName("TVE Engines");

            //Assigning rotors and hinges
            List<IMyMotorStator> mechanicalParts = new List<IMyMotorStator>();
            enginesGroup.GetBlocksOfType(mechanicalParts);
            AssignRotors(mechanicalParts);
            AssignHinges(mechanicalParts);

            //Removing engines with wrongly placed rotors/hinges
            RemoveEnginesWithWrongRotorsOrHinges();

            //Assigning thrusters
            List<IMyThrust> thrusterParts = new List<IMyThrust>();
            enginesGroup.GetBlocksOfType(thrusterParts);
            AssignThrusters(thrusterParts);

            //Removing engines with wrongly placed thrusters
            RemoveEnginesWithWrongThrusters();
        }

        public void AssignRotors(List<IMyMotorStator> mechanicalParts)
        {
            foreach (IMyMotorStator mechanicalPart in mechanicalParts)
                if (!mechanicalPart.BlockDefinition.ToString().Contains("Hinge"))
                {
                    bool partAssigned = false;
                    foreach (Engine engine in ship.engines)
                    {
                        if (engine.IsPartConnectedWithRotor(mechanicalPart))
                        {
                            engine.rotors.Add(mechanicalPart);
                            partAssigned = true;
                            break;
                        }
                    }

                    if (!partAssigned)
                    {
                        Engine newEngine = new Engine();
                        newEngine.SetParentShip(ship);
                        newEngine.rotors.Add(mechanicalPart);
                        ship.engines.Add(newEngine);
                    }
                }
        }

        public void AssignHinges(List<IMyMotorStator> mechanicalParts)
        {
            foreach (IMyMotorStator mechanicalPart in mechanicalParts)
                if (mechanicalPart.BlockDefinition.ToString().Contains("Hinge"))
                    foreach (Engine engine in ship.engines)
                        if (engine.IsPartConnectedWithRotor(mechanicalPart))
                            engine.hinges.Add(mechanicalPart);
        }

        public void RemoveEnginesWithWrongRotorsOrHinges()
        {
            List<Engine> enginesToRemove = new List<Engine>();
            foreach (Engine engine in ship.engines)
            {
                //Without a hinge assigned
                if (engine.hinges.Count == 0)
                {
                    Echo("Error: Detected no hinges on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                    enginesToRemove.Add(engine);
                }
                else
                {
                    //With the first hinge having the wrong direction (if the rest is wrong, next check will detect that)
                    if (engine.hinges[0].Orientation.Forward.ToString() == "Up" || engine.hinges[0].Orientation.Forward.ToString() == "Down")
                    {
                        Echo("Error: Detected wrongly oriented hinges on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                        enginesToRemove.Add(engine);
                        break;
                    }

                    //With hinges having different directions
                    if (engine.hinges.Count == 1)
                        break;

                    List<string> acceptableDirs = new List<string>();
                    switch (engine.hinges[0].Orientation.Forward.ToString())
                    {
                        case "Right":
                        case "Left":
                            acceptableDirs.Add("Right");
                            acceptableDirs.Add("Left");
                            break;
                        case "Forward":
                        case "Backward":
                            acceptableDirs.Add("Forward");
                            acceptableDirs.Add("Backward");
                            break;
                    }
                    foreach (IMyMotorStator hinge in engine.hinges)
                        if (!acceptableDirs.Contains(engine.hinges[0].Orientation.Forward.ToString()))
                        {
                            Echo("Error: Detected differently oriented hinges on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                            enginesToRemove.Add(engine);
                            break;
                        }
                }
            }
            foreach (Engine engineToRemove in enginesToRemove)
                ship.engines.Remove(engineToRemove);
        }

        public void AssignThrusters(List<IMyThrust> thrusterParts)
        {
            foreach (IMyThrust thruster in thrusterParts)
                foreach (Engine engine in ship.engines)
                {
                    if (engine.IsThrusterConnectedWithHinge(thruster))
                    {
                        engine.thrusters.Add(thruster);
                        break;
                    }
                }
        }

        public void RemoveEnginesWithWrongThrusters()
        {
            List<Engine> enginesToRemove = new List<Engine>();
            foreach (Engine engine in ship.engines)
            {
                //Without a thruster assigned
                if (engine.thrusters.Count == 0)
                {
                    Echo("Error: Detected no thrusters on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                    enginesToRemove.Add(engine);
                }
                else
                {
                    string firstThrusterDir = engine.thrusters[0].Orientation.Forward.ToString();

                    //With the first thruster having the wrong direction (if the rest is wrong, next check will detect that)
                    if (firstThrusterDir != "Forward" && firstThrusterDir != "Backward")
                    {
                        Echo("Error: Detected wrongly oriented thrusters on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                        enginesToRemove.Add(engine);
                        break;
                    }

                    if (engine.thrusters.Count == 1)
                        break;

                    //With thrusters having different directions
                    foreach (IMyThrust thruster in engine.thrusters)
                        if (thruster.Orientation.Forward.ToString() != firstThrusterDir)
                        {
                            Echo("Error: Detected differently oriented thrusters on rotor group: \n" + engine.DisplayRotorNames() + "\n");
                            enginesToRemove.Add(engine);
                            break;
                        }
                }
            }
            foreach (Engine engineToRemove in enginesToRemove)
                ship.engines.Remove(engineToRemove);

        }

        public void Save()
        {

        }

        public void Main(string arg, UpdateType updateSource)
        {
            foreach (Engine engine in ship.engines)
                Echo($"Engine:\n-rotors: {engine.DisplayRotorNames()}\n-hinges: {engine.DisplayHingeNames()}\n-thrusters: {engine.DisplayThrusterNames()}");

            if ((updateSource & UpdateType.Update100) != 0)
            {
                ship.UpdateShipsInfo();
                return;
            }

            ship.FlyTo(Vector3D.TransformNormal(ship.activeCockpit.MoveIndicator, ship.activeCockpit.WorldMatrix));
        }
    }
}
