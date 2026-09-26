using RallyX.Tracks;
using UnityEngine;

namespace RallyX.AI
{
    /// <summary>
    /// A closed loop an AI driver follows for a lap: a racing line either round the main loop or through a branch
    /// such as a shortcut. At each point it knows the room to either side of the line, the corner radius and the
    /// surface grip. Built by <see cref="AIRouteBuilder"/>.
    /// </summary>
    public sealed class AIRoute
    {
        // Rounds of planning speeds and finding the jumps they cause before the plan is used.
        private const int AirborneRounds = 4;

        // A flight may land at least this far to the side of the line, metres, and never has to take off slower
        // than this, m/s, however far it would drift.
        private const float MinLandingRoom = 1f;
        private const float MinTakeoffSpeed = 12f;

        // Longest flight looked for, in route points (about 2 m each).
        private const int MaxFlightPoints = 60;

        // How far the road may drop below a car's path before the car counts as leaving the ground, metres.
        // The suspension takes up smaller dips.
        private const float TakeoffTolerance = 0.1f;

        private readonly Vector3[] points;
        private readonly float[] distances;
        private readonly Vector3[] rights;
        private readonly float[] roomLeft;
        private readonly float[] roomRight;
        private readonly float[] radii;
        private readonly float[] grip;

        public AIRoute(string name, TrackBranch branch, Vector3[] points, Vector3[] rights, float[] roomLeft, float[] roomRight,
            float[] radii, float[] grip)
        {
            Name = name;
            Branch = branch;
            this.points = points;
            this.rights = rights;
            this.roomLeft = roomLeft;
            this.roomRight = roomRight;
            this.radii = radii;
            this.grip = grip;
            distances = LoopMath.CumulativeDistances(points, out float length);
            Length = length;
        }

        public string Name { get; }

        /// <summary>The branch this route goes through, or null for the main loop.</summary>
        public TrackBranch Branch { get; }

        public int Count => points.Length;
        public float Length { get; }

        public Vector3 PointAt(int index) => points[LoopMath.Wrap(index, points.Length)];

        /// <summary>Length of the segment from a point to the next, metres.</summary>
        public float SegmentLength(int index) => Vector3.Distance(PointAt(index), PointAt(index + 1));

        /// <summary>How far the car's centre may move left of the line at a point and stay clear of the edge, metres.</summary>
        public float RoomLeft(int index) => roomLeft[LoopMath.Wrap(index, points.Length)];

        /// <summary>How far the car's centre may move right of the line at a point and stay clear of the edge, metres.</summary>
        public float RoomRight(int index) => roomRight[LoopMath.Wrap(index, points.Length)];

        /// <summary>Radius of the line's curve at a point, metres (infinite on straights).</summary>
        public float Radius(int index) => radii[LoopMath.Wrap(index, points.Length)];

        /// <summary>Grip of the surface under the line at a point (1 = asphalt).</summary>
        public float Grip(int index) => grip[LoopMath.Wrap(index, points.Length)];

        /// <summary>Index of the point nearest a distance along the route.</summary>
        public int IndexAt(float distance)
        {
            int index = LoopMath.FindSegment(points, distances, Length, distance, out float t);
            return t < 0.5f ? index : LoopMath.Wrap(index + 1, points.Length);
        }

        public Vector3 Evaluate(float distance) => LoopMath.EvaluatePosition(points, distances, Length, distance);

        /// <summary>Horizontal unit vector to the right of the line at a distance along the route.</summary>
        public Vector3 RightAt(float distance) => rights[IndexAt(distance)];

        /// <summary>Horizontal direction of travel at a distance along the route.</summary>
        public Vector3 DirectionAt(float distance)
        {
            Vector3 direction = Evaluate(distance + 1f) - Evaluate(distance - 1f);
            direction.y = 0f;
            return direction.normalized;
        }

        /// <summary>Distance along the route of the line point nearest a position (see <see cref="LoopMath.FindClosestDistance"/>).</summary>
        public float FindClosest(Vector3 position, float? hint = null, float searchRadius = 60f) =>
            LoopMath.FindClosestDistance(points, distances, Length, position, hint, searchRadius);

        /// <summary>How far a position is to the right of the line (negative = left) at a distance along the route, metres.</summary>
        public float LateralOffset(Vector3 position, float distance) => Vector3.Dot(position - Evaluate(distance), RightAt(distance));

        /// <summary>
        /// Speed for every point: the most a corner allows at <paramref name="cornerGrip"/> m/s² of sideways grip (scaled by
        /// the surface), capped at <paramref name="topSpeed"/>, and slowed early enough to brake for what comes next at
        /// <paramref name="braking"/> m/s² (also scaled by the surface).
        /// </summary>
        public float[] PlanSpeeds(float cornerGrip, float braking, float topSpeed) => PlanSpeeds(cornerGrip, braking, topSpeed, out _);

        /// <inheritdoc cref="PlanSpeeds(float, float, float)"/>
        /// <param name="airborne">For each point, whether a car at the planned speed is in the air there.</param>
        public float[] PlanSpeeds(float cornerGrip, float braking, float topSpeed, out bool[] airborne)
        {
            int count = points.Length;
            var cornerLimits = new float[count];
            for (int i = 0; i < count; i++)
                cornerLimits[i] = Mathf.Min(topSpeed, Mathf.Sqrt(cornerGrip * grip[i] * radii[i]));

            // A car can't brake or turn in the air: it has to take off slowly enough for whatever comes after it lands,
            // and not fly so far that the road bends out from under it. Where it flies depends on the planned speed,
            // so plan, find the flights, and plan again.
            airborne = new bool[count];
            var takeoffLimits = new float[count];
            for (int i = 0; i < count; i++)
                takeoffLimits[i] = float.MaxValue;

            var plan = new float[count];
            for (int round = 0; round < AirborneRounds; round++)
            {
                Plan(plan, cornerLimits, takeoffLimits, cornerGrip, braking, airborne);
                if (!MarkFlights(plan, airborne, takeoffLimits))
                    return plan;
            }

            Plan(plan, cornerLimits, takeoffLimits, cornerGrip, braking, airborne);
            return plan;
        }

        /// <summary>
        /// Speeds within the corner and takeoff limits, lowered so every slowdown can be braked for (walking backwards
        /// twice round the loop). Braking and cornering share the tyres' grip, so less braking is available mid-corner.
        /// </summary>
        private void Plan(float[] plan, float[] cornerLimits, float[] takeoffLimits, float cornerGrip, float braking, bool[] airborne)
        {
            int count = plan.Length;
            for (int i = 0; i < count; i++)
                plan[i] = Mathf.Min(cornerLimits[i], takeoffLimits[i]);

            for (int pass = 0; pass < count * 2; pass++)
            {
                int i = count - 1 - pass % count;
                float next = plan[(i + 1) % count];
                float reach = 0f;
                if (!airborne[i])
                {
                    float cornering = Mathf.Clamp01(next * next / radii[i] / (cornerGrip * grip[i]));
                    reach = 2f * braking * grip[i] * Mathf.Sqrt(1f - cornering * cornering) * SegmentLength(i);
                }

                plan[i] = Mathf.Min(plan[i], Mathf.Sqrt(next * next + reach));
            }
        }

        /// <summary>
        /// Marks the points a car at the planned speeds flies over: wherever the road falls away faster than a
        /// thrown object would, up to where the flight path meets the road again. Where the road bends away under a
        /// flight by more than the room beside the line, lowers that takeoff's speed limit. Returns true if anything changed.
        /// </summary>
        private bool MarkFlights(float[] plan, bool[] airborne, float[] takeoffLimits)
        {
            int count = plan.Length;
            bool changed = false;
            var marks = new bool[count];
            float gravity = Physics.gravity.magnitude;
            for (int i = 0; i < count; i++)
            {
                Vector3 before = PointAt(i - 1);
                Vector3 takeoff = PointAt(i);
                float run = FlatDistance(before, takeoff);
                if (run < 1e-3f)
                    continue;

                float slope = (takeoff.y - before.y) / run;
                float horizontalSpeed = plan[i] / Mathf.Sqrt(1f + slope * slope);
                float flown = 0f;
                int landing = i + 1;
                for (; landing - i < MaxFlightPoints; landing++)
                {
                    flown += FlatDistance(PointAt(landing - 1), PointAt(landing));
                    float flightHeight = takeoff.y + slope * flown - gravity * flown * flown / (2f * horizontalSpeed * horizontalSpeed);
                    if (PointAt(landing).y >= flightHeight - TakeoffTolerance)
                        break;
                }

                if (landing == i + 1)
                    continue;

                for (int k = i; k < landing; k++)
                    marks[LoopMath.Wrap(k, count)] = true;

                // The car flies straight on; how far does it land from the line, and is there room there?
                Vector3 heading = new Vector3(takeoff.x - before.x, 0f, takeoff.z - before.z) / run;
                Vector3 toLanding = PointAt(landing) - takeoff;
                toLanding.y = 0f;
                Vector3 landsAt = takeoff + heading * Vector3.Dot(toLanding, heading);
                float sideways = Vector3.Dot(landsAt - PointAt(landing), rights[LoopMath.Wrap(landing, count)]);
                float room = Mathf.Max(MinLandingRoom, sideways >= 0f ? RoomRight(landing) : RoomLeft(landing));
                if (Mathf.Abs(sideways) > room)
                {
                    // Sideways drift grows with the square of the flight length, which grows with speed.
                    float limit = Mathf.Max(MinTakeoffSpeed, plan[i] * Mathf.Sqrt(room / Mathf.Abs(sideways)));
                    if (limit < takeoffLimits[i] - 0.1f)
                    {
                        takeoffLimits[i] = limit;
                        changed = true;
                    }
                }

                i = landing - 1;
            }

            for (int i = 0; i < count; i++)
            {
                changed |= marks[i] != airborne[i];
                airborne[i] = marks[i];
            }

            return changed;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;

        /// <summary>Signed distance from one point on the route to another, the short way round: positive = ahead.</summary>
        public float Separation(float from, float to) => Mathf.Repeat(to - from + Length * 0.5f, Length) - Length * 0.5f;
    }
}
