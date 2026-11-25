using Godot;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Game.Camera;
using Artillery.Game.World;

namespace Artillery.Game.Battle;

/// <summary>In-battle HUD (GODOT-PLAN §4): top bar, current player panel + weapon/casing selectors, aim panel, view/replay/info panel, toast.</summary>
public partial class BattleHud : CanvasLayer
{
    public event Action? AimChanged, FirePressed, ReplayPressed, SelectionChanged;
    public event Action<ViewPreset>? ViewPressed;
    public event Action<float>? RotatePressed;

    public double Power { get; private set; } = 50;
    public double Bearing { get; private set; }
    public double Elevation { get; private set; } = 45;
    public WeaponId Weapon { get; private set; } = WeaponId.BasicShell;
    public CasingId Casing { get; private set; } = CasingId.Impact;
    public bool Locked { get; private set; } = true;

    private Label _top = null!, _name = null!, _status = null!, _toast = null!, _power = null!, _bearing = null!, _elev = null!;
    private ColorRect _swatch = null!;
    private ProgressBar _hull = null!, _shield = null!;
    private Label _hullLbl = null!, _shieldLbl = null!;
    private OptionButton _weapons = null!, _casings = null!;
    private HSlider _sPower = null!, _sBearing = null!, _sElev = null!;
    private Button _fire = null!, _replay = null!;
    private PanelContainer _info = null!;
    private GridContainer _infoGrid = null!;
    private Control _left = null!, _aim = null!, _right = null!;
    private readonly List<Button> _buttons = new();
    private bool _updating;
    private int _minElev = 0, _maxElev = 80;
    private Tween? _toastTween;

    // key / button hold repeat
    private Action<int>? _holdStep; private int _holdDir; private double _holdT; private bool _holdStarted;
    private readonly Dictionary<string, double> _keyHold = new();

    public override void _Ready()
    {
        Layer = 5;
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // top bar
        var topP = Panel(); root.AddChild(topP);
        topP.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        topP.GrowHorizontal = Control.GrowDirection.Both; topP.OffsetTop = 6;
        _top = Lbl("", 18); topP.GetChild<MarginContainer>(0).AddChild(_top);

        // left panel
        _left = Panel(); root.AddChild(_left);
        _left.Position = new Vector2(10, 10); _left.CustomMinimumSize = new Vector2(250, 0);
        var lv = VBox(_left);
        var hb = new HBoxContainer(); lv.AddChild(hb);
        _swatch = new ColorRect { CustomMinimumSize = new Vector2(16, 24), Color = Colors.White }; hb.AddChild(_swatch);
        _name = Lbl("Player", 22); hb.AddChild(_name);
        lv.AddChild(Lbl("Hull", 13)); _hull = Bar(new Color(0.35f, 0.8f, 0.3f), out _hullLbl); lv.AddChild(_hull);
        lv.AddChild(Lbl("Shield", 13)); _shield = Bar(new Color(0.35f, 0.6f, 1f), out _shieldLbl); lv.AddChild(_shield);
        lv.AddChild(Lbl("Weapon", 13)); _weapons = Opt(); lv.AddChild(_weapons);
        lv.AddChild(Lbl("Casing", 13)); _casings = Opt(); lv.AddChild(_casings);
        _weapons.ItemSelected += i => { Weapon = (WeaponId)(int)_weapons.GetItemMetadata((int)i); SelectionChanged?.Invoke(); };
        _casings.ItemSelected += i => { Casing = (CasingId)(int)_casings.GetItemMetadata((int)i); SelectionChanged?.Invoke(); };

        // aim panel
        _aim = Panel(); root.AddChild(_aim);
        _aim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _aim.GrowHorizontal = Control.GrowDirection.Both; _aim.GrowVertical = Control.GrowDirection.Begin; _aim.OffsetBottom = -10;
        var av = VBox(_aim);
        _status = Lbl("", 16); _status.HorizontalAlignment = HorizontalAlignment.Center; av.AddChild(_status);
        AimRow(av, "Power", 0, 100, out _sPower, out _power, d => SetPower(Power + d), v => SetPower(v));
        AimRow(av, "Bearing", 0, 359, out _sBearing, out _bearing, d => SetBearing(Bearing + d), v => SetBearing(v));
        AimRow(av, "Elevation", 0, 80, out _sElev, out _elev, d => SetElevation(Elevation + d), v => SetElevation(v));
        _fire = Btn("FIRE  [Space]", () => FirePressed?.Invoke()); _fire.CustomMinimumSize = new Vector2(0, 40);
        _fire.AddThemeFontSizeOverride("font_size", 22); av.AddChild(_fire);

        // right panel
        _right = Panel(); root.AddChild(_right);
        _right.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _right.GrowHorizontal = Control.GrowDirection.Begin; _right.OffsetTop = 10; _right.OffsetRight = -10;
        var rv = VBox(_right);
        rv.AddChild(Lbl("View", 13));
        var vr = new HBoxContainer(); rv.AddChild(vr);
        vr.AddChild(Btn("Side", () => ViewPressed?.Invoke(ViewPreset.Side)));
        vr.AddChild(Btn("Iso", () => ViewPressed?.Invoke(ViewPreset.Iso)));
        vr.AddChild(Btn("Plan", () => ViewPressed?.Invoke(ViewPreset.Plan)));
        var rr = new HBoxContainer(); rv.AddChild(rr);
        var rl = Btn("<< Rot", null); var rrb = Btn("Rot >>", null);
        rl.ButtonDown += () => StartHold(_ => RotatePressed?.Invoke(-6), 1); rl.ButtonUp += StopHold;
        rrb.ButtonDown += () => StartHold(_ => RotatePressed?.Invoke(6), 1); rrb.ButtonUp += StopHold;
        rr.AddChild(rl); rr.AddChild(rrb);
        _replay = Btn("Replay last shot", () => ReplayPressed?.Invoke()); rv.AddChild(_replay);
        rv.AddChild(Btn("Info (tanks)", () => _info.Visible = !_info.Visible));

        _info = Panel(); root.AddChild(_info); _info.Visible = false;
        _info.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _info.GrowHorizontal = Control.GrowDirection.Begin; _info.OffsetTop = 190; _info.OffsetRight = -10;
        _infoGrid = new GridContainer { Columns = 5 }; _infoGrid.AddThemeConstantOverride("h_separation", 12); _info.GetChild<MarginContainer>(0).AddChild(_infoGrid);

        _toast = Lbl("", 26); _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.Modulate = new Color(1, 1, 1, 0);
        _toast.AddThemeColorOverride("font_outline_color", Colors.Black); _toast.AddThemeConstantOverride("outline_size", 6);
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_toast);
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.GrowHorizontal = Control.GrowDirection.Both; _toast.OffsetTop = 70;
        SetLocked(true);
    }

    // ---- builders ----
    private static PanelContainer Panel()
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.1f, 0.78f), BorderColor = new Color(0.4f, 0.45f, 0.55f, 0.9f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
        });
        var m = new MarginContainer(); foreach (var s in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + s, 8);
        p.AddChild(m); return p;
    }
    private static VBoxContainer VBox(Control panel) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 3); panel.GetChild<MarginContainer>(0).AddChild(v); return v; }
    private static Label Lbl(string t, int size) { var l = new Label { Text = t }; l.AddThemeFontSizeOverride("font_size", size); return l; }
    private static OptionButton Opt() => new() { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 28), Alignment = HorizontalAlignment.Left };
    private Button Btn(string text, Action? a)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        if (a != null) b.Pressed += a;
        _buttons.Add(b); return b;
    }
    private static ProgressBar Bar(Color c, out Label lbl)
    {
        var b = new ProgressBar { CustomMinimumSize = new Vector2(0, 18), ShowPercentage = false, MaxValue = 100 };
        b.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = c });
        lbl = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lbl.AddThemeFontSizeOverride("font_size", 12); lbl.AddThemeColorOverride("font_outline_color", Colors.Black); lbl.AddThemeConstantOverride("outline_size", 4);
        lbl.SetAnchorsPreset(Control.LayoutPreset.FullRect); b.AddChild(lbl); return b;
    }

    private void AimRow(VBoxContainer parent, string name, double min, double max, out HSlider slider, out Label value, Action<int> step, Action<double> set)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); parent.AddChild(row);
        var n = Lbl(name, 16); n.CustomMinimumSize = new Vector2(80, 0); row.AddChild(n);
        var minus = Btn("-", null); minus.CustomMinimumSize = new Vector2(34, 0); row.AddChild(minus);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = 1, CustomMinimumSize = new Vector2(300, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None };
        row.AddChild(s);
        var plus = Btn("+", null); plus.CustomMinimumSize = new Vector2(34, 0); row.AddChild(plus);
        var v = Lbl("0", 18); v.CustomMinimumSize = new Vector2(50, 0); v.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(v);
        minus.ButtonDown += () => StartHold(step, -1); minus.ButtonUp += StopHold;
        plus.ButtonDown += () => StartHold(step, +1); plus.ButtonUp += StopHold;
        s.ValueChanged += x => { if (!_updating) set(x); };
        slider = s; value = v;
    }

    // ---- hold repeat ----
    private void StartHold(Action<int> step, int dir)
    {
        _holdStep = step; _holdDir = dir; _holdT = 0; _holdStarted = false;
        step(dir * (Input.IsKeyPressed(Godot.Key.Shift) ? 10 : 1));
    }
    private void StopHold() => _holdStep = null;

    public override void _Process(double delta)
    {
        if (_holdStep != null)
        {
            _holdT += delta;
            if (!_holdStarted && _holdT > 0.35) { _holdStarted = true; _holdT = 0; }
            else if (_holdStarted && _holdT > 0.05) { _holdT = 0; _holdStep(_holdDir * (Input.IsKeyPressed(Godot.Key.Shift) ? 10 : 1)); }
        }
        if (Locked) return;
        int k = Input.IsKeyPressed(Godot.Key.Shift) ? 10 : 1;
        KeyRepeat("aim_power_up", () => SetPower(Power + k), delta); KeyRepeat("aim_power_down", () => SetPower(Power - k), delta);
        KeyRepeat("aim_bearing_left", () => SetBearing(Bearing - k), delta); KeyRepeat("aim_bearing_right", () => SetBearing(Bearing + k), delta);
        KeyRepeat("aim_elev_up", () => SetElevation(Elevation + k), delta); KeyRepeat("aim_elev_down", () => SetElevation(Elevation - k), delta);
    }

    private void KeyRepeat(string action, Action step, double delta)
    {
        if (!Input.IsActionPressed(action)) { _keyHold.Remove(action); return; }
        if (!_keyHold.TryGetValue(action, out var t)) { step(); _keyHold[action] = 0; return; }
        t += delta; _keyHold[action] = t;
        if (t > 0.35) { step(); _keyHold[action] = 0.30; }
    }

    // ---- state setters ----
    public void SetPower(double v) { Power = Math.Clamp(Math.Round(v), 0, 100); RefreshAim(); AimChanged?.Invoke(); }
    public void SetBearing(double v) { Bearing = ((Math.Round(v) % 360) + 360) % 360; RefreshAim(); AimChanged?.Invoke(); }
    public void SetElevation(double v) { Elevation = Math.Clamp(Math.Round(v), _minElev, _maxElev); RefreshAim(); AimChanged?.Invoke(); }

    private void RefreshAim()
    {
        _updating = true;
        _sPower.Value = Power; _sBearing.Value = Bearing; _sElev.MinValue = _minElev; _sElev.MaxValue = _maxElev; _sElev.Value = Elevation;
        _power.Text = $"{Power:0}"; _bearing.Text = $"{Bearing:0}°"; _elev.Text = $"{Elevation:0}°";
        _updating = false;
    }

    /// <summary>Sets the aim values without raising events (e.g. when a computer picks its shot).</summary>
    public void ShowAim(double power, double bearing, double elevation)
    {
        Power = power; Bearing = bearing; Elevation = elevation; RefreshAim();
    }

    /// <summary>Populates the panels for the current player. Hull/shield bars use the supplied display values.</summary>
    public void SetTurn(MatchState s, Tank t, int hull, int shield)
    {
        _name.Text = t.Name + (t.IsComputer ? $"  (CPU {t.Type})" : "");
        _swatch.Color = WorldMath.PlayerColor(t.Index);
        _minElev = t.MinElevation; _maxElev = t.MaxElevation;
        _top.Text = $"Round {s.Round}/{s.Settings.Rounds}     Shot {Math.Min(s.TurnCounter, s.Settings.MaxShots)}/{s.Settings.MaxShots}     {s.World.Landscape?.Name}" + WindText(s);
        _weapons.Clear();
        int sel = 0;
        for (int i = 1; i < t.Weapons.Length; i++)
        {
            int c = t.Weapons[i];
            if (c <= 0 || !Enum.IsDefined((WeaponId)i)) continue;
            var w = ItemCatalog.Get((WeaponId)i);
            _weapons.AddItem($"{w.Name}  x {(c == Tank.Infinite ? "∞" : c.ToString())}");
            _weapons.SetItemMetadata(_weapons.ItemCount - 1, i);
            if ((WeaponId)i == t.SelectedWeapon) sel = _weapons.ItemCount - 1;
        }
        if (_weapons.ItemCount > 0) { _weapons.Select(sel); Weapon = (WeaponId)(int)_weapons.GetItemMetadata(sel); } else Weapon = WeaponId.BasicShell;
        _casings.Clear(); sel = 0;
        for (int i = 1; i < t.Casings.Length; i++)
        {
            int c = t.Casings[i];
            if (!Enum.IsDefined((CasingId)i) || (c <= 0 && (CasingId)i != CasingId.Impact)) continue;
            _casings.AddItem($"{ItemCatalog.Get((CasingId)i).Name}  x {(c == Tank.Infinite || (CasingId)i == CasingId.Impact ? "∞" : c.ToString())}");
            _casings.SetItemMetadata(_casings.ItemCount - 1, i);
            if ((CasingId)i == t.SelectedCasing) sel = _casings.ItemCount - 1;
        }
        _casings.Select(sel); Casing = (CasingId)(int)_casings.GetItemMetadata(sel);
        ShowAim(t.Power, t.Bearing, Math.Clamp(t.Elevation, _minElev, _maxElev));
        SetBars(t, hull, shield);
        RefreshInfo(s);
    }

    /// <summary>Wind readout for the top line: strength and the bearing it pushes toward (same bearing scale as aiming).</summary>
    private static string WindText(MatchState s) =>
        !s.Settings.Wind ? "" : s.WindStrength <= 0 ? "     Wind: calm" : $"     Wind: {s.WindStrength} toward {s.WindBearing}°";

    public void SetBars(Tank t, int hull, int shield)
    {
        _hull.MaxValue = Math.Max(1, t.MaxHull); _hull.Value = Math.Max(0, hull); _hullLbl.Text = $"{Math.Max(0, hull)} / {t.MaxHull}";
        _shield.MaxValue = Math.Max(100, shield); _shield.Value = Math.Max(0, shield); _shieldLbl.Text = $"{Math.Max(0, shield)}";
    }

    public void SetLocked(bool locked)
    {
        Locked = locked;
        _fire.Disabled = locked; _weapons.Disabled = locked; _casings.Disabled = locked;
        foreach (var sl in new[] { _sPower, _sBearing, _sElev }) sl.Editable = !locked;
        _holdStep = null;
        foreach (var b in _buttons) if (b.Text is "-" or "+") b.Disabled = locked;
    }

    public void SetStatus(string text) => _status.Text = text;

    public void Toast(string text, float seconds = 2.5f)
    {
        _toast.Text = text; _toast.Modulate = Colors.White;
        _toastTween?.Kill();
        _toastTween = CreateTween(); _toastTween.TweenInterval(seconds); _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.5);
    }

    public void ToggleInfo(bool on) => _info.Visible = on;

    public void RefreshInfo(MatchState s)
    {
        foreach (var c in _infoGrid.GetChildren()) c.QueueFree();
        foreach (var h in new[] { "Player", "Hull", "Shield", "Round", "Total" }) _infoGrid.AddChild(Lbl(h, 14));
        foreach (var t in s.AllTanks)
        {
            var n = Lbl(t.Name, 14); n.AddThemeColorOverride("font_color", WorldMath.PlayerColor(t.Index, true)); _infoGrid.AddChild(n);
            _infoGrid.AddChild(Lbl(t.Alive ? t.Hull.ToString() : "dead", 14));
            _infoGrid.AddChild(Lbl(t.Alive ? t.Shield.ToString() : "-", 14));
            _infoGrid.AddChild(Lbl(t.RoundScore.ToString(), 14));
            _infoGrid.AddChild(Lbl(t.TotalScore.ToString(), 14));
        }
    }

    /// <summary>True when the mouse is over a HUD panel (so world clicks are ignored).</summary>
    public bool OverPanel(Vector2 p)
    {
        foreach (var c in new[] { _left, _aim, _right, _info }) if (c.Visible && c.GetGlobalRect().HasPoint(p)) return true;
        return false;
    }
}
