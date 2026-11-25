using Godot;
using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;
using Artillery.Game.World;

namespace Artillery.Game.Ui;

/// <summary>Match setup: players, options, landscape selection with a live 3D preview, save/load.</summary>
public partial class SetupScreen : Control
{
	public event Action<MatchSettings>? StartRequested;
	public event Action? BackRequested;

	private const string Section = "match";
	private MatchSettings _defaults = new();
	private readonly List<(string name, PlayerType type)> _players = new();
	private readonly HashSet<int> _lands = new();
	private int _focusLand = 13;
	private uint _seed = 12345;
	private bool _built;

	private VBoxContainer _playerBox = null!;
	private Button _addBtn = null!;
	private SpinBox _rounds = null!, _shots = null!, _cash = null!;
	private OptionButton _order = null!;
	private CheckBox _air = null!, _wind = null!;
	private HSlider _windEvery = null!;
	private Label _windLbl = null!;
	private readonly Button[] _landBtns = new Button[28];
	private Label _status = null!, _previewName = null!;
	private SubViewport _vp = null!;
	private TerrainView _terrain = null!;
	private Node3D _camPivot = null!;
	private Camera3D _cam = null!;
	private float _yaw = 20, _pitch = 38, _dist = 82;
	private bool _dragging;
	private WorldState? _previewWorld;
	private int _shownLand = -1;
	private Button _startBtn = null!;

	/// <summary>Fresh-form defaults: core defaults, but with air resistance and wind on.</summary>
	public static MatchSettings DefaultSettings() => new() { AirResistance = true, Wind = true };

	/// <summary>Loads the given settings (defaults or previously saved) into the form.</summary>
	public void Init(MatchSettings s)
	{
		_defaults = s;
		Apply(s);
	}

	private void Apply(MatchSettings s)
	{
		_players.Clear();
		foreach (var p in s.Players.Take(4)) _players.Add((p.Name, p.Type));
		while (_players.Count < 2) _players.Add(("", PlayerType.Human));
		_lands.Clear();
		foreach (var l in s.Landscapes) if (l is >= 1 and <= 27) _lands.Add(l);
		if (_lands.Count == 0) _lands.Add(13);
		_focusLand = _lands.Min();
		_seed = s.Seed;
		if (!_built) return;
		_rounds.Value = s.Rounds; _shots.Value = s.MaxShots; _cash.Value = s.StartCash;
		_order.Selected = (int)s.Order; _air.ButtonPressed = s.AirResistance;
		_wind.ButtonPressed = s.Wind; _windEvery.Value = Math.Clamp(s.WindChangeEvery, 1, 10); SyncWind();
		RebuildPlayers(); SyncLandButtons();
	}

	public override void _Ready()
	{
		UiKit.Root(this);
		var margin = new MarginContainer().Full();
		foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 28);
		AddChild(margin);
		var outer = new VBoxContainer();
		margin.AddChild(outer);
		outer.AddChild(UiKit.Lbl("Game Setup", "Heading"));

		var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		cols.AddThemeConstantOverride("separation", 20);
		outer.AddChild(cols);

		// ---- left: players + options
		var left = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
		cols.AddChild(left);
		var pp = new PanelContainer(); left.AddChild(pp);
		var pv = new VBoxContainer(); pp.AddChild(pv);
		pv.AddChild(UiKit.Lbl("Players (2 to 4)", "Heading"));
		_playerBox = new VBoxContainer(); pv.AddChild(_playerBox);
		_addBtn = UiKit.Btn("+ Add Player", () => { if (_players.Count < 4) { _players.Add(("", PlayerType.Goldfish)); RebuildPlayers(); } });
		pv.AddChild(_addBtn);

		var op = new PanelContainer(); left.AddChild(op);
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 20);
		op.AddChild(grid);
		_rounds = Spin(grid, "Rounds", 1, 50, 1);
		_shots = Spin(grid, "Max Shots", 5, 50, 1);
		_cash = Spin(grid, "Start Cash", 0, 20000, 1000);
		grid.AddChild(UiKit.Lbl("Play Order"));
		_order = new OptionButton(); _order.AddItem("Sequential", 0); _order.AddItem("Random", 1);
		grid.AddChild(_order);
		grid.AddChild(UiKit.Lbl("Air Resistance"));
		_air = new CheckBox { Text = "Enabled" }; grid.AddChild(_air);
		grid.AddChild(UiKit.Lbl("Random Wind"));
		_wind = new CheckBox { Text = "Enabled", TooltipText = "A random wind pushes shots sideways. Its direction and strength are shown in the battle HUD." };
		_wind.Toggled += _ => SyncWind();
		grid.AddChild(_wind);
		grid.AddChild(UiKit.Lbl("Wind Changes"));
		var wr = new HBoxContainer(); wr.AddThemeConstantOverride("separation", 10);
		_windEvery = new HSlider { MinValue = 1, MaxValue = 10, Step = 1, Value = 3, CustomMinimumSize = new Vector2(150, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "How many shots pass before a new random wind is rolled." };
		_windEvery.ValueChanged += _ => SyncWind();
		_windLbl = UiKit.Lbl("", null, HorizontalAlignment.Left, UiTheme.Accent);
		wr.AddChild(_windEvery); wr.AddChild(_windLbl);
		grid.AddChild(wr);

		left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		_status = UiKit.Lbl("", "Dim"); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; left.AddChild(_status);   // must wrap, or a long message widens the column and pushes the preview off-screen

		// ---- right: landscapes + preview
		var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cols.AddChild(right);
		var lp = new PanelContainer(); right.AddChild(lp);
		var lv = new VBoxContainer(); lp.AddChild(lv);
		var lh = new HBoxContainer(); lv.AddChild(lh);
		lh.AddChild(new Label { Text = "Landscapes (at least one)", ThemeTypeVariation = "Heading", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		lh.AddChild(UiKit.Btn("All", () => { for (int i = 1; i <= 27; i++) _lands.Add(i); SyncLandButtons(); }));
		lh.AddChild(UiKit.Btn("None", () => { _lands.Clear(); _lands.Add(_focusLand); SyncLandButtons(); }));
		var lg = new GridContainer { Columns = 5 };
		lv.AddChild(lg);
		foreach (var d in LandscapeCatalog.All)
		{
			int id = d.Id;
			var b = new Button { Text = d.Name, ToggleMode = true, CustomMinimumSize = new Vector2(150, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
			b.AddThemeFontSizeOverride("font_size", 15);
			b.Toggled += on => OnLandToggled(id, on);
			b.MouseEntered += () => ShowPreviewFor(id);
			_landBtns[id] = b; lg.AddChild(b);
		}

		var prevPanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; right.AddChild(prevPanel);
		var prevV = new VBoxContainer(); prevPanel.AddChild(prevV);
		var ph = new HBoxContainer(); prevV.AddChild(ph);
		_previewName = UiKit.Lbl("Hover a landscape to preview it (drag to orbit)", "Dim");
		_previewName.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		ph.AddChild(_previewName);
		var svc = new SubViewportContainer { Stretch = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 240) };
		svc.GuiInput += OnPreviewInput;
		prevV.AddChild(svc);
		BuildPreview(svc);

		// ---- bottom bar
		var bar = new HBoxContainer();
		outer.AddChild(bar);
		bar.AddChild(UiKit.Btn("Back", () => BackRequested?.Invoke(), minW: 110));
		bar.AddChild(UiKit.Btn("Reset", () => { Apply(DefaultSettings()); _status.Text = "Reset to defaults."; }, minW: 110));
		bar.AddChild(UiKit.Btn("Save", SaveSettings, minW: 110));
		bar.AddChild(UiKit.Btn("Load", LoadSettings, minW: 110));
		bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
		_startBtn = UiKit.Btn("Start", Start, "Primary", 200);
		bar.AddChild(_startBtn);

		_built = true;
		Apply(_defaults);
		ShowPreview();
	}

	/// <summary>Online host mode: the form only configures the lobby; "Human" seats are filled by the connected players.</summary>
	public void UseForLobby()
	{
		_startBtn.Text = "Save & Back to Lobby";
		_status.Text = "Online: each Human seat is taken by one connected player (you first). Other seats are computers.";
	}

	private void SyncWind()
	{
		int n = (int)_windEvery.Value;
		_windLbl.Text = n == 1 ? "every shot" : $"every {n} shots";
		_windEvery.Editable = _wind.ButtonPressed;
		_windLbl.Modulate = _wind.ButtonPressed ? Colors.White : new Color(1, 1, 1, 0.4f);
	}

	private SpinBox Spin(GridContainer g, string label, double min, double max, double step)
	{
		g.AddChild(UiKit.Lbl(label));
		var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, CustomMinimumSize = new Vector2(150, 0) };
		g.AddChild(s);
		return s;
	}

	// ---- players ------------------------------------------------------------------------

	private static string Placeholder(int i, PlayerType t) => t == PlayerType.Human ? $"Player {i}" : $"CPU {i}";

	private void RebuildPlayers()
	{
		UiKit.Clear(_playerBox);
		for (int i = 0; i < _players.Count; i++)
		{
			int idx = i;
			var row = new HBoxContainer();
			row.AddChild(UiKit.Swatch(UiTheme.PlayerColor(i + 1), 26));
			var le = new LineEdit { Text = _players[i].name, MaxLength = 9, PlaceholderText = Placeholder(i + 1, _players[i].type), CustomMinimumSize = new Vector2(160, 0) };
			le.TextChanged += t => _players[idx] = (t, _players[idx].type);
			row.AddChild(le);
			var ob = new OptionButton { CustomMinimumSize = new Vector2(130, 0) };
			foreach (PlayerType t in Enum.GetValues<PlayerType>()) ob.AddItem(t.ToString(), (int)t);
			ob.Selected = (int)_players[i].type - 1;
			ob.ItemSelected += sel =>
			{
				_players[idx] = (_players[idx].name, (PlayerType)(sel + 1));
				le.PlaceholderText = Placeholder(idx + 1, _players[idx].type);
			};
			row.AddChild(ob);
			var rm = UiKit.Btn("Remove", () => { if (_players.Count > 2) { _players.RemoveAt(idx); RebuildPlayers(); } });
			rm.Disabled = _players.Count <= 2;
			row.AddChild(rm);
			_playerBox.AddChild(row);
		}
		_addBtn.Disabled = _players.Count >= 4;
	}

	// ---- landscapes ---------------------------------------------------------------------

	private void OnLandToggled(int id, bool on)
	{
		if (on) _lands.Add(id);
		else if (_lands.Count > 1) _lands.Remove(id);
		else { _landBtns[id].SetPressedNoSignal(true); }
		_focusLand = id;
	}

	private void SyncLandButtons()
	{
		for (int i = 1; i <= 27; i++) _landBtns[i].SetPressedNoSignal(_lands.Contains(i));
	}

	// ---- preview ------------------------------------------------------------------------

	private void BuildPreview(SubViewportContainer svc)
	{
		_vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I(640, 300) };
		svc.AddChild(_vp);
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.12f, 0.15f, 0.2f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.8f, 0.85f, 0.95f), AmbientLightEnergy = 0.8f,
		};
		_vp.AddChild(new WorldEnvironment { Environment = env });
		_vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -35, 0), LightEnergy = 1.1f });
		_terrain = new TerrainView(); _vp.AddChild(_terrain);
		_camPivot = new Node3D(); _vp.AddChild(_camPivot);
		_cam = new Camera3D { Fov = 42f, Far = 600f, Near = 0.3f, Current = true };
		_camPivot.AddChild(_cam);
		UpdateCam();
	}

	private void UpdateCam()
	{
		if (_cam == null) return;
		_camPivot.Position = new Vector3(0, 5, 0);
		_camPivot.RotationDegrees = new Vector3(-_pitch, _yaw, 0);
		_cam.Position = new Vector3(0, 0, _dist);
	}

	private void OnPreviewInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
			else if (mb.ButtonIndex == MouseButton.WheelUp) { _dist = Math.Max(30, _dist - 4); UpdateCam(); }
			else if (mb.ButtonIndex == MouseButton.WheelDown) { _dist = Math.Min(140, _dist + 4); UpdateCam(); }
		}
		else if (e is InputEventMouseMotion mm && _dragging)
		{
			_yaw -= mm.Relative.X * 0.4f; _pitch = Math.Clamp(_pitch + mm.Relative.Y * 0.3f, 8f, 85f);
			UpdateCam();
		}
	}

	public override void _Process(double delta)
	{
		if (_previewWorld != null && !_dragging) { _yaw += (float)delta * 6f; UpdateCam(); }
	}

	/// <summary>Generates the focused (last clicked) landscape into the preview viewport.</summary>
	public void ShowPreview() => ShowPreviewFor(_lands.Contains(_focusLand) ? _focusLand : _lands.Min());

	/// <summary>Generates the given landscape into the preview viewport (called on hover).</summary>
	public void ShowPreviewFor(int id)
	{
		if (!_built || id == _shownLand) return;
		_shownLand = id;
		var def = LandscapeCatalog.Get(id);
		var w = new WorldState();
		TerrainGenerator.Generate(w, def, new TpRandom(_seed));
		_previewWorld = w;
		_terrain.Build(w);
		_previewName.Text = $"{def.Name} (#{id})";
		_previewName.RemoveThemeColorOverride("font_color");
	}

	// ---- settings -----------------------------------------------------------------------

	private MatchSettings Collect()
	{
		var s = new MatchSettings
		{
			Rounds = (int)_rounds.Value, MaxShots = (int)_shots.Value, StartCash = (int)_cash.Value,
			Order = (PlayOrder)_order.Selected, AirResistance = _air.ButtonPressed, Wind = _wind.ButtonPressed, WindChangeEvery = (int)_windEvery.Value,
			Landscapes = new HashSet<int>(_lands), Seed = (uint)GD.Randi(),
			Players = new List<PlayerSetup>(),
		};
		for (int i = 0; i < _players.Count; i++)
		{
			var n = _players[i].name.Trim();
			s.Players.Add(new PlayerSetup(n.Length == 0 ? Placeholder(i + 1, _players[i].type) : n, _players[i].type));
		}
		return s;
	}

	private void Start()
	{
		if (_players.Count < 2 || _lands.Count == 0) { _status.Text = "Need 2 players and one landscape."; return; }
		StartRequested?.Invoke(Collect());
	}

	private void SaveSettings()
	{
		var s = Collect();
		var cf = new ConfigFile();
		cf.Load(UiSettings.FilePath);
		cf.SetValue(Section, "rounds", s.Rounds); cf.SetValue(Section, "max_shots", s.MaxShots); cf.SetValue(Section, "start_cash", s.StartCash);
		cf.SetValue(Section, "order", (int)s.Order); cf.SetValue(Section, "air", s.AirResistance); cf.SetValue(Section, "wind", s.Wind); cf.SetValue(Section, "wind_every", s.WindChangeEvery);
		cf.SetValue(Section, "landscapes", string.Join(",", s.Landscapes.OrderBy(x => x)));
		cf.SetValue(Section, "player_count", s.Players.Count);
		for (int i = 0; i < _players.Count; i++) { cf.SetValue(Section, $"p{i}_name", _players[i].name); cf.SetValue(Section, $"p{i}_type", (int)_players[i].type); }
		_status.Text = cf.Save(UiSettings.FilePath) == Error.Ok ? "Settings saved." : "Could not save settings.";
	}

	/// <summary>Reads settings saved by Save; returns null if none exist.</summary>
	public static MatchSettings? LoadSaved()
	{
		var cf = new ConfigFile();
		if (cf.Load(UiSettings.FilePath) != Error.Ok || !cf.HasSection(Section)) return null;
		var s = new MatchSettings
		{
			Rounds = Math.Clamp((int)cf.GetValue(Section, "rounds", 3), 1, 50),
			MaxShots = Math.Clamp((int)cf.GetValue(Section, "max_shots", 25), 5, 50),
			StartCash = Math.Clamp((int)cf.GetValue(Section, "start_cash", 2000), 0, 20000),
			Order = (PlayOrder)(int)cf.GetValue(Section, "order", 1),
			AirResistance = (bool)cf.GetValue(Section, "air", true),
			Wind = (bool)cf.GetValue(Section, "wind", true),
			WindChangeEvery = Math.Clamp((int)cf.GetValue(Section, "wind_every", 3), 1, 10),
			Players = new List<PlayerSetup>(),
		};
		s.Landscapes = new HashSet<int>(((string)cf.GetValue(Section, "landscapes", "13")).Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v is >= 1 and <= 27));
		if (s.Landscapes.Count == 0) s.Landscapes.Add(13);
		int n = Math.Clamp((int)cf.GetValue(Section, "player_count", 2), 2, 4);
		for (int i = 0; i < n; i++)
		{
			int t = Math.Clamp((int)cf.GetValue(Section, $"p{i}_type", 1), 1, 6);
			var nm = (string)cf.GetValue(Section, $"p{i}_name", "");
			s.Players.Add(new PlayerSetup(nm.Length > 9 ? nm[..9] : nm, (PlayerType)t));
		}
		return s;
	}

	private void LoadSettings()
	{
		var s = LoadSaved();
		if (s == null) { _status.Text = "No saved settings."; return; }
		Apply(s); _status.Text = "Settings loaded.";
	}
}
