using System;
using System.Collections.Generic;
using RallyX.Audio;
using RallyX.Core;
using RallyX.Garage;
using RallyX.Progression;
using RallyX.SaveSystem;
using RallyX.Vehicle;
using UnityEngine;
using UnityEngine.UIElements;

namespace RallyX.UI
{
    /// <summary>
    /// The garage. Browse the cars on the turntable, compare their stats and choose the one to race; or open a car's
    /// upgrades, see what the next stage of each part does to the stats, and buy it after a confirmation, as long as
    /// the credits are there. Locked cars are shown as silhouettes with what unlocks them. Every change is saved to
    /// the player's profile straight away. Keyboard, gamepad and mouse (drag to turn the car). Every action has its menu
    /// sound, and the panels slide in as cars change and the upgrades open. Layout and styling are in
    /// Assets/UI/Menus/Garage.uxml and Menus.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class GarageMenu : MonoBehaviour
    {
        private static readonly VehicleStat[] Stats = (VehicleStat[])Enum.GetValues(typeof(VehicleStat));

        [SerializeField] private VehicleRoster roster;
        [SerializeField] private VehicleShowroom showroom;

        [Tooltip("Upgrades on sale and starting credits. Without it the garage sells no upgrades.")]
        [SerializeField] private ProgressionSettings progression;

        [Tooltip("Turning speed of the car from the stick or Q/E, degrees per second.")]
        [SerializeField, Min(1f)] private float rotateSpeed = 120f;

        [Tooltip("Degrees the car turns per pixel dragged with the mouse.")]
        [SerializeField, Min(0.01f)] private float dragRotateSpeed = 0.35f;

        private readonly Dictionary<VehicleStat, StatRow> statRows = new Dictionary<VehicleStat, StatRow>();
        private readonly List<UpgradeRow> upgradeRows = new List<UpgradeRow>();
        private readonly List<VisualElement> dots = new List<VisualElement>();
        private UIDocument document;
        private RallyInputActions input;
        private PlayerProfile profile;
        private VisualElement boundRoot;
        private VisualElement viewport;
        private VisualElement infoPanel;
        private VisualElement upgradePanel;
        private VisualElement statsPanel;
        private VisualElement confirm;
        private Label counter;
        private Label credits;
        private Label carCategory;
        private Label carName;
        private Label carDescription;
        private Label status;
        private Label upgradeCar;
        private Label upgradeDescription;
        private Label upgradeStatus;
        private Label statsLegend;
        private Label hints;
        private Label confirmTitle;
        private Label confirmDetails;
        private Button selectButton;
        private Button upgradesButton;
        private Button buyButton;
        private bool dragging;
        private string purchaseMessage;
        private bool purchaseMessageGood;

        /// <summary>Position of the car on show in the roster.</summary>
        public int Index { get; private set; }

        /// <summary>The car on show.</summary>
        public VehicleDefinition Current => roster != null && roster.Count > 0 ? roster.Vehicles[Index] : null;

        public VehicleRoster Roster => roster;

        /// <summary>The upgrades on sale, or null if the garage has none.</summary>
        public UpgradeCatalog Catalog => progression != null ? progression.Upgrades : null;

        /// <summary>True while the car's upgrades are shown instead of its details.</summary>
        public bool ShowingUpgrades { get; private set; }

        /// <summary>True while a purchase waits for confirmation.</summary>
        public bool ConfirmingPurchase { get; private set; }

        /// <summary>Position of the highlighted upgrade in the catalogue.</summary>
        public int UpgradeIndex { get; private set; }

        public UpgradeDefinition CurrentUpgrade =>
            Catalog != null && UpgradeIndex < Catalog.Upgrades.Count ? Catalog.Upgrades[UpgradeIndex] : null;

        public void Configure(VehicleRoster newRoster, VehicleShowroom newShowroom, ProgressionSettings newProgression)
        {
            roster = newRoster;
            showroom = newShowroom;
            progression = newProgression;
        }

        /// <summary>Shows a car, wrapping round the ends of the roster.</summary>
        public void ShowVehicle(int index)
        {
            if (roster == null || roster.Count == 0)
                return;

            Index = (index % roster.Count + roster.Count) % roster.Count;
            ConfirmingPurchase = false;
            purchaseMessage = null;
            Refresh();
        }

        public void Next() => SwitchVehicle(Index + 1);

        public void Previous() => SwitchVehicle(Index - 1);

        /// <summary>Browses to another car with its sound, the panels sliding in with the new car's details.</summary>
        private void SwitchVehicle(int index)
        {
            GameAudio.Play(UiCue.Switch);
            ShowVehicle(index);
            AnimateIn(ShowingUpgrades ? upgradePanel : infoPanel);
            AnimateIn(statsPanel);
        }

        /// <summary>
        /// Plays a panel's entry transition: the entry state (panel--enter in Menus.uss) is applied now and removed on the
        /// next frame, so the panel eases into place.
        /// </summary>
        private static void AnimateIn(VisualElement panel)
        {
            if (panel == null)
                return;

            panel.AddToClassList("panel--enter");
            panel.schedule.Execute(() => panel.RemoveFromClassList("panel--enter"));
        }

        /// <summary>Chooses the car on show to race. Returns false if it is locked.</summary>
        public bool SelectCurrent()
        {
            var vehicle = Current;
            if (vehicle == null || !vehicle.IsUnlocked(profile))
            {
                GameAudio.Play(UiCue.Error);
                return false;
            }

            GameAudio.Play(UiCue.Confirm);
            profile.SelectVehicle(vehicle.Id);
            Refresh();
            return true;
        }

        /// <summary>Steps back: out of a confirmation, out of the upgrades, or out of the garage to the main menu.</summary>
        public void Back()
        {
            if (ConfirmingPurchase)
                CancelPurchase();
            else if (ShowingUpgrades)
                CloseUpgrades();
            else
            {
                GameAudio.Play(UiCue.Back);
                GameScenes.Load(GameScenes.MainMenu);
            }
        }

        /// <summary>Shows the car's upgrades. Returns false if the garage sells none.</summary>
        public bool OpenUpgrades()
        {
            if (Catalog == null || Catalog.Upgrades.Count == 0)
            {
                GameAudio.Play(UiCue.Error);
                return false;
            }

            GameAudio.Play(UiCue.Confirm);
            ShowingUpgrades = true;
            purchaseMessage = null;
            Refresh();
            AnimateIn(upgradePanel);
            return true;
        }

        public void CloseUpgrades()
        {
            GameAudio.Play(UiCue.Back);
            ShowingUpgrades = false;
            ConfirmingPurchase = false;
            purchaseMessage = null;
            Refresh();
            AnimateIn(infoPanel);
        }

        public void SelectUpgrade(int index)
        {
            if (Catalog == null || Catalog.Upgrades.Count == 0 || ConfirmingPurchase)
                return;

            int clamped = Mathf.Clamp(index, 0, Catalog.Upgrades.Count - 1);
            if (clamped != UpgradeIndex)
                GameAudio.Play(UiCue.Navigate);
            UpgradeIndex = clamped;
            purchaseMessage = null;
            Refresh();
        }

        public void MoveUpgrade(int direction) => SelectUpgrade(UpgradeIndex + direction);

        /// <summary>Whether the highlighted upgrade's next stage can be bought for the car on show.</summary>
        public PurchaseCheck CheckPurchase() => UpgradeShop.Check(profile, Current, CurrentUpgrade);

        /// <summary>
        /// Asks to buy the highlighted upgrade's next stage: opens the confirmation if it can be bought, otherwise
        /// says why not. Returns true if the confirmation opened.
        /// </summary>
        public bool RequestPurchase()
        {
            if (!ShowingUpgrades || CurrentUpgrade == null || ConfirmingPurchase)
                return false;

            var check = CheckPurchase();
            ConfirmingPurchase = check == PurchaseCheck.Available;
            purchaseMessage = ConfirmingPurchase ? null : Explain(check);
            purchaseMessageGood = false;
            GameAudio.Play(ConfirmingPurchase ? UiCue.Confirm : UiCue.Error);
            Refresh();
            if (ConfirmingPurchase && confirm != null)
            {
                confirm.AddToClassList("confirm--enter");
                confirm.schedule.Execute(() => confirm.RemoveFromClassList("confirm--enter"));
            }

            return ConfirmingPurchase;
        }

        /// <summary>Buys the stage waiting for confirmation. Returns true if it was bought.</summary>
        public bool ConfirmPurchase()
        {
            if (!ConfirmingPurchase)
                return false;

            ConfirmingPurchase = false;
            var upgrade = CurrentUpgrade;
            int level = UpgradeShop.NextLevel(profile, Current, upgrade);
            var result = UpgradeShop.Buy(profile, Current, upgrade);
            bool bought = result == PurchaseCheck.Available;
            GameAudio.Play(bought ? UiCue.Purchase : UiCue.Error);
            purchaseMessage = bought ? $"{upgrade.DisplayName.ToUpperInvariant()} STAGE {level} INSTALLED" : Explain(result);
            purchaseMessageGood = bought;
            Refresh();
            return bought;
        }

        public void CancelPurchase()
        {
            GameAudio.Play(UiCue.Back);
            ConfirmingPurchase = false;
            Refresh();
        }

        // Read-outs for tests and tooling.

        /// <summary>Text on the stat row's value label.</summary>
        public string StatText(VehicleStat stat) => statRows.TryGetValue(stat, out var row) ? row.Value.text : null;

        /// <summary>How full the stat's bar is, 0..1.</summary>
        public float StatRating(VehicleStat stat) => statRows.TryGetValue(stat, out var row) ? row.Rating : 0f;

        /// <summary>How full the stat's bar would be with the previewed upgrade, 0..1 (the same as the bar with no preview).</summary>
        public float StatPreviewRating(VehicleStat stat) => statRows.TryGetValue(stat, out var row) ? row.PreviewRating : 0f;

        public string StatusText => status != null ? status.text : null;
        public string SelectButtonText => selectButton != null ? selectButton.text : null;
        public bool SelectButtonEnabled => selectButton != null && selectButton.enabledSelf;
        public string ShownName => carName != null ? carName.text : null;
        public string CreditsText => credits != null ? credits.text : null;
        public string BuyButtonText => buyButton != null ? buyButton.text : null;
        public bool BuyButtonEnabled => buyButton != null && buyButton.enabledSelf;
        public string UpgradeStatusText => upgradeStatus != null && upgradeStatus.style.display != DisplayStyle.None ? upgradeStatus.text : string.Empty;
        public string ConfirmText => confirmTitle != null ? $"{confirmTitle.text} {confirmDetails.text}" : null;

        /// <summary>Cost shown on an upgrade's row ("MAXED" when there are no more stages).</summary>
        public string UpgradeCostText(UpgradeType type) => upgradeRows.Find(row => row.Upgrade.Type == type)?.Cost.text;

        /// <summary>Stages lit on an upgrade's row.</summary>
        public int UpgradePipsLit(UpgradeType type) => upgradeRows.Find(row => row.Upgrade.Type == type)?.PipsLit ?? 0;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            input = new RallyInputActions();
        }

        private void OnEnable()
        {
            input.EnableMenu();
            profile = PlayerProfile.Current;
            profile.Changed += Refresh;
        }

        private void OnDisable()
        {
            input.DisableMenu();
            profile.Changed -= Refresh;
        }

        private void OnDestroy() => input.Dispose();

        private void Start()
        {
            string problem = roster == null ? "it has no VehicleRoster" : roster.FindProblem();
            if (problem == null && progression != null)
                problem = progression.FindProblem();
            if (problem != null)
            {
                Debug.LogError($"{name}: the garage can't open: {problem}.", this);
                enabled = false;
                return;
            }

            if (progression != null)
                profile.EnsureStarted(progression.StartingCredits);

            // Open on the car the player races.
            ShowVehicle(Mathf.Max(0, roster.IndexOf(roster.Resolve(profile))));
        }

        private void Update()
        {
            if (!Bind())
                return;

            if (ConfirmingPurchase)
            {
                if (input.Submit.WasPressedThisFrame())
                    ConfirmPurchase();
                else if (input.Back.WasPressedThisFrame())
                    CancelPurchase();
                return;
            }

            if (input.MenuLeft.WasPressedThisFrame())
                Previous();
            if (input.MenuRight.WasPressedThisFrame())
                Next();

            if (ShowingUpgrades)
            {
                if (input.MenuUp.WasPressedThisFrame())
                    MoveUpgrade(-1);
                if (input.MenuDown.WasPressedThisFrame())
                    MoveUpgrade(1);
                if (input.Submit.WasPressedThisFrame())
                    RequestPurchase();
                if (input.Back.WasPressedThisFrame() || input.Secondary.WasPressedThisFrame())
                    CloseUpgrades();
            }
            else
            {
                if (input.Submit.WasPressedThisFrame())
                    SelectCurrent();
                if (input.Secondary.WasPressedThisFrame())
                    OpenUpgrades();
                else if (input.Back.WasPressedThisFrame())
                    Back();
            }

            float turn = input.RotateView.ReadValue<float>();
            if (showroom != null && Mathf.Abs(turn) > 0.01f)
                showroom.Rotate(turn * rotateSpeed * Time.unscaledDeltaTime);
        }

        private static string Explain(PurchaseCheck check) => check switch
        {
            PurchaseCheck.CarLocked => "UNLOCK THIS CAR FIRST",
            PurchaseCheck.Maxed => "FULLY UPGRADED",
            PurchaseCheck.NotEnoughCredits => "NOT ENOUGH CREDITS",
            _ => null,
        };

        private void Refresh()
        {
            var vehicle = Current;
            if (vehicle == null || !Bind())
                return;

            var catalog = Catalog;
            var raced = roster.Resolve(profile);
            bool locked = !vehicle.IsUnlocked(profile);
            bool chosen = vehicle == raced;

            credits.text = Credits.Format(profile.Credits);
            counter.text = $"{Index + 1} / {roster.Count}";
            for (int i = 0; i < dots.Count; i++)
                dots[i].EnableInClassList("car-dot--current", i == Index);
            carCategory.text = vehicle.Category.ToUpperInvariant();
            carName.text = vehicle.DisplayName.ToUpperInvariant();
            carDescription.text = vehicle.Description;
            ShowStatus(status, locked ? vehicle.UnlockHint(profile) : chosen ? "YOUR CAR" : string.Empty, good: chosen && !locked);

            SetVisible(infoPanel, !ShowingUpgrades);
            SetVisible(upgradePanel, ShowingUpgrades);
            SetVisible(selectButton, !ShowingUpgrades);
            SetVisible(upgradesButton, !ShowingUpgrades);
            SetVisible(buyButton, ShowingUpgrades);
            upgradesButton.SetEnabled(catalog != null && catalog.Upgrades.Count > 0);
            selectButton.text = locked ? "LOCKED" : chosen ? "SELECTED" : "SELECT";
            selectButton.SetEnabled(!locked && !chosen);

            // Bars show the car with its installed upgrades; the tick marks the car raced now; in the upgrades a green
            // extension shows what the highlighted part's next stage adds.
            var tuning = catalog != null ? catalog.TuningFor(profile, vehicle.Id) : default;
            var stats = vehicle.MeasureStats(tuning);
            var racedStats = raced.MeasureStats(catalog != null ? catalog.TuningFor(profile, raced.Id) : default);
            VehicleStats? preview = null;
            var upgrade = CurrentUpgrade;
            int nextLevel = upgrade != null ? UpgradeShop.NextLevel(profile, vehicle, upgrade) : 0;
            if (ShowingUpgrades && upgrade != null && nextLevel <= upgrade.MaxLevel)
                preview = vehicle.MeasureStats(catalog.TuningWith(profile, vehicle.Id, upgrade, nextLevel));

            foreach (var stat in Stats)
            {
                var row = statRows[stat];
                float rating = roster.Rate(stats, stat);
                float previewRating = preview.HasValue ? roster.Rate(preview.Value, stat) : rating;
                string text = stats.Describe(stat, rating);
                string previewText = preview.HasValue ? preview.Value.Describe(stat, previewRating) : text;
                bool improves = previewText != text;

                row.Rating = rating;
                row.PreviewRating = previewRating;
                row.Fill.style.width = Length.Percent(Mathf.Max(rating, 0.02f) * 100f);
                row.Gain.style.left = Length.Percent(rating * 100f);
                row.Gain.style.width = Length.Percent(Mathf.Max(0f, previewRating - rating) * 100f);
                row.Value.text = improves ? $"{text}  →  {previewText.Replace("0-100  ", string.Empty).Replace("100-0  ", string.Empty)}" : text;
                row.Value.EnableInClassList("stat-value--preview", improves);
                row.Marker.style.left = Length.Percent(roster.Rate(racedStats, stat) * 100f);
                row.Marker.style.display = chosen || ShowingUpgrades ? DisplayStyle.None : DisplayStyle.Flex;
            }

            statsLegend.text = preview.HasValue ? $"Green: with {upgrade.DisplayName} stage {nextLevel}"
                : ShowingUpgrades ? "With the upgrades installed" : "White tick: the car you race now";

            if (ShowingUpgrades)
                RefreshUpgrades(vehicle, upgrade, nextLevel);

            SetVisible(confirm, ConfirmingPurchase);
            if (ConfirmingPurchase)
            {
                int cost = UpgradeShop.NextCost(profile, vehicle, upgrade);
                confirmTitle.text = $"{upgrade.DisplayName.ToUpperInvariant()}  STAGE {nextLevel}";
                confirmDetails.text = $"For the {vehicle.DisplayName}.   Cost {Credits.Format(cost)}   |   Credits after {Credits.Format(profile.Credits - cost)}";
            }

            hints.text = ConfirmingPurchase ? "ENTER   CONFIRM        ESC   CANCEL"
                : ShowingUpgrades ? "UP / DOWN   CHOOSE PART        LEFT / RIGHT   CHANGE CAR        ENTER   BUY        ESC   BACK"
                : "LEFT / RIGHT   CHANGE CAR        Q / E  OR  DRAG   TURN        ENTER   SELECT        U   UPGRADES        ESC   BACK";

            if (showroom != null)
                showroom.Show(vehicle, locked);
        }

        private void RefreshUpgrades(VehicleDefinition vehicle, UpgradeDefinition selected, int nextLevel)
        {
            upgradeCar.text = vehicle.DisplayName.ToUpperInvariant();
            foreach (var row in upgradeRows)
            {
                int level = profile.GetUpgradeLevel(vehicle.Id, row.Upgrade.Id);
                bool maxed = level >= row.Upgrade.MaxLevel;
                int cost = maxed ? 0 : row.Upgrade.CostOf(level + 1);
                row.SetLevel(level);
                row.Cost.text = maxed ? "MAXED" : Credits.Format(cost);
                row.Cost.EnableInClassList("upgrade-cost--maxed", maxed);
                row.Cost.EnableInClassList("upgrade-cost--short", !maxed && cost > profile.Credits);
                row.Root.EnableInClassList(MenuSelection.SelectedClass, row.Upgrade == selected);
            }

            upgradeDescription.text = selected.Description;
            var check = UpgradeShop.Check(profile, vehicle, selected);
            int nextCost = UpgradeShop.NextCost(profile, vehicle, selected);
            buyButton.text = check switch
            {
                PurchaseCheck.Maxed => "MAXED",
                PurchaseCheck.CarLocked => "LOCKED",
                _ => $"BUY  {Credits.Format(nextCost)}",
            };
            buyButton.SetEnabled(check == PurchaseCheck.Available && !ConfirmingPurchase);

            string message = purchaseMessage;
            bool good = purchaseMessageGood;
            if (message == null)
            {
                message = check switch
                {
                    PurchaseCheck.NotEnoughCredits => $"NOT ENOUGH CREDITS: NEED {Credits.Format(nextCost)}",
                    PurchaseCheck.CarLocked => "UNLOCK THIS CAR FIRST",
                    PurchaseCheck.Maxed => "FULLY UPGRADED",
                    _ => string.Empty,
                };
                good = check == PurchaseCheck.Maxed;
            }
            else if (!good && check == PurchaseCheck.NotEnoughCredits)
            {
                message = $"NOT ENOUGH CREDITS: NEED {Credits.Format(nextCost)}";
            }

            ShowStatus(upgradeStatus, message, good);
        }

        private static void ShowStatus(Label label, string text, bool good)
        {
            label.text = text ?? string.Empty;
            label.EnableInClassList("status--chosen", good);
            label.EnableInClassList("status--locked", !good);
            label.style.display = label.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Finds the garage's elements and builds the stat and upgrade rows; again whenever the document rebuilds its tree.</summary>
        private bool Bind()
        {
            var root = document.rootVisualElement;
            if (root == null)
                return false;
            if (root == boundRoot)
                return true;

            viewport = root.Q("viewport");
            infoPanel = root.Q("info-panel");
            upgradePanel = root.Q("upgrade-panel");
            statsPanel = root.Q(className: "garage-stats");
            confirm = root.Q("confirm");
            counter = root.Q<Label>("car-counter");
            credits = root.Q<Label>("credits");
            carCategory = root.Q<Label>("car-category");
            carName = root.Q<Label>("car-name");
            carDescription = root.Q<Label>("car-description");
            status = root.Q<Label>("car-status");
            upgradeCar = root.Q<Label>("upgrade-car");
            upgradeDescription = root.Q<Label>("upgrade-description");
            upgradeStatus = root.Q<Label>("upgrade-status");
            statsLegend = root.Q<Label>("stats-legend");
            hints = root.Q<Label>("hints");
            confirmTitle = root.Q<Label>("confirm-title");
            confirmDetails = root.Q<Label>("confirm-details");
            selectButton = root.Q<Button>("select-button");
            upgradesButton = root.Q<Button>("upgrades-button");
            buyButton = root.Q<Button>("buy-button");
            var backButton = root.Q<Button>("back-button");
            var previousButton = root.Q<Button>("previous-button");
            var nextButton = root.Q<Button>("next-button");
            var confirmBuy = root.Q<Button>("confirm-buy");
            var confirmCancel = root.Q<Button>("confirm-cancel");
            var statList = root.Q("stat-list");
            var dotList = root.Q("car-dots");
            var upgradeList = root.Q("upgrade-list");
            var required = new object[]
            {
                viewport, infoPanel, upgradePanel, confirm, counter, credits, carCategory, carName, carDescription, status, upgradeCar,
                upgradeDescription, upgradeStatus, statsLegend, hints, confirmTitle, confirmDetails, selectButton, upgradesButton, buyButton,
                backButton, previousButton, nextButton, confirmBuy, confirmCancel, statList, dotList, upgradeList,
            };
            if (Array.Exists(required, element => element == null))
            {
                Debug.LogError($"{name}: the garage layout is missing elements GarageMenu needs. Check Garage.uxml.", this);
                enabled = false;
                return false;
            }

            boundRoot = root;
            foreach (var button in new[] { selectButton, upgradesButton, buyButton, backButton, previousButton, nextButton, confirmBuy, confirmCancel })
                button.focusable = false;
            selectButton.clicked += () => SelectCurrent();
            upgradesButton.clicked += () => OpenUpgrades();
            buyButton.clicked += () => RequestPurchase();
            backButton.clicked += Back;
            previousButton.clicked += Previous;
            nextButton.clicked += Next;
            confirmBuy.clicked += () => ConfirmPurchase();
            confirmCancel.clicked += CancelPurchase;

            statRows.Clear();
            statList.Clear();
            foreach (var stat in Stats)
            {
                var row = new StatRow(stat);
                statList.Add(row.Root);
                statRows[stat] = row;
            }

            upgradeRows.Clear();
            upgradeList.Clear();
            if (Catalog != null)
            {
                for (int i = 0; i < Catalog.Upgrades.Count; i++)
                {
                    int index = i;
                    var row = new UpgradeRow(Catalog.Upgrades[i]);
                    row.Root.RegisterCallback<ClickEvent>(_ => SelectUpgrade(index));
                    upgradeList.Add(row.Root);
                    upgradeRows.Add(row);
                }
            }

            dots.Clear();
            dotList.Clear();
            for (int i = 0; i < roster.Count; i++)
            {
                var dot = new VisualElement();
                dot.AddToClassList("car-dot");
                dotList.Add(dot);
                dots.Add(dot);
            }

            // Dragging across the empty middle of the screen turns the car.
            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            return true;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            dragging = true;
            viewport.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (dragging && showroom != null)
                showroom.Rotate(-evt.deltaPosition.x * dragRotateSpeed);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            dragging = false;
            viewport.ReleasePointer(evt.pointerId);
        }

        /// <summary>One stat: name and figure above a bar, with the upgrade preview and a tick for the car raced now.</summary>
        private sealed class StatRow
        {
            public StatRow(VehicleStat stat)
            {
                Root = new VisualElement();
                Root.AddToClassList("stat-row");

                var header = new VisualElement();
                header.AddToClassList("stat-header");
                var label = new Label(VehicleStats.Label(stat));
                label.AddToClassList("stat-name");
                Value = new Label();
                Value.AddToClassList("stat-value");
                header.Add(label);
                header.Add(Value);

                var track = new VisualElement();
                track.AddToClassList("stat-track");
                Fill = new VisualElement();
                Fill.AddToClassList("stat-fill");
                Gain = new VisualElement();
                Gain.AddToClassList("stat-gain");
                Marker = new VisualElement();
                Marker.AddToClassList("stat-marker");
                track.Add(Fill);
                track.Add(Gain);
                track.Add(Marker);

                Root.Add(header);
                Root.Add(track);
            }

            public VisualElement Root { get; }
            public float Rating { get; set; }
            public float PreviewRating { get; set; }
            public VisualElement Fill { get; }
            public VisualElement Gain { get; }
            public VisualElement Marker { get; }
            public Label Value { get; }
        }

        /// <summary>One upgrade: its name, a pip per stage (lit when installed) and the next stage's price.</summary>
        private sealed class UpgradeRow
        {
            private readonly List<VisualElement> pips = new List<VisualElement>();

            public UpgradeRow(UpgradeDefinition upgrade)
            {
                Upgrade = upgrade;
                Root = new VisualElement();
                Root.AddToClassList("upgrade-row");

                var name = new Label(upgrade.DisplayName.ToUpperInvariant());
                name.AddToClassList("upgrade-name");
                var pipList = new VisualElement();
                pipList.AddToClassList("upgrade-pips");
                for (int i = 0; i < upgrade.MaxLevel; i++)
                {
                    var pip = new VisualElement();
                    pip.AddToClassList("pip");
                    pipList.Add(pip);
                    pips.Add(pip);
                }

                Cost = new Label();
                Cost.AddToClassList("upgrade-cost");
                Root.Add(name);
                Root.Add(pipList);
                Root.Add(Cost);
            }

            public UpgradeDefinition Upgrade { get; }
            public VisualElement Root { get; }
            public Label Cost { get; }
            public int PipsLit { get; private set; }

            public void SetLevel(int level)
            {
                PipsLit = level;
                for (int i = 0; i < pips.Count; i++)
                    pips[i].EnableInClassList("pip--on", i < level);
            }
        }
    }
}
