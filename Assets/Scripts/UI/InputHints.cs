using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RallyX.UI
{
    /// <summary>What a control hint is for.</summary>
    public enum HintAction
    {
        /// <summary>Up and down through a list.</summary>
        Choose,

        /// <summary>Left and right between cars or tracks.</summary>
        Browse,

        /// <summary>Left and right to change a value.</summary>
        Change,
        Confirm,
        Back,

        /// <summary>The garage's upgrades.</summary>
        Secondary,

        /// <summary>Turning the car on the turntable.</summary>
        Rotate,
        Pause,
        Restart,
    }

    /// <summary>The kind of device the player last used, so hints name the right keys or buttons.</summary>
    public enum InputScheme
    {
        KeyboardMouse,
        Gamepad,
    }

    /// <summary>
    /// Which device the player is using (the last one pressed or moved: keyboard and mouse, or a gamepad) and the names of
    /// the keys and buttons for each <see cref="HintAction"/> on it, matching the bindings in RallyInputActions. Watched
    /// by a component on the persistent UI host (<see cref="LoadingScreen"/>'s), so every screen's hints follow the device.
    /// </summary>
    public static class InputHints
    {
        private const float StickThreshold = 0.5f;

        public static InputScheme Scheme { get; private set; } = InputScheme.KeyboardMouse;

        /// <summary>The player switched between keyboard and gamepad.</summary>
        public static event Action SchemeChanged;

        /// <summary>The key or button for an action on the device in use, e.g. "ENTER" or "A".</summary>
        public static string Key(HintAction action) => Scheme == InputScheme.Gamepad ? GamepadKey(action) : KeyboardKey(action);

        public static string KeyboardKey(HintAction action) => action switch
        {
            HintAction.Choose => "↑ ↓",
            HintAction.Browse => "← →",
            HintAction.Change => "← →",
            HintAction.Confirm => "ENTER",
            HintAction.Back => "ESC",
            HintAction.Secondary => "U",
            HintAction.Rotate => "Q  E",
            HintAction.Pause => "ESC",
            HintAction.Restart => "BACKSPACE",
            _ => string.Empty,
        };

        public static string GamepadKey(HintAction action) => action switch
        {
            HintAction.Choose => "D-PAD",
            HintAction.Browse => "LB  RB",
            HintAction.Change => "← →",
            HintAction.Confirm => "A",
            HintAction.Back => "B",
            HintAction.Secondary => "Y",
            HintAction.Rotate => "R-STICK",
            HintAction.Pause => "MENU",
            HintAction.Restart => "VIEW",
            _ => string.Empty,
        };

        /// <summary>Switches the scheme to the device used this frame, if any. Called once a frame.</summary>
        internal static void Poll()
        {
            if (Scheme != InputScheme.Gamepad && GamepadUsed())
                SetScheme(InputScheme.Gamepad);
            else if (Scheme != InputScheme.KeyboardMouse && KeyboardOrMouseUsed())
                SetScheme(InputScheme.KeyboardMouse);
        }

        private static bool GamepadUsed()
        {
            var pad = Gamepad.current;
            if (pad == null)
                return false;

            return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
                   || pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                   || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
                   || pad.dpad.ReadValue().sqrMagnitude > 0.25f || pad.leftStick.ReadValue().magnitude > StickThreshold
                   || pad.leftTrigger.ReadValue() > StickThreshold || pad.rightTrigger.ReadValue() > StickThreshold;
        }

        private static bool KeyboardOrMouseUsed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                return true;

            var mouse = Mouse.current;
            return mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 25f);
        }

        private static void SetScheme(InputScheme scheme)
        {
            Scheme = scheme;
            SchemeChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Scheme = InputScheme.KeyboardMouse;
            SchemeChanged = null;
        }
    }
}
