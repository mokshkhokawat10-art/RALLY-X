using System;
using System.Collections.Generic;
using RallyX.Progression;
using RallyX.Racing;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The track selection screen, opened by RACE on the main menu (which owns the choice and the input): the track's map,
    /// name and setting, laps, lap length, pay and best lap, or what unlocks it, a dot per track, and RACE and BACK. The card
    /// slides in from the side it was browsed towards. Lives in MainMenu.uxml's "track-select" element; styles in Menus.uss.
    /// </summary>
    internal sealed class TrackSelect
    {
        private readonly VisualElement overlay;
        private readonly VisualElement card;
        private readonly TrackMap map;
        private readonly Label name;
        private readonly Label description;
        private readonly Label laps;
        private readonly Label length;
        private readonly Label pay;
        private readonly Label record;
        private readonly Label lockTag;
        private readonly Label count;
        private readonly VisualElement dots;
        private readonly Button race;

        private TrackSelect(VisualElement root)
        {
            overlay = root.Q("track-select");
            card = root.Q("select-card");
            name = root.Q<Label>("select-name");
            description = root.Q<Label>("select-description");
            laps = root.Q<Label>("select-laps");
            length = root.Q<Label>("select-length");
            pay = root.Q<Label>("select-credits");
            record = root.Q<Label>("select-record");
            lockTag = root.Q<Label>("select-lock");
            count = root.Q<Label>("select-count");
            dots = root.Q("select-dots");
            race = root.Q<Button>("select-race");
            map = new TrackMap { LineWidth = 8f };
            map.AddToClassList("select-map-drawing");
            root.Q("select-map")?.Add(map);
        }

        public bool IsOpen { get; private set; }

        /// <summary>The screen in a layout, wired to the menu's actions, or null if the layout has none.</summary>
        public static TrackSelect Bind(VisualElement root, Action previous, Action next, Action raceTrack, Action back)
        {
            if (root.Q("track-select") == null)
                return null;

            var screen = new TrackSelect(root);
            if (screen.card == null || screen.name == null || screen.description == null || screen.laps == null || screen.length == null
                || screen.pay == null || screen.record == null || screen.lockTag == null || screen.count == null || screen.dots == null
                || screen.race == null)
            {
                Debug.LogError("[RALLY X] The track selection screen in MainMenu.uxml is missing elements.");
                return null;
            }

            Wire(root.Q<Button>("select-previous"), previous);
            Wire(root.Q<Button>("select-next"), next);
            Wire(screen.race, raceTrack);
            Wire(root.Q<Button>("select-back"), back);
            screen.overlay.style.display = DisplayStyle.None;
            return screen;
        }

        public void Open()
        {
            IsOpen = true;
            overlay.style.display = DisplayStyle.Flex;
            Pop(overlay, "track-select--enter");
        }

        public void Close()
        {
            IsOpen = false;
            overlay.style.display = DisplayStyle.None;
        }

        /// <summary>Slides the card in from the side it was browsed towards (+1 = next, from the right).</summary>
        public void AnimateSwap(int direction)
        {
            if (IsOpen)
                Pop(card, direction > 0 ? "select-card--from-right" : "select-card--from-left");
        }

        public void Show(TrackInfo track, int index, int total, bool locked, string unlockHint, float bestLap, bool canRace)
        {
            if (track == null)
                return;

            name.text = track.DisplayName.ToUpperInvariant();
            description.text = track.Description.ToUpperInvariant();
            laps.text = track.Laps > 0 ? track.Laps.ToString() : "-";
            length.text = track.LapLength > 0f ? $"{track.LapLength / 1000f:0.00} KM" : "-";
            pay.text = $"x{track.CreditMultiplier:0.##}";
            record.text = bestLap > 0f ? RaceTime.Format(bestLap) : "NONE YET";
            lockTag.text = locked ? $"LOCKED  |  {unlockHint}" : "READY TO RACE";
            lockTag.EnableInClassList("tag--bad", locked);
            count.text = $"TRACK {index + 1} / {total}";
            race.SetEnabled(canRace);
            card.EnableInClassList("select-card--locked", locked);

            var outline = new Vector2[track.MapOutline.Count];
            for (int i = 0; i < outline.Length; i++)
                outline[i] = track.MapOutline[i];
            map.SetOutline(outline);
            map.LineColor = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;

            dots.Clear();
            for (int i = 0; i < total; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("car-dot");
                dot.EnableInClassList("car-dot--current", i == index);
                dots.Add(dot);
            }
        }

        private static void Wire(Button button, Action action)
        {
            if (button == null)
                return;

            button.focusable = false;
            button.clicked += () => action?.Invoke();
        }

        private static void Pop(VisualElement element, string entryClass)
        {
            element.AddToClassList(entryClass);
            element.schedule.Execute(() => element.RemoveFromClassList(entryClass));
        }
    }
}
