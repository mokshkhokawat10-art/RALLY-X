using System;
using UnityEngine.InputSystem;

namespace RallyX.Core
{
    /// <summary>
    /// Single source of truth for RALLY X input bindings (keyboard + gamepad).
    /// Each owner creates its own instance and must Dispose it when done.
    /// </summary>
    public sealed class RallyInputActions : IDisposable
    {
        public InputActionMap Driving { get; }

        /// <summary>0..1</summary>
        public InputAction Throttle { get; }

        /// <summary>0..1. Also drives reverse once the vehicle has stopped.</summary>
        public InputAction Brake { get; }

        /// <summary>-1 (left) .. 1 (right)</summary>
        public InputAction Steer { get; }

        public InputAction Handbrake { get; }

        /// <summary>Puts the vehicle back on its wheels.</summary>
        public InputAction ResetVehicle { get; }

        public InputAction Pause { get; }

        /// <summary>Starts the race again from the grid.</summary>
        public InputAction RestartRace { get; }

        public RallyInputActions()
        {
            Driving = new InputActionMap("Driving");

            // Value actions resolve keyboard/gamepad conflicts by picking the most actuated control.
            Throttle = Driving.AddAction("Throttle", InputActionType.Value, expectedControlLayout: "Axis");
            Throttle.AddBinding("<Gamepad>/rightTrigger");
            Throttle.AddBinding("<Keyboard>/w");
            Throttle.AddBinding("<Keyboard>/upArrow");

            Brake = Driving.AddAction("Brake", InputActionType.Value, expectedControlLayout: "Axis");
            Brake.AddBinding("<Gamepad>/leftTrigger");
            Brake.AddBinding("<Keyboard>/s");
            Brake.AddBinding("<Keyboard>/downArrow");

            Steer = Driving.AddAction("Steer", InputActionType.Value, expectedControlLayout: "Axis");
            // Stick deadzones are applied to the whole stick, not its x child, so add one here.
            Steer.AddBinding("<Gamepad>/leftStick/x", processors: "axisDeadzone");
            Steer.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            Steer.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");

            Handbrake = Driving.AddAction("Handbrake", InputActionType.Button);
            Handbrake.AddBinding("<Keyboard>/space");
            Handbrake.AddBinding("<Gamepad>/buttonSouth");

            ResetVehicle = Driving.AddAction("ResetVehicle", InputActionType.Button);
            ResetVehicle.AddBinding("<Keyboard>/r");
            ResetVehicle.AddBinding("<Gamepad>/buttonNorth");

            Pause = Driving.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");

            RestartRace = Driving.AddAction("RestartRace", InputActionType.Button);
            RestartRace.AddBinding("<Keyboard>/backspace");
            RestartRace.AddBinding("<Gamepad>/select");
        }

        public void Enable() => Driving.Enable();

        public void Disable() => Driving.Disable();

        public void Dispose() => Driving.Dispose();
    }
}
