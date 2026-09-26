using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>Marks a vehicle as a racer. The player's car carries one, and so will AI cars.</summary>
    [RequireComponent(typeof(VehicleController))]
    [DisallowMultipleComponent]
    public sealed class RaceParticipant : MonoBehaviour
    {
        [Tooltip("Name shown in positions and results.")]
        [SerializeField] private string displayName = "Driver";

        [Tooltip("The HUD and results follow the player's racer.")]
        [SerializeField] private bool isPlayer;

        private VehicleController vehicle;

        public string DisplayName => displayName;
        public bool IsPlayer => isPlayer;
        public VehicleController Vehicle => vehicle != null ? vehicle : vehicle = GetComponent<VehicleController>();

        /// <summary>Progress in the current race; null until a RaceManager has added this racer.</summary>
        public RaceTracker Tracker { get; internal set; }

        /// <summary>Race position, 1 = leading. 0 until a RaceManager has added this racer.</summary>
        public int Position { get; internal set; }

        /// <summary>Starting grid slot, 0 = pole position.</summary>
        public int GridSlot { get; internal set; }

        public void Configure(string newDisplayName, bool player)
        {
            displayName = newDisplayName;
            isPlayer = player;
        }
    }
}
