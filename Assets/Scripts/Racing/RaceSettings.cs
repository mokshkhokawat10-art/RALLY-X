using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>Designer-tunable race rules and timings.</summary>
    [CreateAssetMenu(menuName = "RALLY X/Race Settings", fileName = "RaceSettings")]
    public sealed class RaceSettings : ScriptableObject
    {
        [Header("Race")]
        [SerializeField, Range(1, 20)] private int laps = 3;

        [Header("Start")]
        [Tooltip("Wait on the grid before the countdown begins, seconds.")]
        [SerializeField, Min(0f)] private float gridDelay = 1f;

        [Tooltip("Number the countdown starts from.")]
        [SerializeField, Range(1, 9)] private int countdownFrom = 3;

        [Tooltip("Length of each countdown number, seconds.")]
        [SerializeField, Min(0.1f)] private float countdownStep = 1f;

        [Tooltip("How long GO stays on screen, seconds.")]
        [SerializeField, Min(0f)] private float goDisplayTime = 1f;

        [Header("Wrong Way")]
        [Tooltip("Angle between the car's heading and the track direction beyond which the car is going the wrong way, degrees.")]
        [SerializeField, Range(90f, 180f)] private float wrongWayAngle = 120f;

        [Tooltip("Angle below which the warning clears, degrees. Lower than the wrong-way angle so the warning doesn't flicker.")]
        [SerializeField, Range(0f, 90f)] private float rightWayAngle = 60f;

        [Tooltip("Forward speed needed to count as driving the wrong way, m/s. Reversing never counts.")]
        [SerializeField, Min(0f)] private float wrongWayMinSpeed = 3f;

        [Tooltip("How long the car must drive the wrong way before the warning shows, seconds. Ignores brief spins.")]
        [SerializeField, Min(0f)] private float wrongWayDelay = 1f;

        [Header("Finish")]
        [Tooltip("Time between the player crossing the finish line and the results screen, seconds.")]
        [SerializeField, Min(0f)] private float resultsDelay = 2f;

        public int Laps => laps;
        public float GridDelay => gridDelay;
        public int CountdownFrom => countdownFrom;
        public float CountdownStep => countdownStep;
        public float GoDisplayTime => goDisplayTime;
        public float WrongWayAngle => wrongWayAngle;
        public float RightWayAngle => rightWayAngle;
        public float WrongWayMinSpeed => wrongWayMinSpeed;
        public float WrongWayDelay => wrongWayDelay;
        public float ResultsDelay => resultsDelay;

        /// <summary>Time from the first countdown number to GO, seconds.</summary>
        public float CountdownDuration => countdownFrom * countdownStep;
    }
}
