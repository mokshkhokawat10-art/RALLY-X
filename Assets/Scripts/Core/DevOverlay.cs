using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace RallyX.Core
{
    /// <summary>
    /// Development overlay showing the active render pipeline, frame rate and live driving input.
    /// Used to verify the project foundation; toggle with F1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Key toggleKey = Key.F1;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.1f;
        [SerializeField] private Vector2 panelSize = new Vector2(340f, 215f);
        [SerializeField, Min(8)] private int fontSize = 15;

        private readonly StringBuilder builder = new StringBuilder(256);
        private RallyInputActions input;
        private GUIStyle labelStyle;
        private string text = string.Empty;
        private string lastDevice = "none";
        private int pausePresses;
        private int framesSinceRefresh;
        private float timeSinceRefresh;

        private void Awake() => input = new RallyInputActions();

        private void OnEnable() => input.Enable();

        private void OnDisable() => input.Disable();

        private void OnDestroy() => input.Dispose();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;

            if (input.Pause.WasPressedThisFrame())
                pausePresses++;

            TrackLastUsedDevice();

            framesSinceRefresh++;
            timeSinceRefresh += Time.unscaledDeltaTime;
            if (timeSinceRefresh < refreshInterval)
                return;

            float fps = framesSinceRefresh / timeSinceRefresh;
            framesSinceRefresh = 0;
            timeSinceRefresh = 0f;

            if (visible)
                text = BuildText(fps);
        }

        private void TrackLastUsedDevice()
        {
            var actions = input.Driving.actions;
            for (int i = 0; i < actions.Count; i++)
            {
                var control = actions[i].activeControl;
                if (control != null)
                {
                    lastDevice = control.device.displayName;
                    return;
                }
            }
        }

        private string BuildText(float fps)
        {
            var pipeline = RenderPipelineManager.currentPipeline;
            var gamepad = Gamepad.current;

            builder.Clear();
            builder.Append("RALLY X  -  foundation check   [").Append(toggleKey).Append("] hide\n");
            builder.Append("Pipeline:  ").Append(pipeline != null ? pipeline.GetType().Name : "Built-in (URP NOT ACTIVE)").Append('\n');
            builder.Append("FPS:  ").Append(Mathf.RoundToInt(fps)).Append('\n');
            builder.Append("Gamepad:  ").Append(gamepad != null ? gamepad.displayName : "none").Append('\n');
            builder.Append("Last input device:  ").Append(lastDevice).Append('\n');
            builder.Append("Throttle:  ").Append(input.Throttle.ReadValue<float>().ToString("0.00")).Append('\n');
            builder.Append("Brake:  ").Append(input.Brake.ReadValue<float>().ToString("0.00")).Append('\n');
            builder.Append("Steer:  ").Append(input.Steer.ReadValue<float>().ToString("0.00")).Append('\n');
            builder.Append("Handbrake:  ").Append(input.Handbrake.IsPressed() ? "ON" : "off").Append('\n');
            builder.Append("Pause presses:  ").Append(pausePresses);
            return builder.ToString();
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = fontSize };

            var panel = new Rect(10f, 10f, panelSize.x, panelSize.y);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, panel.height - 12f), text, labelStyle);
        }
    }
}
