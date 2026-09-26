using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RallyX.Vehicle
{
    /// <summary>Development panel with live telemetry for tuning handling. Toggle with F2.</summary>
    [RequireComponent(typeof(VehicleController))]
    [DisallowMultipleComponent]
    public sealed class VehicleDebugPanel : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Key toggleKey = Key.F2;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.1f;
        [SerializeField] private Vector2 panelSize = new Vector2(390f, 250f);
        [SerializeField, Min(8)] private int fontSize = 15;

        private readonly StringBuilder builder = new StringBuilder(384);
        private VehicleController vehicle;
        private GUIStyle labelStyle;
        private string text = string.Empty;
        private float timeSinceRefresh;

        /// <summary>Whether the panel is on screen. F2 toggles it.</summary>
        public bool Visible
        {
            get => visible;
            set => visible = value;
        }

        private void Awake() => vehicle = GetComponent<VehicleController>();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;

            timeSinceRefresh += Time.unscaledDeltaTime;
            if (!visible || timeSinceRefresh < refreshInterval)
                return;

            timeSinceRefresh = 0f;
            text = BuildText();
        }

        private string BuildText()
        {
            var drift = vehicle.Drift;
            builder.Clear();
            builder.Append("VEHICLE   [").Append(toggleKey).Append("] hide\n");
            builder.Append("Speed:  ").Append(Mathf.RoundToInt(vehicle.SpeedKmh)).Append(" km/h\n");
            builder.Append("Steer angle:  ").Append(vehicle.SteerAngle.ToString("0.0")).Append("°\n");
            builder.Append("Wheels grounded:  ").Append(vehicle.GroundedWheelCount).Append('/').Append(vehicle.Wheels.Count).Append('\n');
            AppendSurfaces();
            builder.Append("Slip angle:  ").Append(drift.SlipAngle.ToString("0")).Append("°\n");
            builder.Append("Drifting:  ");
            if (drift.IsDrifting)
                builder.Append("YES  ").Append(drift.CurrentDriftDuration.ToString("0.0")).Append(" s\n");
            else
                builder.Append("no\n");
            builder.Append("Rear grip:  ").Append(drift.RearGripScale.ToString("0.00")).Append('\n');
            builder.Append("Airborne:  ");
            if (vehicle.IsGrounded)
                builder.Append("no");
            else
                builder.Append(vehicle.AirborneTime.ToString("0.0")).Append(" s");
            return builder.ToString();
        }

        private void AppendSurfaces()
        {
            builder.Append("Surface (FL FR RL RR):  ");
            var wheels = vehicle.Wheels;
            for (int i = 0; i < wheels.Count; i++)
            {
                if (i > 0)
                    builder.Append(" | ");
                var wheel = wheels[i];
                builder.Append(wheel.IsGrounded && wheel.SurfaceType != null ? wheel.SurfaceType.DisplayName : "-");
            }

            builder.Append('\n');
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };

            var panel = new Rect(Screen.width - panelSize.x - 10f, 10f, panelSize.x, panelSize.y);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, panel.height - 12f), text, labelStyle);
        }
    }
}
