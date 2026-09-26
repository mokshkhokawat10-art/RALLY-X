using System;
using UnityEngine.InputSystem;

namespace RallyX.Core
{
    /// <summary>
    /// Single source of truth for RALLY X input bindings (keyboard + gamepad): the Driving map, enabled with
    /// <see cref="Enable"/>, and the Menu map for menus and the garage, enabled with <see cref="EnableMenu"/>.
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

        public InputActionMap Menu { get; }

        public InputAction MenuUp { get; }
        public InputAction MenuDown { get; }
        public InputAction MenuLeft { get; }
        public InputAction MenuRight { get; }

        /// <summary>Confirms the highlighted menu item.</summary>
        public InputAction Submit { get; }

        /// <summary>Leaves the current menu.</summary>
        public InputAction Back { get; }

        /// <summary>A screen's second action, e.g. opening upgrades in the garage.</summary>
        public InputAction Secondary { get; }

        /// <summary>-1 .. 1: turns the car on the garage turntable.</summary>
        public InputAction RotateView { get; }

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

            Menu = new InputActionMap("Menu");
            MenuUp = AddMenuButton("Up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
            MenuDown = AddMenuButton("Down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");
            MenuLeft = AddMenuButton("Left", "<Keyboard>/leftArrow", "<Keyboard>/a", "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left",
                "<Gamepad>/leftShoulder");
            MenuRight = AddMenuButton("Right", "<Keyboard>/rightArrow", "<Keyboard>/d", "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right",
                "<Gamepad>/rightShoulder");
            Submit = AddMenuButton("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Back = AddMenuButton("Back", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            Secondary = AddMenuButton("Secondary", "<Keyboard>/u", "<Gamepad>/buttonNorth");

            RotateView = Menu.AddAction("RotateView", InputActionType.Value, expectedControlLayout: "Axis");
            RotateView.AddBinding("<Gamepad>/rightStick/x", processors: "axisDeadzone");
            RotateView.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");
        }

        public void Enable() => Driving.Enable();

        public void Disable() => Driving.Disable();

        public void EnableMenu() => Menu.Enable();

        public void DisableMenu() => Menu.Disable();

        public void Dispose()
        {
            Driving.Dispose();
            Menu.Dispose();
        }

        private InputAction AddMenuButton(string name, params string[] bindings)
        {
            var action = Menu.AddAction(name, InputActionType.Button);
            foreach (string binding in bindings)
                action.AddBinding(binding);
            return action;
        }
    }
}
