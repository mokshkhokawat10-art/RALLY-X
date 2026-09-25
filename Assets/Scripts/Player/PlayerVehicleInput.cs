using RallyX.Core;
using RallyX.Vehicle;
using UnityEngine;

namespace RallyX.Player
{
    /// <summary>Feeds keyboard/gamepad input to the VehicleController on the same GameObject.</summary>
    [RequireComponent(typeof(VehicleController))]
    [DisallowMultipleComponent]
    public sealed class PlayerVehicleInput : MonoBehaviour
    {
        private VehicleController vehicle;
        private RallyInputActions actions;

        private void Awake()
        {
            vehicle = GetComponent<VehicleController>();
            actions = new RallyInputActions();
        }

        private void OnEnable() => actions.Enable();

        private void OnDisable()
        {
            actions.Disable();
            vehicle.SetInput(default);
        }

        private void OnDestroy() => actions.Dispose();

        private void Update()
        {
            vehicle.SetInput(new VehicleInputState(
                actions.Throttle.ReadValue<float>(),
                actions.Brake.ReadValue<float>(),
                actions.Steer.ReadValue<float>(),
                actions.Handbrake.IsPressed()));

            if (actions.ResetVehicle.WasPressedThisFrame())
                vehicle.ResetVehicle();
        }
    }
}
