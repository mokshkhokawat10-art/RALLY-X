using UnityEngine;

namespace RallyX.Cameras
{
    /// <summary>Designer-tunable chase camera behaviour. Read live, so edits in Play Mode apply immediately.</summary>
    [CreateAssetMenu(menuName = "RALLY X/Chase Camera Config", fileName = "ChaseCameraConfig")]
    public sealed class ChaseCameraConfig : ScriptableObject
    {
        [Header("Framing")]
        [Tooltip("Horizontal distance behind the car at standstill, metres.")]
        [SerializeField, Min(1f)] private float distance = 6.5f;

        [Tooltip("Extra distance added at full speed, metres. Adds a sense of speed.")]
        [SerializeField, Min(0f)] private float extraDistanceAtSpeed = 1f;

        [Tooltip("Camera height above the car's base, metres.")]
        [SerializeField, Min(0f)] private float height = 2.4f;

        [Tooltip("Height above the car's base that the camera aims at, metres.")]
        [SerializeField, Min(0f)] private float lookHeight = 1.1f;

        [Tooltip("How far ahead of the car the camera aims, metres. Puts more road in view.")]
        [SerializeField, Min(0f)] private float lookAhead = 2f;

        [Header("Smoothing")]
        [Tooltip("How quickly the camera swings behind the car's heading. Higher = tighter.")]
        [SerializeField, Min(0.1f)] private float rotationSharpness = 5f;

        [Tooltip("How quickly the camera follows the car's height on the ground. Lower soaks up more bumps.")]
        [SerializeField, Min(0.1f)] private float heightSharpness = 6f;

        [Tooltip("Height follow while airborne. Lower lets jumps rise visibly in frame.")]
        [SerializeField, Min(0.1f)] private float airborneHeightSharpness = 2.5f;

        [Tooltip("Car movement in one frame beyond which the camera snaps instead of swinging (e.g. a reset), metres.")]
        [SerializeField, Min(1f)] private float teleportDistance = 25f;

        [Header("Field of View")]
        [SerializeField, Range(30f, 100f)] private float baseFieldOfView = 60f;
        [SerializeField, Range(30f, 110f)] private float maxFieldOfView = 72f;

        [Tooltip("Speed at which FOV and extra distance reach their maximum, km/h.")]
        [SerializeField, Min(1f)] private float speedForMaxEffectKmh = 160f;

        [Tooltip("FOV blend over normalised speed (0 = standstill, 1 = speed for max effect).")]
        [SerializeField] private AnimationCurve fieldOfViewBySpeed =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 0.2f), new Keyframe(1f, 1f));

        [SerializeField, Min(0.1f)] private float fieldOfViewSharpness = 3f;

        [Header("Drift")]
        [Tooltip("Fraction of the car's slip angle the camera swings towards the direction of travel.")]
        [SerializeField, Range(0f, 1f)] private float driftSwing = 0.3f;

        [Tooltip("Largest drift swing, degrees.")]
        [SerializeField, Range(0f, 45f)] private float maxDriftSwing = 18f;

        [Header("Shake")]
        [Tooltip("Rotation shake at full trauma, degrees.")]
        [SerializeField, Min(0f)] private float maxShakeAngle = 1.5f;

        [Tooltip("Position shake at full trauma, metres.")]
        [SerializeField, Min(0f)] private float maxShakeOffset = 0.08f;

        [SerializeField, Min(0.1f)] private float shakeFrequency = 18f;

        [Tooltip("Trauma lost per second. Shake strength is trauma squared, so it fades quickly at the end.")]
        [SerializeField, Min(0.01f)] private float traumaDecay = 1.6f;

        [Tooltip("Minimum air time for a landing to shake the camera, seconds.")]
        [SerializeField, Min(0f)] private float minLandingAirTime = 0.3f;

        [Tooltip("Landing speed that produces the full landing shake, m/s.")]
        [SerializeField, Min(0.1f)] private float landingSpeedForFullShake = 10f;

        [Tooltip("Trauma added by the hardest landing (0..1).")]
        [SerializeField, Range(0f, 1f)] private float landingShake = 0.55f;

        [Tooltip("Collision impulse below which the camera does not shake, N·s.")]
        [SerializeField, Min(0f)] private float minCollisionImpulse = 3000f;

        [Tooltip("Collision impulse that produces the full collision shake, N·s.")]
        [SerializeField, Min(1f)] private float maxCollisionImpulse = 20000f;

        [Tooltip("Trauma added by the hardest collision (0..1).")]
        [SerializeField, Range(0f, 1f)] private float collisionShake = 0.7f;

        [Header("Obstacles")]
        [Tooltip("Radius kept clear around the camera, metres. Keep above the near clip plane.")]
        [SerializeField, Min(0.05f)] private float collisionRadius = 0.3f;

        [Tooltip("Closest the camera may be pulled towards the car's centre, metres. Keeps it out of the car body.")]
        [SerializeField, Min(0.5f)] private float minDistance = 3f;

        [Tooltip("How quickly the camera eases back out once an obstacle clears.")]
        [SerializeField, Min(0.1f)] private float obstacleRecoverySharpness = 4f;

        [Tooltip("Minimum height of the camera above the ground beneath it, metres.")]
        [SerializeField, Min(0f)] private float groundClearance = 0.4f;

        public float Distance => distance;
        public float ExtraDistanceAtSpeed => extraDistanceAtSpeed;
        public float Height => height;
        public float LookHeight => lookHeight;
        public float LookAhead => lookAhead;

        public float RotationSharpness => rotationSharpness;
        public float HeightSharpness => heightSharpness;
        public float AirborneHeightSharpness => airborneHeightSharpness;
        public float TeleportDistance => teleportDistance;

        public float BaseFieldOfView => baseFieldOfView;
        public float MaxFieldOfView => maxFieldOfView;
        public float SpeedForMaxEffectKmh => speedForMaxEffectKmh;
        public AnimationCurve FieldOfViewBySpeed => fieldOfViewBySpeed;
        public float FieldOfViewSharpness => fieldOfViewSharpness;

        public float DriftSwing => driftSwing;
        public float MaxDriftSwing => maxDriftSwing;

        public float MaxShakeAngle => maxShakeAngle;
        public float MaxShakeOffset => maxShakeOffset;
        public float ShakeFrequency => shakeFrequency;
        public float TraumaDecay => traumaDecay;
        public float MinLandingAirTime => minLandingAirTime;
        public float LandingSpeedForFullShake => landingSpeedForFullShake;
        public float LandingShake => landingShake;
        public float MinCollisionImpulse => minCollisionImpulse;
        public float MaxCollisionImpulse => maxCollisionImpulse;
        public float CollisionShake => collisionShake;

        public float CollisionRadius => collisionRadius;
        public float MinDistance => minDistance;
        public float ObstacleRecoverySharpness => obstacleRecoverySharpness;
        public float GroundClearance => groundClearance;
    }
}
