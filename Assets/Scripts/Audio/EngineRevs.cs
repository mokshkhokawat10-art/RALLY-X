using System;
using UnityEngine;

namespace RallyX.Audio
{
    /// <summary>
    /// A sound-only engine: revs and a gear worked out from how fast the car goes, through the gearing in a
    /// <see cref="VehicleAudioConfig"/>. It changes up near the redline and down (with a blip) as the car slows, cuts the
    /// throttle for a moment on each change, holds launch revs while pulling away, flares with wheelspin, revs freely in
    /// the air or when held on the grid, and bounces off the limiter. Nothing here changes how the car drives.
    /// </summary>
    public sealed class EngineRevs
    {
        private float shiftTimeLeft;

        /// <summary>A gear change: the old gear and the new one (-1 = reverse).</summary>
        public event Action<int, int> GearChanged;

        public float Rpm { get; private set; }

        /// <summary>1 and up going forwards, -1 in reverse.</summary>
        public int Gear { get; private set; } = 1;

        /// <summary>How hard the engine is working, 0..1: follows the throttle and drops while changing gear.</summary>
        public float Load { get; private set; }

        public bool Shifting => shiftTimeLeft > 0f;

        public void Reset(VehicleAudioConfig config)
        {
            Rpm = config.IdleRpm;
            Gear = 1;
            Load = 0f;
            shiftTimeLeft = 0f;
        }

        /// <param name="forwardSpeed">Speed along the car's heading, m/s (negative backwards).</param>
        /// <param name="topSpeed">The car's top speed, m/s.</param>
        /// <param name="reverseTopSpeed">The car's top reverse speed, m/s.</param>
        /// <param name="throttle">Throttle 0..1 (the brake when reversing).</param>
        /// <param name="freeRevving">True when the wheels don't hold the engine back: in the air, or held on the grid.</param>
        /// <param name="wheelSpin">How hard the driven wheels spin, 0..1.</param>
        /// <param name="reversing">True while reverse is engaged.</param>
        public void Step(VehicleAudioConfig config, float forwardSpeed, float topSpeed, float reverseTopSpeed, float throttle,
            bool freeRevving, float wheelSpin, bool reversing, float deltaTime)
        {
            float idle = config.IdleRpm;
            float redline = config.RedlineRpm;
            throttle = Mathf.Clamp01(throttle);
            shiftTimeLeft -= deltaTime;

            float target;
            if (reversing)
            {
                SetGear(-1, config);
                target = Mathf.Lerp(idle, redline * 0.75f, Mathf.Abs(forwardSpeed) / Mathf.Max(reverseTopSpeed, 0.1f));
            }
            else if (freeRevving)
            {
                if (Gear < 1)
                    SetGear(1, config);
                target = Mathf.Lerp(idle, redline, throttle);
            }
            else
            {
                if (Gear < 1)
                    SetGear(1, config);
                target = DrivenRevs(config, Mathf.Max(0f, forwardSpeed) / Mathf.Max(topSpeed, 0.1f), throttle);
                target += (redline - target) * Mathf.Clamp01(wheelSpin) * 0.8f;
            }

            target = Mathf.Clamp(target, idle, redline);
            float rate = target > Rpm ? config.RpmRiseRate : config.RpmFallRate;
            if (Shifting)
                rate *= 2f;
            Rpm = Mathf.MoveTowards(Rpm, target, rate * deltaTime);

            // The limiter cuts in just short of the redline and knocks the revs back.
            if (throttle > 0.5f && Rpm >= redline * 0.995f)
                Rpm = redline - config.LimiterDrop * UnityEngine.Random.Range(0.4f, 1f);

            Load = Mathf.MoveTowards(Load, Shifting ? 0f : throttle, deltaTime * 8f);
        }

        /// <summary>Revs with the wheels driving the engine, changing gear on the way.</summary>
        private float DrivenRevs(VehicleAudioConfig config, float speedShare, float throttle)
        {
            var gears = config.GearTopSpeeds;
            if (gears == null || gears.Length == 0)
                return Mathf.Lerp(config.IdleRpm, config.RedlineRpm, speedShare);

            Gear = Mathf.Clamp(Gear, 1, gears.Length);
            if (!Shifting)
            {
                if (Gear < gears.Length && speedShare > gears[Gear - 1] * config.UpshiftAt)
                    SetGear(Gear + 1, config);
                else if (Gear > 1 && speedShare / Mathf.Max(gears[Gear - 2], 0.01f) < config.DownshiftAt)
                    SetGear(Gear - 1, config);
            }

            float wheelRevs = config.RedlineRpm * speedShare / Mathf.Max(gears[Gear - 1], 0.01f);

            // Pulling away the clutch slips, so the engine holds launch revs until the car catches up with them.
            float floor = Mathf.Lerp(config.IdleRpm, config.LaunchRpm, throttle);
            return Mathf.Max(wheelRevs, floor);
        }

        private void SetGear(int gear, VehicleAudioConfig config)
        {
            if (gear == Gear)
                return;

            int from = Gear;
            Gear = gear;
            shiftTimeLeft = from > 0 && gear > 0 ? config.ShiftTime : 0f;
            GearChanged?.Invoke(from, gear);
        }
    }
}
