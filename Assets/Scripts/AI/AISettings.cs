using UnityEngine;

namespace RallyX.AI
{
    /// <summary>
    /// Designer-tunable behaviour shared by every AI driver, and how each difficulty scales their pace. Individual pace
    /// and style are in <see cref="AIDriverProfile"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "RALLY X/AI Settings", fileName = "AISettings")]
    public sealed class AISettings : ScriptableObject
    {
        [Header("Racing Line")]
        [Tooltip("Closest the racing line brings the car's centre to the road edge, metres.")]
        [SerializeField, Min(0.5f)] private float edgeMargin = 1.8f;

        [Tooltip("Smoothing passes for the racing line. More passes = a straighter line that cuts corners harder.")]
        [SerializeField, Range(0, 400)] private int lineSmoothing = 150;

        [Header("Steering")]
        [Tooltip("Distance ahead the driver aims at when stopped, metres.")]
        [SerializeField, Min(1f)] private float lookAheadBase = 5f;

        [Tooltip("Extra aiming distance per m/s of speed, seconds.")]
        [SerializeField, Min(0f)] private float lookAheadPerSpeed = 0.55f;

        [Tooltip("Longest aiming distance, metres.")]
        [SerializeField, Min(1f)] private float lookAheadMax = 24f;

        [Tooltip("How much of a slide the driver steers into. 1 = front wheels turned to where the car is actually going.")]
        [SerializeField, Range(0f, 1f)] private float countersteer = 1f;

        [Tooltip("Slide of the tail beyond which the driver eases off the throttle, degrees. Fully off at twice this.")]
        [SerializeField, Range(2f, 30f)] private float slideAngle = 8f;

        [Header("Speed")]
        [Tooltip("How far ahead, in seconds of travel, the driver reads its planned speed. Like a reaction time.")]
        [SerializeField, Min(0f)] private float speedLookAheadTime = 0.4f;

        [Tooltip("Throttle per m/s below the planned speed.")]
        [SerializeField, Min(0.05f)] private float throttleGain = 0.5f;

        [Tooltip("Brake per m/s above the planned speed.")]
        [SerializeField, Min(0.05f)] private float brakeGain = 0.3f;

        [Tooltip("Speed above the plan tolerated before braking, m/s.")]
        [SerializeField, Min(0f)] private float brakeDeadband = 0.8f;

        [Header("Traffic")]
        [Tooltip("How far ahead, in seconds of travel, the driver reacts to cars in its way.")]
        [SerializeField, Min(0.5f)] private float trafficLookAheadTime = 1.6f;

        [Tooltip("Shortest distance ahead the driver watches for cars, metres.")]
        [SerializeField, Min(5f)] private float trafficLookAheadMin = 14f;

        [Tooltip("Side-to-side distance between car centres the driver keeps when passing or alongside, metres.")]
        [SerializeField, Min(1f)] private float passingSeparation = 2.7f;

        [Tooltip("How fast the driver moves sideways to pass or make room, m/s.")]
        [SerializeField, Min(0.5f)] private float sideStepSpeed = 3f;

        [Tooltip("Gap kept behind a car that can't be passed, metres.")]
        [SerializeField, Min(2f)] private float followDistance = 9f;

        [Tooltip("Time before a jump when the driver lines up on the racing line and stops passing, seconds. " +
                 "A car flies straight on, so taking off off-line can land it off the road.")]
        [SerializeField, Min(0f)] private float jumpCaution = 1.5f;

        [Header("Recovery")]
        [Tooltip("Below this speed the car counts as stuck, m/s.")]
        [SerializeField, Min(0.1f)] private float stuckSpeed = 1.5f;

        [Tooltip("Time stuck before the driver reverses out, seconds.")]
        [SerializeField, Min(0.5f)] private float stuckTime = 2f;

        [Tooltip("Longest each reversing attempt lasts, seconds.")]
        [SerializeField, Min(0.2f)] private float reverseTime = 3f;

        [Tooltip("How far the driver backs straight out before swinging the nose towards the line, metres. About a car length clears what the nose was against.")]
        [SerializeField, Min(0f)] private float backOutDistance = 5f;

        [Tooltip("Reversing attempts before the car is put back on the track.")]
        [SerializeField, Range(0, 5)] private int reverseAttempts = 2;

        [Tooltip("Time on its side or roof before the car is put back on the track, seconds.")]
        [SerializeField, Min(0.2f)] private float flippedTime = 1.5f;

        [Tooltip("Distance beyond the road edge at which the driver counts as lost, metres.")]
        [SerializeField, Min(1f)] private float offRoadDistance = 12f;

        [Tooltip("Time lost off the road before the car is put back on the track, seconds.")]
        [SerializeField, Min(0.5f)] private float offRoadTime = 4f;

        [Tooltip("Time going the wrong way before the car is put back on the track, seconds.")]
        [SerializeField, Min(0.5f)] private float wrongWayTime = 3f;

        [Header("Difficulty")]
        [Tooltip("Easy: drivers corner, brake and reach top speed well short of their best.")]
        [SerializeField] private AIDifficultyLevel easy = new AIDifficultyLevel(0.8f, 0.8f, 0.9f);

        [Tooltip("Normal: every driver at the pace set in their profile.")]
        [SerializeField] private AIDifficultyLevel normal = new AIDifficultyLevel(1f, 1f, 1f);

        [Tooltip("Hard: drivers carry more speed through corners, brake later and use more of the car's top speed.")]
        [SerializeField] private AIDifficultyLevel hard = new AIDifficultyLevel(1.15f, 1.15f, 1.03f);

        public float EdgeMargin => edgeMargin;
        public int LineSmoothing => lineSmoothing;
        public float LookAheadBase => lookAheadBase;
        public float LookAheadPerSpeed => lookAheadPerSpeed;
        public float LookAheadMax => lookAheadMax;
        public float Countersteer => countersteer;
        public float SlideAngle => slideAngle;
        public float SpeedLookAheadTime => speedLookAheadTime;
        public float ThrottleGain => throttleGain;
        public float BrakeGain => brakeGain;
        public float BrakeDeadband => brakeDeadband;
        public float TrafficLookAheadTime => trafficLookAheadTime;
        public float TrafficLookAheadMin => trafficLookAheadMin;
        public float PassingSeparation => passingSeparation;
        public float SideStepSpeed => sideStepSpeed;
        public float FollowDistance => followDistance;
        public float JumpCaution => jumpCaution;
        public float StuckSpeed => stuckSpeed;
        public float StuckTime => stuckTime;
        public float ReverseTime => reverseTime;
        public float BackOutDistance => backOutDistance;
        public int ReverseAttempts => reverseAttempts;
        public float FlippedTime => flippedTime;
        public float OffRoadDistance => offRoadDistance;
        public float OffRoadTime => offRoadTime;
        public float WrongWayTime => wrongWayTime;

        public AIDifficultyLevel GetDifficulty(AIDifficulty difficulty) => difficulty switch
        {
            AIDifficulty.Easy => easy,
            AIDifficulty.Hard => hard,
            _ => normal,
        };
    }
}
