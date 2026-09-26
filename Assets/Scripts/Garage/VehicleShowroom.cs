using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Garage
{
    /// <summary>
    /// Shows a car on a turntable: the car's own prefab with its physics switched off, turning slowly on its own and
    /// by hand (stick, keys or mouse drag). A locked car is shown as a dark silhouette.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleShowroom : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Rotates with the car on it. The car sits at this transform's position.")]
        [SerializeField] private Transform turntable;

        [Tooltip("Turning speed while nobody is turning the car, degrees per second.")]
        [SerializeField] private float autoRotateSpeed = 14f;

        [Tooltip("Seconds after the player last turned the car before it turns on its own again.")]
        [SerializeField, Min(0f)] private float autoRotateDelay = 2.5f;

        [Tooltip("Colour a locked car is shown in.")]
        [SerializeField] private Color lockedColor = new Color(0.035f, 0.04f, 0.05f);

        private MaterialPropertyBlock block;
        private float idleTime;

        /// <summary>The car on the turntable, or null.</summary>
        public VehicleDefinition Shown { get; private set; }

        /// <summary>The model on the turntable, or null.</summary>
        public GameObject Model { get; private set; }

        public bool ShownLocked { get; private set; }

        public Transform Turntable => turntable;

        public void Configure(Transform newTurntable) => turntable = newTurntable;

        /// <summary>Puts a car on the turntable in place of the one there, keeping the turntable's angle.</summary>
        public void Show(VehicleDefinition vehicle, bool locked)
        {
            if (vehicle == Shown && locked == ShownLocked && Model != null)
                return;

            if (Model != null)
                Destroy(Model);

            Shown = vehicle;
            ShownLocked = locked;
            Model = null;
            if (vehicle == null || vehicle.Prefab == null || turntable == null)
                return;

            var car = Instantiate(vehicle.Prefab, turntable);
            car.name = $"Display_{vehicle.Id}";
            car.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            MakeDisplayOnly(car);
            Model = car.gameObject;
            if (locked)
                Tint(Model, lockedColor);
        }

        /// <summary>Turns the car by hand, degrees (positive = clockwise seen from above).</summary>
        public void Rotate(float degrees)
        {
            if (turntable == null || Mathf.Approximately(degrees, 0f))
                return;

            turntable.Rotate(0f, degrees, 0f, Space.World);
            idleTime = 0f;
        }

        // Turn from the start; the pause only follows turning by hand.
        private void Awake() => idleTime = autoRotateDelay;

        private void Update()
        {
            idleTime += Time.unscaledDeltaTime;
            if (turntable != null && idleTime >= autoRotateDelay)
                turntable.Rotate(0f, autoRotateSpeed * Time.unscaledDeltaTime, 0f, Space.World);
        }

        /// <summary>A car that stands still for display: no driving, no physics, no collisions.</summary>
        private static void MakeDisplayOnly(VehicleController car)
        {
            car.enabled = false;
            var body = car.Body != null ? car.Body : car.GetComponent<Rigidbody>();
            if (body == null)
                return;

            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        private void Tint(GameObject model, Color color)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
