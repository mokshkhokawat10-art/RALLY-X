using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RallyX.Player
{
    /// <summary>
    /// Gamepad rumble for the player's car, kept subtle: a low rumble from rough ground at speed, a lighter buzz while the
    /// tyres slide or spin, and short thumps from hits and landings that die away. Follows the vibration setting in the
    /// player's profile, and stops while the game is paused (time stopped) and when the component is switched off. Does
    /// nothing without a gamepad.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    [DisallowMultipleComponent]
    public sealed class VehicleHaptics : MonoBehaviour
    {
        [Tooltip("Strength of every effect, 0..1.")]
        [SerializeField, Range(0f, 1f)] private float strength = 0.6f;

        [Tooltip("Low rumble at speed on the roughest ground.")]
        [SerializeField, Range(0f, 1f)] private float surfaceRumble = 0.25f;

        [Tooltip("Buzz while the tyres slide, spin or lock.")]
        [SerializeField, Range(0f, 1f)] private float tyreBuzz = 0.3f;

        [Tooltip("Sideways tyre speed where the slide buzz is full, m/s.")]
        [SerializeField, Min(0.1f)] private float fullSlideSpeed = 6f;

        [Tooltip("Change of speed from a hit that gives the strongest thump, m/s.")]
        [SerializeField, Min(0.1f)] private float fullImpactSpeed = 10f;

        [Tooltip("Landing speed (downwards) that gives the strongest thump, m/s.")]
        [SerializeField, Min(0.1f)] private float fullLandingSpeed = 8f;

        [Tooltip("How fast a thump dies away, per second.")]
        [SerializeField, Min(0.1f)] private float thumpDecay = 6f;

        private VehicleController vehicle;
        private Gamepad rumbling;
        private float thump;

        private void Awake() => vehicle = GetComponent<VehicleController>();

        private void OnEnable()
        {
            vehicle.Impact += OnImpact;
            vehicle.Landed += OnLanded;
        }

        private void OnDisable()
        {
            vehicle.Impact -= OnImpact;
            vehicle.Landed -= OnLanded;
            Stop();
        }

        private void Update()
        {
            var gamepad = Gamepad.current;
            var profile = PlayerProfile.Loaded;
            bool allowed = gamepad != null && Time.timeScale > 0f && (profile == null || profile.Vibration) && vehicle.isActiveAndEnabled;
            if (!allowed)
            {
                Stop();
                return;
            }

            thump = Mathf.MoveTowards(thump, 0f, thumpDecay * Time.deltaTime);
            Rumble(out float low, out float high);
            if (gamepad != rumbling)
                Stop();
            rumbling = gamepad;
            gamepad.SetMotorSpeeds(Mathf.Clamp01((low + thump) * strength), Mathf.Clamp01(high * strength));
        }

        /// <summary>The steady part: rough ground at speed (low motor) and working tyres (high motor).</summary>
        private void Rumble(out float low, out float high)
        {
            low = high = 0f;
            if (!vehicle.IsGrounded)
                return;

            float roughness = 0f;
            float slide = 0f;
            int grounded = 0;
            var wheels = vehicle.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                var wheel = wheels[i];
                if (!wheel.IsGrounded)
                    continue;

                grounded++;
                if (wheel.SurfaceType != null)
                    roughness += wheel.SurfaceType.Roughness;
                slide = Mathf.Max(slide, wheel.SidewaysSpeed / fullSlideSpeed, wheel.WheelSpin, wheel.Locked ? 0.7f : 0f);
            }

            float speed = Mathf.Clamp01(vehicle.SpeedKmh / 120f);
            low = surfaceRumble * speed * (grounded > 0 ? roughness / grounded : 0f);
            high = tyreBuzz * Mathf.Clamp01(slide);
        }

        private void OnImpact(float impulse, Vector3 point) =>
            thump = Mathf.Max(thump, Mathf.Clamp01(impulse / vehicle.Body.mass / fullImpactSpeed));

        private void OnLanded(float airTime, float downSpeed)
        {
            if (airTime > 0.2f)
                thump = Mathf.Max(thump, Mathf.Clamp01(downSpeed / fullLandingSpeed) * 0.8f);
        }

        private void Stop()
        {
            thump = 0f;
            if (rumbling == null)
                return;

            rumbling.SetMotorSpeeds(0f, 0f);
            rumbling = null;
        }
    }
}
