using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// A row of control hints, each a key cap and what it does ("[ENTER] RACE"), naming keyboard keys or gamepad buttons
    /// for the device in use (<see cref="InputHints"/>) and redrawn when the player switches device. Styles are
    /// .hint-item, .key-cap and .hint-label in Assets/UI/Common.uss.
    /// </summary>
    public sealed class HintBar
    {
        private readonly VisualElement container;
        private readonly List<(HintAction action, string label)> hints = new List<(HintAction action, string label)>();

        public HintBar(VisualElement container)
        {
            this.container = container;
            container.RegisterCallback<AttachToPanelEvent>(_ => InputHints.SchemeChanged += Rebuild);
            container.RegisterCallback<DetachFromPanelEvent>(_ => InputHints.SchemeChanged -= Rebuild);
            if (container.panel != null)
                InputHints.SchemeChanged += Rebuild;
        }

        /// <summary>The hints as text, e.g. "ENTER RACE  ESC BACK". For tests and tooling.</summary>
        public string Text
        {
            get
            {
                var parts = new List<string>();
                foreach (var (action, label) in hints)
                    parts.Add($"{InputHints.Key(action)} {label}");
                return string.Join("  ", parts);
            }
        }

        /// <summary>Shows these hints in place of the ones there.</summary>
        public void Show(params (HintAction action, string label)[] items)
        {
            hints.Clear();
            hints.AddRange(items);
            Rebuild();
        }

        private void Rebuild()
        {
            container.Clear();
            foreach (var (action, label) in hints)
            {
                var item = new VisualElement { pickingMode = PickingMode.Ignore };
                item.AddToClassList("hint-item");
                var key = new Label(InputHints.Key(action)) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("key-cap");
                var text = new Label(label) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("hint-label");
                item.Add(key);
                item.Add(text);
                container.Add(item);
            }
        }
    }
}
