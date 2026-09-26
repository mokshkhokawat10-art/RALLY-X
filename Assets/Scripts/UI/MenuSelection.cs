using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// Keyboard and gamepad selection over a row or column of buttons, alongside the mouse: moving highlights the next
    /// enabled button, submitting runs its action, hovering highlights it and clicking runs it. The highlighted
    /// button carries the <see cref="SelectedClass"/> USS class. Buttons don't take UI Toolkit focus, so only the
    /// menu's own input moves the highlight.
    /// </summary>
    public sealed class MenuSelection
    {
        public const string SelectedClass = "is-selected";

        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Action> actions = new List<Action>();

        public int Index { get; private set; } = -1;
        public Button Selected => Index >= 0 ? buttons[Index] : null;
        public int Count => buttons.Count;

        public void Add(Button button, Action action)
        {
            int index = buttons.Count;
            buttons.Add(button);
            actions.Add(action);
            button.focusable = false;
            button.clicked += () => Run(index);
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (button.enabledSelf)
                    Select(index);
            });

            if (Index < 0 && button.enabledSelf)
                Select(index);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= buttons.Count)
                return;

            if (Selected != null)
                Selected.RemoveFromClassList(SelectedClass);
            Index = index;
            buttons[index].AddToClassList(SelectedClass);
        }

        public void Select(Button button) => Select(buttons.IndexOf(button));

        /// <summary>Highlights the next enabled button in a direction (+1 / -1), stopping at the ends.</summary>
        public void Move(int direction)
        {
            for (int index = Index + direction; index >= 0 && index < buttons.Count; index += direction)
            {
                if (!buttons[index].enabledSelf)
                    continue;
                Select(index);
                return;
            }
        }

        /// <summary>Runs the highlighted button's action, if it is enabled.</summary>
        public void Submit() => Run(Index);

        private void Run(int index)
        {
            if (index < 0 || index >= buttons.Count || !buttons[index].enabledSelf)
                return;
            actions[index]?.Invoke();
        }
    }
}
