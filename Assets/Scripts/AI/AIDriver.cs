using System.Collections.Generic;
using RallyX.Racing;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.AI
{
    /// <summary>
    /// Races a car the way a player would, through <see cref="VehicleController.SetInput"/>: follows a racing line,
    /// plans its speed for corners and surfaces, countersteers and eases off in slides, passes slower cars and makes
    /// room alongside, takes shortcuts on some laps, and recovers when stuck, rolled over, lost off the road or going
    /// the wrong way. Its pace comes from its profile and the difficulty only, never from where the player is.
    /// </summary>
    [RequireComponent(typeof(VehicleController), typeof(RaceParticipant))]
    [DisallowMultipleComponent]
    public sealed class AIDriver : MonoBehaviour
    {
        // Other cars within this distance along the route (either way) count as alongside, metres.
        private const float AlongsideDistance = 5f;

        // A side is only used for passing if its room lasts this far past the car being passed, metres.
        private const float PassingRoomDistance = 40f;

        // The shortcut decision is made in this window before a branch leaves the main loop, metres.
        private const float BranchDecisionNearest = 60f;
        private const float BranchDecisionFurthest = 250f;

        // Putting the car back on the track: lift, clearance before the next gate and from other cars, search step (metres).
        private const float RespawnLift = 0.5f;
        private const float RespawnGateClearance = 3f;
        private const float RespawnCarClearance = 7f;
        private const float RespawnSearchStep = 2f;

        // Race distance to cover after reversing out before the reversing attempts are used up again, metres.
        private const float RecoveredDistance = 30f;

        // Below this uprightness (dot of the car's up with world up) the car is on its side or roof.
        private const float FlippedUprightness = 0.3f;

        private readonly List<AIRoute> routes = new List<AIRoute>();
        private readonly List<float[]> speedPlans = new List<float[]>();
        private readonly List<bool[]> flightPlans = new List<bool[]>();

        private VehicleController vehicle;
        private RaceParticipant participant;
        private RaceManager race;
        private AISettings settings;
        private AIDriverProfile profile;
        private AIDifficulty difficulty = AIDifficulty.Normal;
        private float[] branchLeaveDistances = System.Array.Empty<float>();
        private int[] branchDecisionLaps = System.Array.Empty<int>();
        private float wheelbase;
        private float rearAxle;
        private bool subscribed;

        private int routeIndex;
        private float routeDistance;
        private float lateralOffset;
        private float stuckTime;
        private float flippedTime;
        private float offRoadTime;
        private float wrongWayTime;
        private float reverseTimeLeft;
        private Vector3 reverseStart;
        private int reverseAttemptsUsed;
        private float lastRecoveryRaceDistance;

        public AIDriverProfile Profile => profile;
        public AIRoute ActiveRoute => routes.Count > 0 ? routes[routeIndex] : null;

        /// <summary>How hard the driver races. Changing it re-plans the driver's speeds straight away.</summary>
        public AIDifficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                PlanSpeeds();
            }
        }

        /// <summary>Times the car was put back on the track this race.</summary>
        public int Respawns { get; private set; }

        /// <summary>Times the driver reversed out of trouble this race.</summary>
        public int ReverseManoeuvres { get; private set; }

        /// <summary>Laps this race on which the driver chose a branch such as a shortcut.</summary>
        public int BranchesTaken { get; private set; }

        /// <summary>Speed the plan asks for just ahead, m/s, before traffic is taken into account.</summary>
        public float PlannedSpeed { get; private set; }

        /// <summary>Speed the driver is aiming for, m/s, after slowing for traffic.</summary>
        public float TargetSpeed { get; private set; }

        /// <summary>How far the car is to the right of its racing line (negative = left), metres.</summary>
        public float OffLine { get; private set; }

        /// <summary>How far the tail is sliding: the rear axle's direction of travel from the nose, degrees (+ = to the right).</summary>
        public float Slide { get; private set; }

        public void Configure(RaceManager newRace, AISettings newSettings, AIDriverProfile newProfile, IReadOnlyList<AIRoute> newRoutes,
            AIDifficulty newDifficulty)
        {
            Unsubscribe();
            race = newRace;
            settings = newSettings;
            profile = newProfile;
            difficulty = newDifficulty;
            vehicle = GetComponent<VehicleController>();
            participant = GetComponent<RaceParticipant>();
            MeasureAxles(vehicle, out wheelbase, out rearAxle);

            routes.Clear();
            routes.AddRange(newRoutes);

            branchLeaveDistances = new float[routes.Count];
            branchDecisionLaps = new int[routes.Count];
            for (int r = 1; r < routes.Count; r++)
                branchLeaveDistances[r] = race.Circuit.TrackDistance(routes[r].Branch.GetPoint(0));

            if (isActiveAndEnabled)
                Subscribe();
            ResetForNewRace();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (subscribed || race == null)
                return;
            race.RaceRestarted += ResetForNewRace;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;
            if (race != null)
                race.RaceRestarted -= ResetForNewRace;
            subscribed = false;
        }

        private void ResetForNewRace()
        {
            if (routes.Count == 0)
                return;

            // Planned afresh for every race, so edits to the profile or the difficulty tuning apply from the next restart.
            PlanSpeeds();
            routeIndex = 0;
            routeDistance = routes[0].FindClosest(vehicle.Body.position);
            lateralOffset = profile.LaneOffset;
            stuckTime = flippedTime = offRoadTime = wrongWayTime = reverseTimeLeft = 0f;
            reverseAttemptsUsed = 0;
            lastRecoveryRaceDistance = 0f;
            Respawns = ReverseManoeuvres = BranchesTaken = 0;
            for (int r = 0; r < branchDecisionLaps.Length; r++)
                branchDecisionLaps[r] = -1;
        }

        private void FixedUpdate()
        {
            if (race == null || routes.Count == 0)
                return;

            var tracker = participant.Tracker;
            if (!race.isActiveAndEnabled || race.State != RaceState.Racing || tracker == null || tracker.Finished)
            {
                vehicle.SetInput(default);
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            var body = vehicle.Body;
            Vector3 position = body.position;
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;

            ChooseBranch(tracker, position);
            var route = routes[routeIndex];
            routeDistance = route.FindClosest(position, routeDistance, 30f);

            float planned = PlannedSpeedAt(routeDistance + speed * settings.SpeedLookAheadTime);
            float target = AvoidTraffic(route, position, velocity, speed, planned, deltaTime);
            PlannedSpeed = planned;
            TargetSpeed = target;
            OffLine = route.LateralOffset(position, routeDistance);
            Slide = RearSlipAngle();
            if (Recover(route, tracker, position, speed, target > settings.StuckSpeed * 2f, deltaTime))
                return;

            float steer = SteerTowardsLine(route, position, speed, Slide);
            if (target < 0.5f && vehicle.ForwardSpeed < 1f)
            {
                // Waiting behind a car: hold still. The brake pedal would engage reverse here.
                vehicle.SetInput(new VehicleInputState(0f, 0f, steer, true));
                return;
            }

            float error = target - speed;
            float throttle = error > 0f ? Mathf.Clamp01(error * settings.ThrottleGain) : 0f;
            float brake = error < -settings.BrakeDeadband ? Mathf.Clamp01((-error - settings.BrakeDeadband) * settings.BrakeGain) : 0f;

            // Brakes do nothing in the air, where the pedal would only dip the nose (air pitch control).
            if (!vehicle.IsGrounded)
                brake = 0f;

            // Sliding: ease off so the rear tyres can grip again, fully off at twice the slip the driver tolerates.
            float excessSlide = Mathf.Abs(Slide) - settings.SlideAngle;
            if (excessSlide > 0f)
                throttle *= Mathf.Clamp01(1f - excessSlide / settings.SlideAngle);

            vehicle.SetInput(new VehicleInputState(throttle, brake, steer, false));
        }

        /// <summary>
        /// Angle between the car's nose and the direction its rear axle is travelling, degrees (+ = travelling to the
        /// right of the nose). Near 0 while the tyres grip; grows when the tail slides out. 0 when not moving forwards.
        /// </summary>
        private float RearSlipAngle()
        {
            var body = vehicle.Body;
            Vector3 nose = body.rotation * Vector3.forward;
            Vector3 travel = body.GetPointVelocity(body.position + body.rotation * new Vector3(0f, 0f, rearAxle));
            nose.y = 0f;
            travel.y = 0f;
            return Vector3.Dot(travel, nose) > settings.StuckSpeed ? Vector3.SignedAngle(nose, travel, Vector3.up) : 0f;
        }

        /// <summary>Speeds and jumps along every route for the driver's profile at the current difficulty.</summary>
        private void PlanSpeeds()
        {
            if (profile == null || settings == null || vehicle == null)
                return;

            var level = settings.GetDifficulty(difficulty);
            float cornerGrip = profile.CornerGrip * level.Cornering;
            float braking = profile.Braking * level.Braking;
            float topSpeed = vehicle.Config.MaxSpeed * Mathf.Min(1f, profile.TopSpeed * level.TopSpeed);

            speedPlans.Clear();
            flightPlans.Clear();
            foreach (var route in routes)
            {
                speedPlans.Add(route.PlanSpeeds(cornerGrip, braking, topSpeed, out bool[] airborne));
                flightPlans.Add(airborne);
            }
        }

        private float PlannedSpeedAt(float distance) => speedPlans[routeIndex][routes[routeIndex].IndexAt(distance)];

        /// <summary>
        /// Pure pursuit: the steering that arcs the car onto the line a speed-dependent distance ahead. The arc starts
        /// from the way the car is actually going, so in a slide the wheels are first turned into it (countersteer).
        /// </summary>
        /// <param name="slide">Rear slip angle, degrees (<see cref="RearSlipAngle"/>).</param>
        private float SteerTowardsLine(AIRoute route, Vector3 position, float speed, float slide)
        {
            float lookAhead = Mathf.Clamp(settings.LookAheadBase + settings.LookAheadPerSpeed * speed, settings.LookAheadBase, settings.LookAheadMax);
            float aimDistance = routeDistance + lookAhead;
            int aimIndex = route.IndexAt(aimDistance);
            float offset = Mathf.Clamp(lateralOffset, -route.RoomLeft(aimIndex), route.RoomRight(aimIndex));
            Vector3 toAim = route.Evaluate(aimDistance) + route.RightAt(aimDistance) * offset - position;
            toAim.y = 0f;
            float countersteer = slide * settings.Countersteer;
            Vector3 travel = Quaternion.AngleAxis(countersteer, Vector3.up) * (vehicle.Body.rotation * Vector3.forward);
            travel.y = 0f;

            float alpha = Vector3.SignedAngle(travel, toAim, Vector3.up) * Mathf.Deg2Rad;
            float wheelAngle = countersteer + Mathf.Atan2(2f * wheelbase * Mathf.Sin(alpha), Mathf.Max(toAim.magnitude, 1f)) * Mathf.Rad2Deg;

            // Steering authority shrinks with speed; ask for proportionally more input to get the wheel angle wanted.
            var config = vehicle.Config;
            float speedFactor = Mathf.Clamp01(Mathf.Abs(vehicle.ForwardSpeed) / config.MaxSpeed);
            float authority = config.MaxSteerAngle * config.SteerBySpeed.Evaluate(speedFactor);
            return Mathf.Clamp(wheelAngle / Mathf.Max(authority, 1f), -1f, 1f);
        }

        /// <summary>
        /// Moves the driver sideways to pass a slower car ahead or to keep a car's width from one alongside, and slows
        /// down behind a car it can't get past. Returns the speed to aim for.
        /// </summary>
        private float AvoidTraffic(AIRoute route, Vector3 position, Vector3 velocity, float speed, float targetSpeed, float deltaTime)
        {
            int here = route.IndexAt(routeDistance);
            float separation = settings.PassingSeparation;
            float range = Mathf.Max(settings.TrafficLookAheadMin, speed * settings.TrafficLookAheadTime);
            float mySpeedAlong = Vector3.Dot(velocity, route.DirectionAt(routeDistance));
            float wanted = Mathf.Clamp(profile.LaneOffset, -route.RoomLeft(here), route.RoomRight(here));
            bool jumpAhead = JumpWithin(route, here, speed * settings.JumpCaution);

            bool blocked = false;
            float blockerAhead = float.MaxValue;
            float blockerOffset = 0f;
            float blockerSpeedAlong = 0f;
            int blockerIndex = here;
            foreach (var other in race.Participants)
            {
                if (other == null || other == participant)
                    continue;

                var otherBody = other.Vehicle.Body;
                Vector3 otherPosition = otherBody.position;
                if ((otherPosition - position).sqrMagnitude > (range + 20f) * (range + 20f))
                    continue;

                float otherDistance = route.FindClosest(otherPosition, routeDistance, range + 20f);
                float ahead = route.Separation(routeDistance, otherDistance);
                if (ahead < -AlongsideDistance || ahead > range)
                    continue;

                float otherOffset = route.LateralOffset(otherPosition, otherDistance);
                if (Mathf.Abs(ahead) <= AlongsideDistance)
                {
                    // Alongside: keep a car's width apart, staying on the side we are already on.
                    if (Mathf.Abs(otherOffset - wanted) < separation)
                        wanted = lateralOffset >= otherOffset ? otherOffset + separation : otherOffset - separation;
                    continue;
                }

                if (Mathf.Abs(otherOffset - wanted) >= separation)
                    continue;

                float otherSpeedAlong = Vector3.Dot(otherBody.linearVelocity, route.DirectionAt(otherDistance));
                bool closingIn = mySpeedAlong - otherSpeedAlong > 0.5f || ahead < settings.FollowDistance;
                if (!closingIn || ahead >= blockerAhead)
                    continue;

                blocked = true;
                blockerAhead = ahead;
                blockerOffset = otherOffset;
                blockerSpeedAlong = otherSpeedAlong;
                blockerIndex = route.IndexAt(otherDistance);
            }

            if (blocked)
            {
                // Pass on whichever side has room, preferring the one needing the smaller move; not before a jump. The room
                // must last while passing: a side that closes up just past the other car (the line swinging to that edge)
                // would leave no way back onto the road.
                float left = blockerOffset - separation;
                float right = blockerOffset + separation;
                bool leftFits = !jumpAhead && left >= -RoomAhead(route, blockerIndex, rightSide: false);
                bool rightFits = !jumpAhead && right <= RoomAhead(route, blockerIndex, rightSide: true);
                if (leftFits && (!rightFits || Mathf.Abs(left - lateralOffset) <= Mathf.Abs(right - lateralOffset)))
                    wanted = left;
                else if (rightFits)
                    wanted = right;
                else
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, blockerSpeedAlong + (blockerAhead - settings.FollowDistance) * 0.5f));

                // Until clear of it sideways, don't run into the back of it.
                if (Mathf.Abs(lateralOffset - blockerOffset) < separation && blockerAhead < settings.FollowDistance)
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, blockerSpeedAlong));
            }

            wanted = Mathf.Clamp(wanted, -route.RoomLeft(here), route.RoomRight(here));
            lateralOffset = Mathf.MoveTowards(lateralOffset, wanted, settings.SideStepSpeed * deltaTime);
            return targetSpeed;
        }

        /// <summary>The least room on one side of the line from route point <paramref name="from"/> over the passing distance, metres.</summary>
        private static float RoomAhead(AIRoute route, int from, bool rightSide)
        {
            float room = float.MaxValue;
            float covered = 0f;
            for (int i = from; covered < PassingRoomDistance && i - from < route.Count; i++)
            {
                room = Mathf.Min(room, rightSide ? route.RoomRight(i) : route.RoomLeft(i));
                covered += route.SegmentLength(i);
            }

            return room;
        }

        /// <summary>True if the car will be in the air within <paramref name="distance"/> metres of route point <paramref name="from"/>.</summary>
        private bool JumpWithin(AIRoute route, int from, float distance)
        {
            var airborne = flightPlans[routeIndex];
            float covered = 0f;
            for (int i = from; covered < distance && i - from < route.Count; i++)
            {
                if (airborne[i % route.Count])
                    return true;
                covered += route.SegmentLength(i);
            }

            return false;
        }

        /// <summary>Once per lap, just before each branch, decides whether to take it (a repeatable roll of the profile's chance).</summary>
        private void ChooseBranch(RaceTracker tracker, Vector3 position)
        {
            for (int r = 1; r < routes.Count; r++)
            {
                float toBranch = race.Circuit.DistanceAhead(tracker.TrackDistance, branchLeaveDistances[r]);
                int lap = tracker.CompletedLaps;
                if (toBranch < BranchDecisionNearest || toBranch > BranchDecisionFurthest || branchDecisionLaps[r] == lap)
                    continue;

                branchDecisionLaps[r] = lap;
                bool take = Roll(r, lap) < profile.ShortcutChance;
                if (take)
                    BranchesTaken++;

                int chosen = take ? r : 0;
                if (chosen != routeIndex)
                {
                    routeIndex = chosen;
                    routeDistance = routes[chosen].FindClosest(position);
                }
            }
        }

        private double Roll(int branch, int lap)
        {
            unchecked
            {
                int seed = 17;
                foreach (char c in profile.DisplayName)
                    seed = seed * 31 + c;
                seed = seed * 31 + branch;
                seed = seed * 31 + lap;
                return new System.Random(seed).NextDouble();
            }
        }

        /// <summary>
        /// Watches for trouble. Stuck: reverse out, and after repeated attempts go back on the track. Rolled over,
        /// lost off the road or going the wrong way for too long: go back on the track. Returns true while it is in charge.
        /// </summary>
        private bool Recover(AIRoute route, RaceTracker tracker, Vector3 position, float speed, bool wantsToMove, float deltaTime)
        {
            float uprightness = Vector3.Dot(vehicle.Body.rotation * Vector3.up, Vector3.up);
            flippedTime = uprightness < FlippedUprightness ? flippedTime + deltaTime : 0f;

            int here = route.IndexAt(routeDistance);
            float offset = route.LateralOffset(position, routeDistance);
            float toEdge = (offset >= 0f ? route.RoomRight(here) : route.RoomLeft(here)) + settings.EdgeMargin;
            offRoadTime = Mathf.Abs(offset) - toEdge > settings.OffRoadDistance ? offRoadTime + deltaTime : 0f;
            wrongWayTime = tracker.IsWrongWay ? wrongWayTime + deltaTime : 0f;

            if (flippedTime > settings.FlippedTime || offRoadTime > settings.OffRoadTime || wrongWayTime > settings.WrongWayTime)
            {
                PutBackOnTrack(route, tracker);
                return true;
            }

            if (reverseTimeLeft > 0f)
            {
                // Back straight out first, clear of whatever the nose is against; then reversing with the wheels
                // turned away from the line swings the nose back towards it.
                reverseTimeLeft -= deltaTime;
                Vector3 backedUp = position - reverseStart;
                backedUp.y = 0f;
                bool backingOut = backedUp.magnitude < settings.BackOutDistance;
                float steer = backingOut ? 0f : -SteerTowardsLine(route, position, 0f, 0f);
                vehicle.SetInput(new VehicleInputState(0f, 1f, steer, false));
                return true;
            }

            if (tracker.RaceDistance - lastRecoveryRaceDistance > RecoveredDistance)
                reverseAttemptsUsed = 0;

            stuckTime = wantsToMove && speed < settings.StuckSpeed ? stuckTime + deltaTime : 0f;
            if (stuckTime < settings.StuckTime)
                return false;

            stuckTime = 0f;
            lastRecoveryRaceDistance = tracker.RaceDistance;
            if (reverseAttemptsUsed < settings.ReverseAttempts)
            {
                reverseAttemptsUsed++;
                ReverseManoeuvres++;
                reverseTimeLeft = settings.ReverseTime;
                reverseStart = position;
                return true;
            }

            PutBackOnTrack(route, tracker);
            return true;
        }

        /// <summary>
        /// Places the car on the line at rest, facing the right way: near where it was, but never beyond the next
        /// checkpoint it still has to drive through, and clear of other cars.
        /// </summary>
        private void PutBackOnTrack(AIRoute route, RaceTracker tracker)
        {
            var circuit = race.Circuit;
            int next = tracker.NextCheckpoint;
            float nextGate = circuit.CheckpointDistance(next);
            float previousGate = circuit.CheckpointDistance((next + circuit.CheckpointCount - 1) % circuit.CheckpointCount);
            float gateGap = circuit.DistanceAhead(previousGate, nextGate);

            float distance = routeDistance;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                Vector3 point = route.Evaluate(distance);
                float toGate = circuit.DistanceAhead(circuit.TrackDistance(point), nextGate);
                if (toGate >= RespawnGateClearance && toGate <= gateGap && !CarNear(point))
                    break;
                distance -= RespawnSearchStep;
            }

            vehicle.PlaceAt(route.Evaluate(distance) + Vector3.up * RespawnLift, Quaternion.LookRotation(route.DirectionAt(distance)));
            routeDistance = Mathf.Repeat(distance, route.Length);
            lateralOffset = 0f;
            stuckTime = flippedTime = offRoadTime = wrongWayTime = reverseTimeLeft = 0f;
            reverseAttemptsUsed = 0;
            Respawns++;
        }

        private bool CarNear(Vector3 point)
        {
            foreach (var other in race.Participants)
            {
                if (other != null && other != participant && (other.Vehicle.Body.position - point).sqrMagnitude < RespawnCarClearance * RespawnCarClearance)
                    return true;
            }

            return false;
        }

        /// <summary>Distance between the axles and how far the rear axle is ahead of the car's origin (negative = behind), metres.</summary>
        private static void MeasureAxles(VehicleController car, out float wheelbase, out float rearAxle)
        {
            float front = 0f;
            float rear = 0f;
            int frontCount = 0;
            int rearCount = 0;
            foreach (var wheel in car.Wheels)
            {
                float along = car.transform.InverseTransformPoint(wheel.transform.position).z;
                if (wheel.IsFront)
                {
                    front += along;
                    frontCount++;
                }
                else
                {
                    rear += along;
                    rearCount++;
                }
            }

            bool measured = frontCount > 0 && rearCount > 0;
            wheelbase = measured ? Mathf.Max(1f, front / frontCount - rear / rearCount) : 2.6f;
            rearAxle = measured ? rear / rearCount : -1.3f;
        }

        private void OnDrawGizmosSelected()
        {
            var route = ActiveRoute;
            if (route == null)
                return;

            Gizmos.color = Color.magenta;
            for (float d = 0f; d < 60f; d += 2f)
                Gizmos.DrawLine(route.Evaluate(routeDistance + d) + Vector3.up, route.Evaluate(routeDistance + d + 2f) + Vector3.up);
        }
    }
}
