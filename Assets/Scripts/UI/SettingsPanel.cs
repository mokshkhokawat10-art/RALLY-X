using System;
using System.Collections.Generic;
using RallyX.Audio;
using RallyX.Core;
using RallyX.SaveSystem;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The settings screen: master, music, sound effects and vehicle volumes and gamepad vibration, saved in the player's
    /// profile as they change. Hosted by the main menu and the pause menu in their layout's element named "settings", with
    /// a "settings-rows" container and a "settings-back" button (styles in Assets/UI/Settings.uss). Up and down choose a
    /// row, left and right change it (Enter toggles vibration), Back closes; the mouse clicks a row's minus and plus.
    /// </summary>
    public sealed class SettingsPanel
    {
        private const float Step = 0.1f;

        private static readonly (AudioChannel channel, string label)[] VolumeRows =
        {
            (AudioChannel.Master, "MASTER VOLUME"),
            (AudioChannel.Music, "MUSIC"),
            (AudioChannel.Effects, "SOUND EFFECTS"),
            (AudioChannel.Vehicle, "VEHICLES"),
        };

        private readonly List<Row> rows = new List<Row>();
        private VisualElement overlay;

        /// <summary>The settings screen closed (Back).</summary>
        public event Action Closed;

        public bool IsOpen { get; private set; }

        /// <summary>Highlighted row: the volumes in <see cref="VolumeRows"/> order, then vibration.</summary>
        public int Index { get; private set; }

        public int RowCount => rows.Count;

        /// <summary>Finds the screen in a layout and builds its rows. Returns false if the layout has no settings screen.</summary>
        public bool Bind(VisualElement root)
        {
            overlay = root.Q("settings");
            var container = root.Q("settings-rows");
            var back = root.Q<Button>("settings-back");
            rows.Clear();
            if (overlay == null || container == null || back == null)
            {
                overlay = null;
                return false;
            }

            container.Clear();
            foreach (var (channel, label) in VolumeRows)
                rows.Add(AddRow(container, label, channel));
            rows.Add(AddRow(container, "VIBRATION", null));

            back.focusable = false;
            back.clicked += Close;
            var hintKeys = root.Q("settings-hints");
            if (hintKeys != null)
                new HintBar(hintKeys).Show((HintAction.Choose, "CHOOSE"), (HintAction.Change, "CHANGE"), (HintAction.Back, "BACK"));
            SetVisible(IsOpen);
            Refresh();
            return true;
        }

        public void Open()
        {
            if (overlay == null)
                return;

            IsOpen = true;
            Index = 0;
            SetVisible(true);
            Refresh();

            // Slides in: the entry state is applied now and removed next frame, so the transition runs.
            overlay.AddToClassList("settings--enter");
            overlay.schedule.Execute(() => overlay.RemoveFromClassList("settings--enter"));
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            SetVisible(false);
            GameAudio.Play(UiCue.Back);
            Closed?.Invoke();
        }

        /// <summary>Reads the menu input for a frame while the screen is open.</summary>
        public void HandleInput(RallyInputActions input)
        {
            if (!IsOpen)
                return;

            if (input.Back.WasPressedThisFrame())
            {
                Close();
                return;
            }

            if (input.MenuUp.WasPressedThisFrame())
                Select(Index - 1);
            if (input.MenuDown.WasPressedThisFrame())
                Select(Index + 1);
            if (input.MenuLeft.WasPressedThisFrame())
                Change(Index, -1);
            if (input.MenuRight.WasPressedThisFrame())
                Change(Index, 1);
            if (input.Submit.WasPressedThisFrame() && rows.Count > 0 && rows[Index].Channel == null)
                Change(Index, 1);
        }

        public void Select(int index)
        {
            if (rows.Count == 0)
                return;

            int clamped = Math.Clamp(index, 0, rows.Count - 1);
            if (clamped != Index)
                GameAudio.Play(UiCue.Navigate);
            Index = clamped;
            Refresh();
        }

        /// <summary>Steps a row's volume by a tenth up (+1) or down (-1), or toggles vibration.</summary>
        public void Change(int index, int direction)
        {
            if (index < 0 || index >= rows.Count)
                return;

            var profile = PlayerProfile.Current;
            var channel = rows[index].Channel;
            if (channel == null)
            {
                profile.SetVibration(!profile.Vibration);
            }
            else
            {
                float before = profile.GetVolume(channel.Value);
                float after = (float)Math.Round(Math.Clamp(before + direction * Step, 0f, 1f), 1);
                if (Math.Abs(after - before) < 0.001f)
                {
                    GameAudio.Play(UiCue.Error);
                    return;
                }

                profile.SetVolume(channel.Value, after);
            }

            Index = index;
            Refresh();

            // Played after the change, so it is heard at the new level.
            GameAudio.Play(UiCue.Navigate);
        }

        private Row AddRow(VisualElement container, string label, AudioChannel? channel)
        {
            var row = new Row { Channel = channel, Root = new VisualElement() };
            row.Root.AddToClassList("settings-row");

            var name = new Label(label);
            name.AddToClassList("settings-name");
            row.Root.Add(name);

            var minus = new Button { text = "-", focusable = false };
            minus.AddToClassList("settings-step");
            row.Root.Add(minus);

            var track = new VisualElement();
            track.AddToClassList("settings-track");
            row.Fill = new VisualElement();
            row.Fill.AddToClassList("settings-fill");
            track.Add(row.Fill);
            row.Root.Add(track);

            var plus = new Button { text = "+", focusable = false };
            plus.AddToClassList("settings-step");
            row.Root.Add(plus);

            row.Value = new Label();
            row.Value.AddToClassList("settings-value");
            row.Root.Add(row.Value);

            int rowIndex = rows.Count;
            minus.clicked += () => Change(rowIndex, -1);
            plus.clicked += () => Change(rowIndex, 1);
            row.Root.RegisterCallback<PointerEnterEvent>(_ => Select(rowIndex));
            container.Add(row.Root);
            return row;
        }

        private void Refresh()
        {
            if (overlay == null)
                return;

            var profile = PlayerProfile.Loaded ?? PlayerProfile.Current;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.Root.EnableInClassList(MenuSelection.SelectedClass, i == Index);
                float value = row.Channel != null ? profile.GetVolume(row.Channel.Value) : profile.Vibration ? 1f : 0f;
                row.Fill.style.width = Length.Percent(value * 100f);
                row.Value.text = row.Channel != null ? $"{Math.Round(value * 100f)}%" : profile.Vibration ? "ON" : "OFF";
            }
        }

        private void SetVisible(bool visible) =>
            overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private sealed class Row
        {
            public AudioChannel? Channel;
            public VisualElement Root;
            public VisualElement Fill;
            public Label Value;
        }
    }
}
