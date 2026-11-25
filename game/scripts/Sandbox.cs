using Godot;
using Artillery.Core;
using Artillery.Core.Combat;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Core.World;
using Artillery.Game.Camera;
using Artillery.Game.World;

namespace Artillery.Game;

/// <summary>
/// Standalone world-view sandbox. Keys: 1-9 landscape (Shift +9, Ctrl +18), N/B next/previous, R new seed, Tab next tank, Space fire,
/// V view, Q/E rotate, right-drag orbit, wheel zoom, left click = fire at the clicked cell.
/// Command line (after "--"): --lid=N --seed=N --view=side|iso|plan --shot=ABS.png --frames=N --fire
/// </summary>
public partial class Sandbox : Node3D
{
    private MatchState _state = null!;
    private TerrainView _terrain = null!;
    private CameraRig _rig = null!;
    private readonly List<TankView> _tanks = new();
    private Label _hud = null!;
    private int _lid = 14;
    private uint _seed = 12345;
    private bool _busy;
    private string? _shotPath;
    private int _shotFrames = 30, _frame;
    private bool _autoFire;
    private double _lastRebuildMs;

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.42f, 0.52f, 0.62f),
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = new Color(0.25f, 0.4f, 0.75f), SkyHorizonColor = new Color(0.7f, 0.8f, 0.9f), GroundHorizonColor = new Color(0.55f, 0.6f, 0.65f), GroundBottomColor = new Color(0.15f, 0.17f, 0.2f) } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.9f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50, -35, 0), LightEnergy = 1.1f, ShadowEnabled = true, DirectionalShadowMaxDistance = 160f });

        _terrain = new TerrainView { Name = "Terrain" };
        AddChild(_terrain);
        _rig = new CameraRig { Name = "CameraRig", Terrain = _terrain };
        AddChild(_rig);
        var layer = new CanvasLayer { Name = "HUD" };
        _hud = new Label { Position = new Vector2(16, 12) };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hud.AddThemeConstantOverride("shadow_offset_x", 1); _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(_hud); AddChild(layer);

        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--lid=")) _lid = int.Parse(a[6..]);
            else if (a.StartsWith("--seed=")) _seed = uint.Parse(a[7..]);
            else if (a.StartsWith("--shot=")) _shotPath = a[7..];
            else if (a.StartsWith("--frames=")) _shotFrames = int.Parse(a[9..]);
            else if (a == "--fire") _autoFire = true;
            else if (a.StartsWith("--view=")) _rig.SetView(Enum.Parse<ViewPreset>(a[7..], true), true);
        }
        LoadLandscape(_lid);
        if (_autoFire) Fire();
    }

    private void LoadLandscape(int id)
    {
        _lid = ((id - 1) % 27 + 27) % 27 + 1;
        var def = LandscapeCatalog.Get(_lid);
        var settings = new MatchSettings { Seed = _seed, Players = Enumerable.Range(1, 4).Select(i => new PlayerSetup("Player " + i, PlayerType.Human)).ToList() };
        _state = new MatchState(settings);
        for (int i = 1; i <= 4; i++)
            _state.Tanks.Add(new Tank { Index = i, Name = "Player " + i, Hull = Balance.BaseHull, Shield = i == 2 ? 60 : 0, Elevation = 45 });
        _state.Order.AddRange(new[] { 1, 2, 3, 4 });
        TerrainGenerator.Generate(_state.World, def, _state.Rng);
        TankPlacement.Place(_state);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _terrain.Build(_state.World);
        sw.Stop();
        _lastRebuildMs = sw.Elapsed.TotalMilliseconds;

        foreach (var t in _tanks) t.QueueFree();
        _tanks.Clear();
        foreach (var t in _state.AllTanks)
        {
            var v = new TankView { Name = "Tank" + t.Index }.Init(t.Index);
            AddChild(v); _tanks.Add(v);
        }
        AimAtNearest();
        SyncTanks();
        UpdateHud();
    }

    private void SyncTanks()
    {
        foreach (var t in _state.AllTanks) { _tanks[t.Index - 1].SyncFrom(t); _tanks[t.Index - 1].SetCurrent(t.Index == _state.CurrentPlayer); }
    }

    private Tank? Nearest(Tank me)
    {
        Tank? best = null; double bd = double.MaxValue;
        foreach (var t in _state.LivingTanks)
        {
            if (t == me) continue;
            double d = (t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    /// <summary>Bearing (deg) from a tank toward a point: launch direction is (cos b, -sin b) in sim (x, y).</summary>
    private static double BearingTo(double sx, double sy, double tx, double ty)
    {
        double b = Math.Atan2(-(ty - sy), tx - sx) * 180.0 / Math.PI;
        return (b % 360 + 360) % 360;
    }

    private void AimAtNearest()
    {
        foreach (var me in _state.AllTanks)
            if (Nearest(me) is { } en) me.Bearing = BearingTo(me.X, me.Y, en.X, en.Y);
    }

    private void Fire(Vector2I? cell = null)
    {
        if (_busy) return;
        var me = _state[_state.CurrentPlayer];
        double bearing;
        if (cell is { } c) bearing = BearingTo(me.X, me.Y, c.X, c.Y);
        else if (Nearest(me) is { } en) bearing = BearingTo(me.X, me.Y, en.X, en.Y);
        else return;
        me.Bearing = bearing; me.Power = 70; me.Elevation = 45;
        var ev = new List<GameEvent>();
        var vel = Ballistics.LaunchVelocity(me, 70, bearing, 45);
        var res = Ballistics.Fly(_state, me.Index, WeaponId.BasicShell, CasingId.Impact, Ballistics.LaunchPoint(_state, me), vel, true);
        ev.Add(new ProjectileFlightEvent(me.Index, WeaponId.BasicShell, CasingId.Impact, res.Path, res.OffMap));
        if (!res.OffMap)
        {
            var def = ItemCatalog.Get(WeaponId.BasicShell);
            Blast.StandardBlast(_state, me.Index, def, res.Impact, def.Radius, ev);
            Blast.Crater(_state, res.Impact, def.Radius, ev);
            TankPhysics.Settle(_state, ev);
        }
        Replay(ev);
    }

    private async void Replay(List<GameEvent> events)
    {
        _busy = true;
        foreach (var e in events)
        {
            switch (e)
            {
                case ProjectileFlightEvent pf:
                {
                    var pv = new ProjectileView();
                    AddChild(pv);
                    pv.Moved += p => _rig.FollowPoint(p);
                    pv.Play(pf.Path, 160);
                    await ToSignal(pv, ProjectileView.SignalName.Finished);
                    break;
                }
                case ExplosionEvent ex:
                    AddChild(new ExplosionView().Setup(ex.Position, ex.Radius, ex.Kind));
                    _rig.FollowPoint(WorldMath.ToGodot(ex.Position));
                    await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
                    break;
                case TerrainChangedEvent:
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _terrain.Rebuild();
                    sw.Stop();
                    _lastRebuildMs = sw.Elapsed.TotalMilliseconds;
                    break;
                }
                case TankMovedEvent or TankDamagedEvent or TankDiedEvent:
                    SyncTanks();
                    break;
            }
        }
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        _rig.ReleaseFollow();
        SyncTanks();
        UpdateHud();
        _busy = false;
    }

    private void UpdateHud()
    {
        var d = LandscapeCatalog.Get(_lid);
        _hud.Text = $"Landscape {_lid}/27: {d.Name}  (seed {_seed}, palette {d.PaletteScheme}, terrain rebuild {_lastRebuildMs:0.0} ms)\n"
                  + $"Current: {_state[_state.CurrentPlayer].Name} (Tab)\n"
                  + "1-9 landscape (Shift +9, Ctrl +18)  N/B next/prev  R reseed  Space/click fire  V view  Q/E or right-drag orbit  wheel zoom";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            int digit = (int)(k.Keycode - Key.Key0);
            if (digit is >= 1 and <= 9) { LoadLandscape(digit + (k.ShiftPressed ? 9 : 0) + (k.CtrlPressed ? 18 : 0)); return; }
            switch (k.Keycode)
            {
                case Key.N: LoadLandscape(_lid + 1); return;
                case Key.B: LoadLandscape(_lid - 1); return;
                case Key.R: _seed = _seed * 1664525u + 1013904223u; LoadLandscape(_lid); return;
                case Key.Tab: _state.OrderPos = (_state.OrderPos + 1) % _state.Order.Count; SyncTanks(); UpdateHud(); return;
            }
        }
        if (e.IsActionPressed("fire")) Fire();
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && _rig.Pick(mb.Position) is { } hit) Fire(new Vector2I(hit.x, hit.y));
    }

    public override void _Process(double delta)
    {
        if (_shotPath == null) return;
        if (_busy) _frame = 0;
        if (++_frame >= _shotFrames && !_busy)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(_shotPath);
            GD.Print($"shot saved {_shotPath} rebuild_ms={_lastRebuildMs:0.00}");
            GetTree().Quit();
        }
    }
}
