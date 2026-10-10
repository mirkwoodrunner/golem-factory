using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Workbench: Unity's WorkbenchCanvas.prefab screen, rebuilt from its numbers (milestone
    /// G7). Every decision is Core's <see cref="WorkbenchSession"/>; this node draws the session
    /// and turns gestures into its calls.
    ///
    /// <para>
    /// Layout, as the prefab has it (1280x720 reference): a brass header (gear, THE WORKBENCH,
    /// the TARGET line, CLOSE); the BLUEPRINT VIEWPORT (chassis portrait, name and stats; the
    /// TRIGGER socket and six STEP sockets with their loop captions); the CHASSIS RACK; the
    /// scrolling CARD VAULT of teal logic cores and copper appendages; and the bottom bar with
    /// the diagnostic tape, the ENGAGE GEARS lever, PATENT, and the status plate.
    /// </para>
    ///
    /// <para>
    /// Cards drag with Godot's own drag and drop: a card hands over its id from
    /// <c>_GetDragData</c>, a socket row takes it in <c>_DropData</c>, and a drag that ends
    /// with no taker is the session's "released over nothing" -- a socketed card leaves its
    /// socket, a vault card goes back. While a card is held every socket is lit by whether it
    /// would take it. Cards are rebuilt from the session whenever its Version moves, Unity's
    /// "always re-render from data", so a failed drag can never orphan a card.
    /// </para>
    /// </summary>
    public partial class WorkbenchScreen : CanvasLayer, IWorkbenchScreen, IClosableScreen
    {
        // WorkbenchController's palette.
        private static readonly Color Teal = new Color(0.42f, 0.80f, 0.75f);
        private static readonly Color Copper = new Color(0.88f, 0.58f, 0.34f);
        private static readonly Color CardInk = new Color(0.10f, 0.08f, 0.06f);
        private static readonly Color CardSubInk = new Color(0.24f, 0.19f, 0.14f);
        private static readonly Color StepperFace = new Color(0.96f, 0.82f, 0.58f);
        private static readonly Color StepperDisabled = new Color(0.72f, 0.62f, 0.50f, 0.55f);
        private static readonly Color SelectedChassis = new Color(0.95f, 0.62f, 0.20f);
        private static readonly Color UnselectedChassis = new Color(0.40f, 0.36f, 0.33f);
        private static readonly Color ChassisInk = new Color(0.98f, 0.94f, 0.86f);
        private static readonly Color ChassisSubInk = new Color(0.84f, 0.77f, 0.65f);
        private static readonly Color VaultHeading = new Color(0.86f, 0.70f, 0.40f);
        private static readonly Color RejectedChassis = new Color(0.78f, 0.22f, 0.16f);
        private static readonly Color StatusError = new Color(1f, 0.55f, 0.42f);
        private static readonly Color StatusInfo = new Color(0.72f, 0.94f, 0.72f);
        private static readonly Color PlateInk = new Color(0.13f, 0.09f, 0.05f);
        private static readonly Color CaptionInk = new Color(0.94f, 0.80f, 0.50f);
        private static readonly Color HintInk = new Color(0.55f, 0.47f, 0.40f);
        private static readonly Color TriggerSocket = new Color(0.38f, 0.72f, 0.68f);
        private static readonly Color StepSocket = new Color(0.80f, 0.54f, 0.34f);
        private const float CardHeight = 44f;
        private const float ChassisButtonHeight = 52f;
        private const float VaultHeadingHeight = 26f;
        private const float LeverTravel = 74f;

        private const string Ui = "res://art/UI/";

        private WorldNode _world;
        private WorkbenchSession _session;
        private Control _root;
        private Label _target;
        private Button _close;
        private TextureRect _portrait;
        private Label _chassisName;
        private Label _chassisStats;
        private WorkbenchDropRow _triggerRow;
        private readonly List<WorkbenchDropRow> _stepRows = new List<WorkbenchDropRow>();
        private VBoxContainer _chassisList;
        private readonly Dictionary<ChassisDefinition, Button> _chassisButtons = new Dictionary<ChassisDefinition, Button>();
        private VBoxContainer _vault;
        private Label _tape;
        private Button _lever;
        private Control _handle;
        private float _handleRestY;
        private Button _patent;
        private Label _status;
        private int _renderedVersion = -1;

        // Drag bookkeeping: cards are rebuilt, so a drag carries an id into this table.
        private readonly Dictionary<int, WorkbenchCardRef> _cards = new Dictionary<int, WorkbenchCardRef>();
        private int _nextCardId;
        private int _draggingId = -1;
        private bool _dropTaken;

        private float _leverElapsed = -1f;
        private bool _leverRefusing;
        private ChassisDefinition _flashChassis;
        private float _flashElapsed = -1f;

        public WorkbenchSession Session => _session;
        public bool IsOpen => _session != null && _session.IsOpen;
        public GolemEntity TargetGolem => _session?.TargetGolem;

        // --- For scenarios ------------------------------------------------------------------
        public IReadOnlyList<WorkbenchDropRow> StepRows => _stepRows;
        public WorkbenchDropRow TriggerRow => _triggerRow;
        public Button Lever => _lever;
        public Button PatentButton => _patent;
        public Control VaultList => _vault;
        public IReadOnlyDictionary<ChassisDefinition, Button> ChassisButtons => _chassisButtons;
        public string StatusText => _status.Text;
        public string TapeText => _tape.Text;
        public string TargetText => _target.Text;
        public Control TargetLabelControl => _target;
        public Control CloseButtonControl => _close;
        public Control RootControl => _root;
        public bool LeverAnimating => _leverElapsed >= 0f;
        public bool LeverRefusing => _leverRefusing && _leverElapsed >= 0f;

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30; // above the HUD, the build menu and the construction panel's backdrop
            _world = WorldNode.Find(this);
            _session = _world.Sandbox.Workbench;
            _session.LeverPulled += () => { _leverElapsed = 0f; _leverRefusing = false; };
            _session.LeverRefused += () => { _leverElapsed = 0f; _leverRefusing = true; };
            _session.ChassisRejected += c => { _flashChassis = c; _flashElapsed = 0f; };

            Build();
            BuildChassisButtons();
            _root.Visible = false;
            _world.Sandbox.Screens.Register(this);
            _world.Sandbox.ConfigureScreens(null, this, null);
        }

        // --- IWorkbenchScreen ---------------------------------------------------------------

        public void Open()
        {
            _world.Sandbox.Screens.Opening(this);
            _session.Open();
            _root.Visible = true;
            Rebuild();
        }

        public void Close()
        {
            _session.Close();
            _root.Visible = false;
        }

        public void RetargetGolem(GolemEntity golem)
        {
            _session.RetargetGolem(golem);
            Rebuild();
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (IsOpen && e.IsActionPressed(BuildCursorNode.CancelAction))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            if (!IsOpen)
            {
                return;
            }
            float dt = (float)delta;
            _session.Tick(dt);

            if (_draggingId >= 0 && !GetViewport().GuiIsDragging())
            {
                // The drag ended. If no socket took the card, it was released over nothing.
                int id = _draggingId;
                _draggingId = -1;
                if (!_dropTaken && _cards.TryGetValue(id, out WorkbenchCardRef card))
                {
                    _session.HandleDrop(card, null);
                }
                _session.EndCardDrag();
            }

            if (_session.Version != _renderedVersion)
            {
                Rebuild();
            }

            _tape.Text = _session.Ticker();
            _status.Text = _session.StatusText;
            _status.AddThemeColorOverride("font_color", _session.StatusIsInfo ? StatusInfo : StatusError);
            _lever.Disabled = !_session.CanEngage;
            _lever.Modulate = _session.CanEngage ? Colors.White : new Color(1f, 1f, 1f, WorkbenchInteractionColors.DisabledAlpha);
            ApplyHighlights();
            AnimateLever(dt);
            AnimateFlash(dt);
        }

        // --- Drag and drop ------------------------------------------------------------------

        internal Variant BeginDrag(int cardId, Control preview, Control source)
        {
            if (!_cards.TryGetValue(cardId, out WorkbenchCardRef card))
            {
                return default;
            }
            _draggingId = cardId;
            _dropTaken = false;
            _session.BeginCardDrag(card);
            source.Modulate = new Color(1f, 1f, 1f, WorkbenchDragVisuals.GhostAlpha); // the ghost left behind
            source.SetDragPreview(preview);
            return cardId;
        }

        internal void Drop(int cardId, WorkbenchZone zone)
        {
            if (!_cards.TryGetValue(cardId, out WorkbenchCardRef card))
            {
                return;
            }
            _dropTaken = true;
            _session.HandleDrop(card, zone);
        }

        private void ApplyHighlights()
        {
            _triggerRow.ApplyHighlight(_session.LogicHighlight, TriggerSocket);
            for (int i = 0; i < _stepRows.Count; i++)
            {
                _stepRows[i].ApplyHighlight(_session.AppendageHighlights[i], StepSocket);
            }
        }

        // --- Rebuild from the session --------------------------------------------------------

        private void Rebuild()
        {
            _renderedVersion = _session.Version;
            _cards.Clear();

            foreach (Node child in _vault.GetChildren())
            {
                child.QueueFree();
            }
            _vault.AddChild(VaultHeadingLabel("LOGIC CORES  ·  triggers"));
            foreach (LogicCoreDefinition core in _session.VaultLogicCores)
            {
                _vault.AddChild(MakeCard(WorkbenchCardRef.FromVault(core), inSocket: false));
            }
            _vault.AddChild(VaultHeadingLabel("APPENDAGES  ·  actions"));
            foreach (AppendageActionDefinition appendage in _session.VaultAppendages)
            {
                _vault.AddChild(MakeCard(WorkbenchCardRef.FromVault(appendage), inSocket: false));
            }

            _triggerRow.ClearCard();
            if (_session.DraftLogicCore != null)
            {
                _triggerRow.SetCard(MakeCard(WorkbenchCardRef.FromTriggerSocket(_session.DraftLogicCore), inSocket: true));
            }
            _triggerRow.Caption.Text = _session.TriggerCaption;

            for (int i = 0; i < _stepRows.Count; i++)
            {
                WorkbenchDropRow row = _stepRows[i];
                row.ClearCard();
                row.Visible = _session.SlotVisible(i);
                AppendageActionDefinition card = _session.DraftAppendageAt(i);
                if (row.Visible && card != null)
                {
                    row.SetCard(MakeCard(WorkbenchCardRef.FromSocket(card, i), inSocket: true));
                }
                row.Caption.Text = _session.SlotCaption(i);
            }

            foreach (KeyValuePair<ChassisDefinition, Button> entry in _chassisButtons)
            {
                if (entry.Key != _flashChassis || _flashElapsed < 0f)
                {
                    entry.Value.SelfModulate = entry.Key == _session.DraftChassis ? SelectedChassis : UnselectedChassis;
                }
            }

            string sprite = _session.DraftChassis?.chassisSprite;
            _portrait.Texture = string.IsNullOrEmpty(sprite) ? null
                : GD.Load<Texture2D>("res://art/" + (sprite.EndsWith(".png") ? sprite : sprite + ".png"));
            _chassisName.Text = _session.ChassisNameLine;
            _chassisStats.Text = _session.ChassisStatsLine;
            _target.Text = _session.TargetHeader;
        }

        private Control VaultHeadingLabel(string text)
        {
            var holder = new Control { CustomMinimumSize = new Vector2(0f, VaultHeadingHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
            Label label = Ugui.Fill(Ugui.Text("Heading", text, 12, VaultHeading));
            label.VerticalAlignment = VerticalAlignment.Bottom;
            holder.AddChild(label);
            return holder;
        }

        private WorkbenchCardControl MakeCard(WorkbenchCardRef card, bool inSocket)
        {
            int id = _nextCardId++;
            _cards[id] = card;
            var control = new WorkbenchCardControl(this, id, card.LogicCore != null ? Teal : Copper)
            {
                Name = WorkbenchSession.CardDisplayName(card.LogicCore, card.Appendage),
                CustomMinimumSize = new Vector2(0f, CardHeight),
            };
            control.Build(card, CardFace(card, withStepper: inSocket && card.SourceAppendageIndex >= 0 && _session.SlotHasStepper(card.SourceAppendageIndex)));
            return control;
        }

        /// <summary>The face of a card: name, subtitle, and (socketed Haul cards) the batch-size dial.</summary>
        internal Control CardFace(WorkbenchCardRef card, bool withStepper)
        {
            var face = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            Ugui.Fill(face);
            // A socketed Haul names its good with a picker (G10): "Haul  < Coke >". The card's own
            // name ("Haul Scrap") is only where it starts.
            bool goodPicker = withStepper && _session.IsDraftHaul(card.SourceAppendageIndex);
            face.AddChild(Ugui.Place(Ugui.Text("Label", goodPicker ? "Haul" : WorkbenchSession.CardDisplayName(card.LogicCore, card.Appendage), 15, CardInk),
                0.06f, 0.40f, goodPicker ? 0.34f : 0.98f, 0.98f));
            if (goodPicker)
            {
                face.AddChild(GoodPicker(card.SourceAppendageIndex));
            }
            // The card's subtitle names an old routing id ("ScrapNode -> ScrapBuffer") that a placed
            // golem never uses; a Haul with a picker says where it actually takes from.
            string subtitle = goodPicker ? "from the tile behind" : WorkbenchSession.CardSubtitle(card.LogicCore, card.Appendage);
            face.AddChild(Ugui.Place(Ugui.Text("Subtitle", subtitle, 11, CardSubInk),
                0.06f, 0.04f, withStepper ? 0.60f : 0.98f, 0.42f));
            if (withStepper)
            {
                face.AddChild(Stepper(card.SourceAppendageIndex, card.Appendage));
            }
            return face;
        }

        /// <summary>The good a Haul slot takes, with arrows to change it (WorkbenchSession.CycleDraftItemType).</summary>
        private Control GoodPicker(int slot)
        {
            var row = Ugui.Place(new Control { Name = "Good", MouseFilter = Control.MouseFilterEnum.Ignore }, 0.34f, 0.46f, 0.98f, 0.96f);
            row.AddChild(PickerButton("PreviousGood", "<", 0f, 0.16f, () => _session.CycleDraftItemType(slot, -1)));
            row.AddChild(Ugui.Place(Ugui.Text("Value", GolemFactory.Economy.ItemTiers.DisplayName(_session.DraftItemTypeAt(slot)), 13, CardInk, align: 2, bold: true),
                0.17f, 0f, 0.83f, 1f));
            row.AddChild(PickerButton("NextGood", ">", 0.84f, 1f, () => _session.CycleDraftItemType(slot, +1)));
            return row;
        }

        private Button PickerButton(string name, string glyph, float minX, float maxX, System.Action pressed)
        {
            var button = Ugui.Place(new Button { Name = name, FocusMode = Control.FocusModeEnum.None }, minX, 0f, maxX, 1f);
            var face = new StyleBoxFlat { BgColor = StepperFace };
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            {
                button.AddThemeStyleboxOverride(state, face);
            }
            button.AddChild(Ugui.Fill(Ugui.Text("Label", glyph, 14, CardInk, align: 2)));
            button.Pressed += pressed;
            return button;
        }

        private Control Stepper(int slot, AppendageActionDefinition card)
        {
            var row = Ugui.Place(new Control { Name = "Quantity", MouseFilter = Control.MouseFilterEnum.Ignore }, 0.62f, 0.04f, 0.98f, 0.42f);
            int quantity = _session.DraftQuantityAt(slot);
            row.AddChild(StepperButton("Decrease", "-", 0f, 0.22f, slot, -1, quantity > WorkbenchQuantityPolicy.MinQuantity));
            row.AddChild(Ugui.Place(Ugui.Text("Value", WorkbenchQuantityPolicy.Describe(card, quantity), 11, CardInk, align: 2), 0.24f, 0f, 0.76f, 1f));
            row.AddChild(StepperButton("Increase", "+", 0.78f, 1f, slot, +1, quantity < WorkbenchQuantityPolicy.MaxQuantity));
            return row;
        }

        private Button StepperButton(string name, string glyph, float minX, float maxX, int slot, int delta, bool enabled)
        {
            var button = Ugui.Place(new Button { Name = name, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None }, minX, 0f, maxX, 1f);
            var face = new StyleBoxFlat { BgColor = enabled ? StepperFace : StepperDisabled };
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            {
                button.AddThemeStyleboxOverride(state, face);
            }
            button.AddChild(Ugui.Fill(Ugui.Text("Label", glyph, 14, CardInk, align: 2)));
            button.Pressed += () => _session.AdjustDraftQuantity(slot, delta);
            return button;
        }

        // --- Construction -------------------------------------------------------------------

        private void Build()
        {
            _root = new Control { Name = "WorkbenchScreen", MouseFilter = Control.MouseFilterEnum.Stop };
            Ugui.Fill(_root);
            AddChild(_root);

            _root.AddChild(Ugui.Fill(Ugui.Image("Backdrop", Ugui.NineSlice(Ui + "Workbench/wb_panel_mahogany.png", 12, tiled: true), new Color(0.24f, 0.21f, 0.19f))));

            // Header.
            Panel header = Ugui.Place(Ugui.Image("HeaderBar", Ugui.NineSlice(Ui + "Workbench/wb_plate_brass.png", 10), new Color(0.92f, 0.88f, 0.84f)),
                0.012f, 0.888f, 0.988f, 0.952f);
            _root.AddChild(header);
            var gear = Ugui.Place(new TextureRect
            {
                Name = "Gear", Texture = GD.Load<Texture2D>(Ui + "Steampunk/gear_large.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = new Color(0.42f, 0.30f, 0.14f), MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0f, 0.5f, 0f, 0.5f, 12f, 0f, 34f, 34f, 0f, 0.5f);
            header.AddChild(gear);
            header.AddChild(Ugui.Place(Ugui.Text("Title", "THE WORKBENCH", 24, PlateInk, bold: true), 0.045f, 0f, 0.46f, 1f));
            _target = Ugui.Place(Ugui.Text("TargetLabel", "TARGET  ·  none", 15, new Color(0.28f, 0.20f, 0.10f), align: 4), 0.46f, 0f, 0.86f, 1f);
            header.AddChild(_target);
            Button close = _close = Ugui.Place(TextureButton("CloseButton", Ui + "Steampunk/steampunk_button_exit.png", 8, new Color(0.72f, 0.30f, 0.24f)),
                1f, 0.5f, 1f, 0.5f, -8f, 0f, 88f, 34f, 1f, 0.5f);
            close.AddChild(Ugui.Fill(Ugui.Text("Label", "CLOSE", 14, new Color(0.98f, 0.92f, 0.82f), align: 2)));
            close.Pressed += Close;
            header.MouseFilter = Control.MouseFilterEnum.Pass;
            header.AddChild(close);

            BuildViewport();
            BuildRack();
            BuildVault();
            BuildBottomBar();
        }

        private void BuildViewport()
        {
            Panel viewport = Ugui.Place(Ugui.Image("BlueprintViewport", Ugui.NineSlice(Ui + "Workbench/wb_panel_mahogany.png", 12, tiled: true), new Color(0.88f, 0.86f, 0.84f)),
                0.012f, 0.205f, 0.487f, 0.875f);
            viewport.MouseFilter = Control.MouseFilterEnum.Pass;
            _root.AddChild(viewport);
            viewport.AddChild(TitlePlate("BLUEPRINT VIEWPORT", 0.028f, 0.972f, 0.03f));

            Panel pane = Ugui.Place(Ugui.Image("ChassisPane", Ugui.NineSlice(Ui + "Workbench/wb_panel_blueprint.png", 12, tiled: true), Colors.White),
                0.032f, 0.028f, 0.4f, 0.895f);
            viewport.AddChild(pane);
            _portrait = Ugui.Place(new TextureRect
            {
                Name = "ChassisPortrait", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0.1f, 0.33f, 0.9f, 0.95f);
            pane.AddChild(_portrait);
            _chassisName = Ugui.Place(Ugui.Text("ChassisName", "No chassis fitted", 15, new Color(0.82f, 0.91f, 0.96f), align: 2), 0.04f, 0.17f, 0.96f, 0.31f);
            _chassisStats = Ugui.Place(Ugui.Text("ChassisStats", "Pick a chassis from the rack", 11, new Color(0.55f, 0.74f, 0.84f), align: 2), 0.04f, 0.04f, 0.96f, 0.16f);
            pane.AddChild(_chassisName);
            pane.AddChild(_chassisStats);

            var stack = Ugui.Place(new Control { Name = "SlotStack", MouseFilter = Control.MouseFilterEnum.Pass }, 0.425f, 0.028f, 0.968f, 0.895f);
            viewport.AddChild(stack);
            _triggerRow = MakeRow("LogicCoreSlot", WorkbenchZone.Trigger, "TRIGGER", "drop a logic core", TriggerSocket, 0.87f, 0.99f);
            stack.AddChild(_triggerRow);
            for (int i = 0; i < _session.SocketCount; i++)
            {
                float top = 0.85f - 0.14f * i;
                WorkbenchDropRow row = MakeRow("AppendageSlot" + i, WorkbenchZone.Socket(i), "STEP " + (i + 1), "drop an appendage", StepSocket, top - 0.12f, top);
                _stepRows.Add(row);
                stack.AddChild(row);
            }
        }

        private WorkbenchDropRow MakeRow(string name, WorkbenchZone zone, string caption, string hint, Color socketTint, float minY, float maxY)
        {
            var row = Ugui.Place(new WorkbenchDropRow(this, zone) { Name = name }, 0f, minY, 1f, maxY);
            row.Caption = Ugui.Place(Ugui.Text("Caption", caption, 12, CaptionInk, align: 4), 0f, 0f, 0.235f, 1f);
            // Wraps, as the prefab's TMP caption did: "STEP 2  ·  loops back to 1" is two lines
            // in this column, and a single truncated line hid the half that teaches the loop.
            row.Caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.Caption.ClipText = false;
            row.Caption.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            row.AddChild(row.Caption);
            row.Socket = Ugui.Place(Ugui.Image("Socket", Ugui.NineSlice(Ui + "Workbench/wb_slot_socket.png", 10), socketTint), 0.255f, 0.06f, 1f, 0.94f);
            row.AddChild(row.Socket);
            row.Socket.AddChild(Ugui.Fill(Ugui.Text("Hint", hint, 11, HintInk, align: 2)));
            return row;
        }

        private void BuildRack()
        {
            Panel rack = Ugui.Place(Ugui.Image("ChassisRack", Ugui.NineSlice(Ui + "Workbench/wb_panel_iron.png", 12, tiled: true), new Color(0.62f, 0.61f, 0.60f)),
                0.497f, 0.205f, 0.7f, 0.875f);
            rack.MouseFilter = Control.MouseFilterEnum.Pass;
            _root.AddChild(rack);
            rack.AddChild(TitlePlate("CHASSIS RACK", 0.045f, 0.955f, 0.05f));
            var area = Ugui.Place(new MarginContainer { Name = "ChassisRow", MouseFilter = Control.MouseFilterEnum.Pass }, 0.045f, 0.02f, 0.955f, 0.905f);
            area.AddThemeConstantOverride("margin_left", 6);
            area.AddThemeConstantOverride("margin_right", 6);
            area.AddThemeConstantOverride("margin_top", 8);
            area.AddThemeConstantOverride("margin_bottom", 6);
            rack.AddChild(area);
            _chassisList = new VBoxContainer();
            _chassisList.AddThemeConstantOverride("separation", 8);
            area.AddChild(_chassisList);
        }

        private void BuildChassisButtons()
        {
            foreach (ChassisDefinition chassis in _session.RackChassis)
            {
                Button button = TextureButton(chassis.name, Ui + "Steampunk/steampunk_button_blank.png", 8, UnselectedChassis);
                button.CustomMinimumSize = new Vector2(0f, ChassisButtonHeight);
                button.AddChild(Ugui.Place(Ugui.Text("Name", WorkbenchDiagnostics.Humanize(chassis.name), 15, ChassisInk), 0.05f, 0.44f, 0.97f, 0.96f));
                button.AddChild(Ugui.Place(Ugui.Text("Subtitle", WorkbenchSession.ChassisSubtitle(chassis), 11, ChassisSubInk), 0.05f, 0.06f, 0.97f, 0.46f));
                ChassisDefinition captured = chassis;
                button.Pressed += () => _session.SelectChassis(captured);
                _chassisButtons[chassis] = button;
                _chassisList.AddChild(button);
            }
        }

        private void BuildVault()
        {
            Panel vault = Ugui.Place(Ugui.Image("CardVault", Ugui.NineSlice(Ui + "Workbench/wb_panel_mahogany.png", 12, tiled: true), new Color(0.88f, 0.86f, 0.84f)),
                0.71f, 0.205f, 0.988f, 0.875f);
            vault.MouseFilter = Control.MouseFilterEnum.Pass;
            _root.AddChild(vault);
            vault.AddChild(TitlePlate("CARD VAULT", 0.035f, 0.965f, 0.04f));
            ColorRect viewport = Ugui.Place(Ugui.Rect("VaultViewport", new Color(0.10f, 0.07f, 0.05f, 0.85f)), 0.035f, 0.02f, 0.965f, 0.902f);
            vault.AddChild(viewport);
            var scroll = Ugui.Fill(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
            viewport.AddChild(scroll);
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            {
                margin.AddThemeConstantOverride(side, 8);
            }
            scroll.AddChild(margin);
            _vault = new VBoxContainer { Name = "VaultContent", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _vault.AddThemeConstantOverride("separation", 6);
            margin.AddChild(_vault);
        }

        private void BuildBottomBar()
        {
            Panel bar = Ugui.Place(Ugui.Image("BottomBar", Ugui.NineSlice(Ui + "Workbench/wb_panel_iron.png", 12, tiled: true), new Color(0.70f, 0.68f, 0.66f)),
                0.012f, 0.012f, 0.988f, 0.195f);
            bar.MouseFilter = Control.MouseFilterEnum.Pass;
            _root.AddChild(bar);

            Panel tape = Ugui.Place(Ugui.Image("DiagnosticTape", Ugui.NineSlice(Ui + "Workbench/wb_tape.png", 0), Colors.White),
                0.012f, 0.5f, 0.655f, 0.5f, 0f, 16f, 0f, 64f);
            bar.AddChild(tape);
            tape.AddChild(Ugui.Place(new TextureRect
            {
                Name = "Pip", Texture = GD.Load<Texture2D>(Ui + "Workbench/wb_gauge_needle.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0f, 0.5f, 0f, 0.5f, 12f, 0f, 16f, 16f, 0f, 0.5f));
            _tape = Ugui.Place(Ugui.Text("TapeText", "", 12, new Color(0.16f, 0.12f, 0.07f)), 0f, 0f, 1f, 1f, 0f, 0f, -48f, 0f);
            _tape.OffsetLeft = 36f;
            tape.AddChild(_tape);

            _lever = Ugui.Place(TextureButton("EngageLever", Ui + "Workbench/wb_panel_iron.png", 12, new Color(0.80f, 0.76f, 0.70f)),
                0.672f, 0.5f, 0.672f, 0.5f, 0f, 0f, 238f, 118f, 0f, 0.5f);
            _lever.AddChild(Ugui.Place(new TextureRect
            {
                Name = "Track", Texture = GD.Load<Texture2D>(Ui + "Workbench/wb_lever_track.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0f, 0.5f, 0f, 0.5f, 7f, 0f, 40f, 118f, 0f, 0.5f));
            _lever.AddChild(Ugui.Place(Ugui.Text("Label", "ENGAGE", 20, new Color(0.99f, 0.88f, 0.62f), align: 2, bold: true), 0.31f, 0.46f, 0.97f, 0.93f));
            _lever.AddChild(Ugui.Place(Ugui.Text("Label2", "GEARS", 20, new Color(0.99f, 0.88f, 0.62f), align: 2, bold: true), 0.31f, 0.21f, 0.97f, 0.68f));
            _handle = Ugui.Place(new TextureRect
            {
                Name = "Handle", Texture = GD.Load<Texture2D>(Ui + "Workbench/wb_lever_handle.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0f, 0.5f, 0f, 0.5f, 10f, 30f, 56f, 46f, 0f, 0.5f);
            _lever.AddChild(_handle);
            _handleRestY = _handle.OffsetTop;
            _lever.Pressed += () => _session.Engage();
            bar.AddChild(_lever);

            _patent = Ugui.Place(TextureButton("PatentButton", Ui + "Steampunk/steampunk_button_blank_iron.png", 8, new Color(0.82f, 0.72f, 0.52f)),
                0.865f, 0.5f, 0.865f, 0.5f, 0f, 0f, 148f, 68f, 0f, 0.5f);
            _patent.AddChild(Ugui.Place(Ugui.Text("Label", "PATENT", 17, PlateInk, align: 2, bold: true), 0f, 0.38f, 1f, 0.92f));
            _patent.Pressed += () => _session.Patent();
            bar.AddChild(_patent);

            Panel statusPlate = Ugui.Place(Ugui.Image("StatusPlate", Ugui.NineSlice(Ui + "Workbench/wb_panel_iron.png", 12), new Color(0.15f, 0.11f, 0.085f, 0.96f)),
                0.01f, 0.8f, 0.99f, 0.99f);
            bar.AddChild(statusPlate);
            _status = Ugui.Place(Ugui.Text("StatusText", "", 17, StatusError), 0f, 0f, 1f, 1f, 0f, 0f, -28f, -4f);
            statusPlate.AddChild(_status);
        }

        private static Panel TitlePlate(string text, float minX, float maxX, float labelMinX)
        {
            Panel plate = Ugui.Place(Ugui.Image("TitlePlate", Ugui.NineSlice(Ui + "Workbench/wb_plate_brass.png", 10), new Color(0.88f, 0.84f, 0.80f)),
                minX, 0.918f, maxX, 0.985f);
            plate.AddChild(Ugui.Place(Ugui.Text("Label", text, 15, PlateInk, bold: true), labelMinX, 0f, 0.99f, 1f));
            return plate;
        }

        /// <summary>A UGUI Button over a sliced sprite: the plate tinted by SelfModulate, labels as children.</summary>
        private static Button TextureButton(string name, string path, int border, Color tint)
        {
            var button = new Button { Name = name, SelfModulate = tint, FocusMode = Control.FocusModeEnum.None };
            StyleBoxTexture box = Ugui.NineSlice(path, border);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            {
                button.AddThemeStyleboxOverride(state, box);
            }
            return button;
        }

        // --- Presentation motion ------------------------------------------------------------

        private void AnimateLever(float dt)
        {
            if (_leverElapsed < 0f)
            {
                return;
            }
            _leverElapsed += dt;
            float duration = _leverRefusing ? WorkbenchLeverMotion.RefuseSeconds : WorkbenchLeverMotion.TotalSeconds;
            float normalized = 0f;
            if (_leverElapsed >= duration)
            {
                _leverElapsed = -1f;
                _leverRefusing = false;
            }
            else
            {
                normalized = _leverRefusing
                    ? WorkbenchLeverMotion.ComputeRefusedNormalized(_leverElapsed)
                    : WorkbenchLeverMotion.ComputeHandleNormalized(_leverElapsed);
            }
            // Unity moved the handle DOWN by travel x normalized; Godot's y points down.
            float height = _handle.OffsetBottom - _handle.OffsetTop;
            _handle.OffsetTop = _handleRestY + LeverTravel * normalized;
            _handle.OffsetBottom = _handle.OffsetTop + height;
        }

        private void AnimateFlash(float dt)
        {
            if (_flashElapsed < 0f || _flashChassis == null || !_chassisButtons.TryGetValue(_flashChassis, out Button plate))
            {
                return;
            }
            _flashElapsed += dt;
            Color rest = _flashChassis == _session.DraftChassis ? SelectedChassis : UnselectedChassis;
            if (_flashElapsed >= WorkbenchRejectFlash.TotalSeconds)
            {
                plate.SelfModulate = rest;
                _flashElapsed = -1f;
                _flashChassis = null;
                return;
            }
            plate.SelfModulate = rest.Lerp(RejectedChassis, WorkbenchRejectFlash.ComputeStrength(_flashElapsed));
        }
    }

    /// <summary>A socket row: drop target for one zone, with its caption and socket plate.</summary>
    public partial class WorkbenchDropRow : Control
    {
        private readonly WorkbenchScreen _screen;
        public WorkbenchZone Zone { get; }
        public Label Caption { get; set; }
        public Panel Socket { get; set; }
        public WorkbenchCardControl Card { get; private set; }

        public WorkbenchDropRow() { }

        public WorkbenchDropRow(WorkbenchScreen screen, WorkbenchZone zone)
        {
            _screen = screen;
            Zone = zone;
            MouseFilter = MouseFilterEnum.Pass;
        }

        public void SetCard(WorkbenchCardControl card)
        {
            Card = card;
            Ugui.Fill(card);
            Socket.AddChild(card);
        }

        public void ClearCard()
        {
            Card?.QueueFree();
            Card = null;
        }

        public void ApplyHighlight(DropZoneHighlight highlight, Color neutral) =>
            Socket.SelfModulate = highlight switch
            {
                DropZoneHighlight.Valid => Ugui.ToGodot(WorkbenchDragVisuals.ValidSocketTint),
                DropZoneHighlight.Invalid => Ugui.ToGodot(WorkbenchDragVisuals.InvalidSocketTint),
                _ => neutral,
            };

        public override bool _CanDropData(Vector2 atPosition, Variant data) => data.VariantType == Variant.Type.Int;

        public override void _DropData(Vector2 atPosition, Variant data) => _screen.Drop(data.AsInt32(), Zone);
    }

    /// <summary>A card face that can be picked up. Hover brightens it, as Unity's card did.</summary>
    public partial class WorkbenchCardControl : Panel
    {
        private readonly WorkbenchScreen _screen;
        private readonly int _id;
        private readonly Color _base;
        private WorkbenchCardRef _card;
        private bool _hovered;

        public WorkbenchCardRef CardRef => _card;

        public WorkbenchCardControl() { }

        public WorkbenchCardControl(WorkbenchScreen screen, int id, Color baseColor)
        {
            _screen = screen;
            _id = id;
            _base = baseColor;
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeStyleboxOverride("panel", Ugui.NineSlice("res://art/UI/Workbench/wb_card_face.png", 10));
            MouseEntered += () => { _hovered = true; Tint(); };
            MouseExited += () => { _hovered = false; Tint(); };
        }

        public void Build(WorkbenchCardRef card, Control face)
        {
            _card = card;
            AddChild(face);
            Tint();
        }

        private void Tint() => SelfModulate = Ugui.ToGodot(WorkbenchInteractionColors.Apply(
            new Compat.Color(_base.R, _base.G, _base.B, 1f),
            _hovered ? WorkbenchInteractionState.Hovered : WorkbenchInteractionState.Normal));

        public override Variant _GetDragData(Vector2 atPosition)
        {
            // The preview is a lifted copy of this face; the original stays behind as the ghost.
            var preview = new Panel { Size = Size, SelfModulate = _base, Scale = Vector2.One * WorkbenchDragVisuals.LiftScale };
            preview.AddThemeStyleboxOverride("panel", Ugui.NineSlice("res://art/UI/Workbench/wb_card_face.png", 10));
            preview.AddChild(_screen.CardFace(_card, withStepper: false));
            preview.Position = -atPosition;
            var holder = new Control();
            holder.AddChild(preview);
            return _screen.BeginDrag(_id, holder, this);
        }
    }
}
