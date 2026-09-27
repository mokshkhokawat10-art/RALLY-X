using UnityEngine;

namespace RallyX.Vehicle
{
    /// <summary>
    /// The car's looks while it drives: the body visibly dives under braking, squats under power and leans in corners
    /// (a spring on top of the physics, which never moves the colliders), chatters on rough ground, and the brake,
    /// reverse and head lights follow the driver's input. Visual only: nothing here changes how the car drives.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleVisuals : MonoBehaviour
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Below this speed, m/s, a car held on the brake or handbrake still shows its brake lights.
        private const float HoldSpeed = 2f;

        [Tooltip("Parent of the body's visible parts (not the colliders). It pivots about its own position.")]
        [SerializeField] private Transform body;

        [Header("Body Lean")]
        [Tooltip("Body pitch per g of braking or acceleration, degrees. Nose down when braking.")]
        [SerializeField, Min(0f)] private float pitchPerG = 2.2f;

        [Tooltip("Body roll per g of cornering, degrees. Leans to the outside of the corner.")]
        [SerializeField, Min(0f)] private float rollPerG = 2.8f;

        [Tooltip("Largest lean either way, degrees.")]
        [SerializeField, Range(0f, 15f)] private float maxLean = 5f;

        [Tooltip("Natural frequency of the lean spring, Hz. Lower feels heavier.")]
        [SerializeField, Range(0.5f, 8f)] private float leanFrequency = 2.6f;

        [Tooltip("1 = settles without overshoot. Lower rocks back past level once.")]
        [SerializeField, Range(0.1f, 1.5f)] private float leanDamping = 0.55f;

        [Header("Rough Ground")]
        [Tooltip("Body chatter at full speed on the roughest surface, metres.")]
        [SerializeField, Min(0f)] private float chatter = 0.012f;

        [Tooltip("Speed at which the chatter is full, km/h.")]
        [SerializeField, Min(1f)] private float chatterSpeedKmh = 100f;

        [Header("Lights")]
        [Tooltip("Tail lights, which brighten into brake lights.")]
        [SerializeField] private Renderer[] brakeLights = System.Array.Empty<Renderer>();
        [SerializeField] private Renderer[] reverseLights = System.Array.Empty<Renderer>();
        [SerializeField] private Renderer[] headLights = System.Array.Empty<Renderer>();

        [SerializeField, ColorUsage(false, true)] private Color tailLightColor = new Color(0.9f, 0.05f, 0.03f) * 0.8f;
        [SerializeField, ColorUsage(false, true)] private Color brakeLightColor = new Color(1f, 0.06f, 0.03f) * 5f;
        [SerializeField, ColorUsage(false, true)] private Color reverseLightColor = new Color(1f, 0.97f, 0.9f) * 3f;
        [SerializeField, ColorUsage(false, true)] private Color headLightColor = new Color(1f, 0.93f, 0.78f) * 2.2f;

        [Tooltip("Seconds for a light to come on or go off.")]
        [SerializeField, Min(0.01f)] private float lightResponse = 0.06f;

        private VehicleController controller;
        private MaterialPropertyBlock block;
        private Vector3 bodyRestPosition;
        private Quaternion bodyRestRotation = Quaternion.identity;
        private Vector2 lean;
        private Vector2 leanVelocity;
        private float brake;
        private float reverse;
        private float chatterTime;
        private bool displayMode;

        /// <summary>Current body pitch (x, nose down positive) and roll (y, left side down positive), degrees.</summary>
        public Vector2 Lean => lean;

        /// <summary>How lit the brake lights are, 0..1.</summary>
        public float BrakeLight => brake;

        /// <summary>How lit the reversing lights are, 0..1.</summary>
        public float ReverseLight => reverse;

        public Transform Body => body;

        /// <summary>Sets the parts the builder made. Lights are optional.</summary>
        public void Configure(Transform newBody, Renderer[] newBrakeLights, Renderer[] newReverseLights, Renderer[] newHeadLights)
        {
            body = newBody;
            brakeLights = newBrakeLights ?? System.Array.Empty<Renderer>();
            reverseLights = newReverseLights ?? System.Array.Empty<Renderer>();
            headLights = newHeadLights ?? System.Array.Empty<Renderer>();
        }

        /// <summary>Per-car lean character, as set by the builder.</summary>
        public void SetLean(float newPitchPerG, float newRollPerG, float newFrequency)
        {
            pitchPerG = newPitchPerG;
            rollPerG = newRollPerG;
            leanFrequency = newFrequency;
        }

        /// <summary>
        /// A car standing on display (garage, menu): level body, lights on as if parked with the engine running.
        /// Keeps working while the controller is switched off.
        /// </summary>
        public void ShowAsDisplay()
        {
            displayMode = true;
            lean = leanVelocity = Vector2.zero;
            brake = 0f;
            reverse = 0f;
            ApplyPose(0f);
            ApplyLights();
        }

        private void Awake()
        {
            controller = GetComponent<VehicleController>();
            if (body != null)
            {
                bodyRestPosition = body.localPosition;
                bodyRestRotation = body.localRotation;
            }
        }

        private void Start() => ApplyLights();

        private void LateUpdate()
        {
            if (displayMode || controller == null || !controller.isActiveAndEnabled)
                return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            UpdateLean(deltaTime);
            ApplyPose(deltaTime);
            UpdateLights(deltaTime);
        }

        private void UpdateLean(float deltaTime)
        {
            // Braking (negative forward acceleration) dives the nose; cornering leans to the outside.
            Vector3 acceleration = controller.LocalAcceleration / Physics.gravity.magnitude;
            var target = new Vector2(
                Mathf.Clamp(-acceleration.z * pitchPerG, -maxLean, maxLean),
                Mathf.Clamp(acceleration.x * rollPerG, -maxLean, maxLean));
            if (!controller.IsGrounded)
                target = Vector2.zero;

            // A damped spring, stepped in small pieces so a long frame can't make it overshoot wildly.
            float omega = 2f * Mathf.PI * leanFrequency;
            for (float left = deltaTime; left > 0f; left -= 0.01f)
            {
                float step = Mathf.Min(left, 0.01f);
                Vector2 accel = (target - lean) * (omega * omega) - leanVelocity * (2f * leanDamping * omega);
                leanVelocity += accel * step;
                lean += leanVelocity * step;
            }

            lean.x = Mathf.Clamp(lean.x, -maxLean * 1.5f, maxLean * 1.5f);
            lean.y = Mathf.Clamp(lean.y, -maxLean * 1.5f, maxLean * 1.5f);
        }

        private void ApplyPose(float deltaTime)
        {
            if (body == null)
                return;

            Vector3 offset = Vector3.zero;
            if (!displayMode && chatter > 0f && controller.IsGrounded)
            {
                chatterTime += deltaTime * (8f + Mathf.Abs(controller.ForwardSpeed) * 1.5f);
                float speedFactor = Mathf.Clamp01(controller.SpeedKmh / chatterSpeedKmh);
                float amount = chatter * speedFactor * AverageRoughness();
                offset.y = (Mathf.PerlinNoise(chatterTime, 0.37f) - 0.5f) * 2f * amount;
            }

            body.localPosition = bodyRestPosition + offset;
            body.localRotation = bodyRestRotation * Quaternion.Euler(lean.x, 0f, lean.y);
        }

        private float AverageRoughness()
        {
            // Indexed, not foreach: the wheels are an interface list, and enumerating one allocates every frame.
            float sum = 0f;
            int count = 0;
            var wheels = controller.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                var wheel = wheels[i];
                if (!wheel.IsGrounded || wheel.SurfaceType == null)
                    continue;
                sum += wheel.SurfaceType.Roughness;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private void UpdateLights(float deltaTime)
        {
            var input = controller.AppliedInput;
            float speed = controller.ForwardSpeed;

            // The brake pedal brakes when rolling forwards; rolling backwards the throttle does. Held still, the car shows
            // its brake lights like one waiting at a line.
            bool braking = (input.Brake > 0.05f && !controller.IsReversing && speed > -0.5f)
                           || (input.Throttle > 0.05f && speed < -0.5f)
                           || (input.Handbrake && Mathf.Abs(speed) < HoldSpeed);
            float blend = 1f - Mathf.Exp(-deltaTime / lightResponse);
            brake = Mathf.Lerp(brake, braking ? 1f : 0f, blend);
            reverse = Mathf.Lerp(reverse, controller.IsReversing ? 1f : 0f, blend);
            ApplyLights();
        }

        private void ApplyLights()
        {
            block ??= new MaterialPropertyBlock();
            SetEmission(brakeLights, Color.Lerp(tailLightColor, brakeLightColor, brake));
            SetEmission(reverseLights, reverseLightColor * reverse);
            SetEmission(headLights, headLightColor);
        }

        private void SetEmission(Renderer[] renderers, Color color)
        {
            foreach (var lightRenderer in renderers)
            {
                if (lightRenderer == null)
                    continue;
                lightRenderer.GetPropertyBlock(block);
                block.SetColor(EmissionColor, color);
                lightRenderer.SetPropertyBlock(block);
            }
        }
    }
}
