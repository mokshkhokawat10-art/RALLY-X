using RallyX.Tracks;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Tests
{
    /// <summary>
    /// Test driver that follows a TrackPath centreline: pure-pursuit steering and a speed plan that slows for
    /// corners in time. Deliberately cautious; it proves a track can be driven, not how fast.
    /// </summary>
    public sealed class ScriptedDriver
    {
        private const float CornerGrip = 5f;
        private const float Braking = 5f;
        private const float Wheelbase = 2.6f;

        private readonly VehicleController car;
        private readonly TrackPath path;
        private readonly float[] targetSpeeds;
        private readonly float spacing;

        /// <param name="topSpeed">Speed the driver never plans to exceed, m/s.</param>
        public ScriptedDriver(VehicleController car, TrackPath path, float topSpeed = 28f)
        {
            this.car = car;
            this.path = path;
            spacing = path.Length / path.Count;
            targetSpeeds = SpeedPlan(path, topSpeed);
            Distance = path.FindClosestDistance(car.Body.position);
        }

        /// <summary>Distance along the path of the car's nearest centreline point, as of the last <see cref="Steer"/>.</summary>
        public float Distance { get; private set; }

        /// <summary>Works out this step's input from where the car is now and hands it to the car.</summary>
        public void Steer()
        {
            Vector3 position = car.Body.position;
            float speed = car.Body.linearVelocity.magnitude;
            Distance = path.FindClosestDistance(position, Distance, 30f);

            // Pure pursuit on the centreline, with a look-ahead that grows with speed.
            float lookAhead = Mathf.Clamp(4f + speed * 0.6f, 6f, 22f);
            Vector3 toTarget = Flat(path.EvaluatePosition(Distance + lookAhead) - position);
            Vector3 heading = Flat(car.Body.rotation * Vector3.forward).normalized;
            float alpha = Vector3.SignedAngle(heading, toTarget, Vector3.up) * Mathf.Deg2Rad;
            float wheelAngle = Mathf.Atan2(2f * Wheelbase * Mathf.Sin(alpha), toTarget.magnitude) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(wheelAngle / car.Config.MaxSteerAngle, -1f, 1f);

            int ahead = Mathf.RoundToInt(Mathf.Repeat(Distance + speed * 0.5f, path.Length) / spacing) % path.Count;
            float speedError = targetSpeeds[ahead] - speed;
            float throttle = speedError > 0f ? Mathf.Clamp01(speedError / 2f) : 0f;
            float brake = speedError < -1f ? Mathf.Clamp01(-speedError / 4f) : 0f;
            car.SetInput(new VehicleInputState(throttle, brake, steer, false));
        }

        /// <summary>Planned speed along the lap: limited by corner radius, then braking in time for each corner.</summary>
        private static float[] SpeedPlan(TrackPath path, float topSpeed)
        {
            int count = path.Count;
            float spacing = path.Length / count;
            var speeds = new float[count];
            for (int i = 0; i < count; i++)
            {
                float radius = CircleRadius(Flat(path.GetPoint(i - 4)), Flat(path.GetPoint(i)), Flat(path.GetPoint(i + 4)));
                speeds[i] = Mathf.Min(topSpeed, Mathf.Sqrt(CornerGrip * radius));
            }

            // Walk backwards twice round the loop so corners just after the start line are braked for too.
            for (int pass = 0; pass < count * 2; pass++)
            {
                int i = count - 1 - pass % count;
                float next = speeds[(i + 1) % count];
                speeds[i] = Mathf.Min(speeds[i], Mathf.Sqrt(next * next + 2f * Braking * spacing));
            }

            return speeds;
        }

        private static float CircleRadius(Vector3 a, Vector3 b, Vector3 c)
        {
            float twiceArea = Vector3.Cross(b - a, c - a).magnitude;
            return twiceArea < 1e-4f
                ? float.PositiveInfinity
                : Vector3.Distance(a, b) * Vector3.Distance(b, c) * Vector3.Distance(c, a) / (2f * twiceArea);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
