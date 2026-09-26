using UnityEngine;

namespace RallyX.Tracks
{
    /// <summary>Starting grid: slot 0 is pole position. Slots face the direction of travel.</summary>
    [DisallowMultipleComponent]
    public sealed class TrackSpawnGrid : MonoBehaviour
    {
        [SerializeField] private Transform[] slots = System.Array.Empty<Transform>();

        public int SlotCount => slots.Length;

        public Transform GetSlot(int index) => slots[index];

        public void SetSlots(Transform[] newSlots) => slots = newSlots;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            foreach (var slot in slots)
            {
                if (slot == null)
                    continue;
                Gizmos.matrix = slot.localToWorldMatrix;
                Gizmos.DrawWireCube(Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 4.2f));
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
