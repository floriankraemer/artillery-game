using Godot;
using Artillery.Core.Ai;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Game.Camera;
using Artillery.Game.Net;
using Artillery.Game.World;
using Artillery.Game.Ui;

namespace Artillery.Game.Battle;

/// <summary>
/// One round of play: terrain, tanks, camera, sky, HUD, event sequencer (GODOT-PLAN §4). Drives the MatchEngine turn loop
/// (BeginTurn; Fire for humans / PlayComputerTurn on a worker thread for CPUs) until Phase leaves Playing, then raises <see cref="Finished"/>.
/// </summary>
public partial class BattleScene : Node3D
{
    public event Action? Finished;
    /// <summary>ESC pressed (Main shows the pause menu).</summary>
    public event Action? MenuRequested;

    private enum Req { None, SelfDestruct, EndRound }

    private MatchEngine _engine = null!;
    private TerrainView _terrain = null!;
    private CameraRig _rig = null!;
    private BattleHud _hud = null!;
    private readonly List<TankView> _views = new();
    private readonly Dictionary<int, (int hull, int shield)> _disp = new();
    private MeshInstance3D _aimLine = null!, _ghost = null!, _cursor = null!;
    private ImmediateMesh _aimMesh = new(), _ghostMesh = new();
    private WorldEnvironment _env = null!;
    private DirectionalLight3D _sun = null!;

    private NetSession? _net;
    private bool _autoplay;
    private bool _awaitingInput, _targeting, _running;
    private TaskCompletionSource<ShotInput?>? _inputTcs;
    private ShotInput? _pendingTarget;
    private Req _req = Req.None;
    private ViewPreset _viewBeforeTarget;
    private ShotInput? _cpuChoice;
    private float _shake;
    private ProjectileFlightEvent? _lastFlight;
    private bool _aimDirty;
    private int _turnsPlayed;

    /// <summary>Screenshot support: save a PNG at the first human turn after N shots, then quit.</summary>
    public string? ScreenshotPath { get; set; }
    public int ScreenshotAfterShots { get; set; } = 3;
    public bool ScreenshotInfo { get; set; }
    public ViewPreset? InitialView { get; set; }

    /// <summary>Test hook: local human turns are decided by the AI (headless online test).</summary>
    public bool AutoHuman { get; set; }
    public bool AwaitingInput => _awaitingInput;
    public MatchEngine Engine => _engine;

    public BattleScene Init(MatchEngine engine, bool autoplay = false, NetSession? net = null)
    {
        _engine = engine; _autoplay = autoplay; _net = net;
        _engine.AiDecide = (st, p) => { var r = AiPlayer.ChooseShot(st, p); _cpuChoice = r; return r; };
        return this;
    }

    public override void _Ready()
    {
        _env = new WorldEnvironment { Name = "Env" };
        AddChild(_env);
        _sun = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50, -35, 0), LightEnergy = 1.1f, ShadowEnabled = true, DirectionalShadowMaxDistance = 160f };
        AddChild(_sun);
        _terrain = new TerrainView { Name = "Terrain" };
        AddChild(_terrain);
        _rig = new CameraRig { Name = "CameraRig", Terrain = _terrain };
        AddChild(_rig);

        var lineMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _aimLine = new MeshInstance3D { Name = "AimLine", Mesh = _aimMesh, MaterialOverride = lineMat, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _ghost = new MeshInstance3D { Name = "Ghost", Mesh = _ghostMesh, MaterialOverride = lineMat, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _cursor = new MeshInstance3D
        {
            Name = "TargetCursor", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 2.0f, Rings = 24, RingSegments = 4 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.3f, 0.2f), NoDepthTest = true },
        };
        AddChild(_aimLine); AddChild(_ghost); AddChild(_cursor);

        _hud = new BattleHud { Name = "Hud" };
        AddChild(_hud);
        _hud.AimChanged += OnAimChanged;
        _hud.SelectionChanged += () => { _aimDirty = true; };
        _hud.FirePressed += OnFire;
        _hud.ReplayPressed += ReplayLast;
        _hud.ViewPressed += v => _rig.SetView(v);
        _hud.RotatePressed += d => _rig.Rotate(d);

        var s = _engine.State;
        _terrain.Build(s.World);
        ApplyEnvironment(s.World.Landscape?.PaletteScheme ?? 4);
        foreach (var t in s.AllTanks)
        {
            var v = new TankView { Name = "Tank" + t.Index }.Init(t.Index);
            AddChild(v); _views.Add(v); v.SyncFrom(t);
            _disp[t.Index] = (t.Hull, t.Shield);
        }
        if (InitialView is { } iv) _rig.SetView(iv, true);
        if (ScreenshotInfo) _hud.ToggleInfo(true);
        _running = true;
        _ = RunLoop();
    }

    public override void _ExitTree() { _running = false; }

    private void ApplyEnvironment(int scheme)
    {
        var ramp = Palette.Ramp(scheme);
        var tint = ramp[12];
        float lum = (tint.R + tint.G + tint.B) / 3f;
        var tintN = lum > 0.01f ? new Color(tint.R / lum, tint.G / lum, tint.B / lum) : Colors.White;
        var blue = new Color(0.25f, 0.42f, 0.8f);
        var top = blue.Lerp(new Color(blue.R * tintN.R, blue.G * tintN.G, blue.B * tintN.B), 0.5f);
        var horizon = new Color(0.78f, 0.84f, 0.92f).Lerp(new Color(0.9f * Mathf.Min(tintN.R, 1.3f), 0.9f * Mathf.Min(tintN.G, 1.3f), 0.9f * Mathf.Min(tintN.B, 1.3f)), 0.45f).Clamp();
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = top.Clamp(), SkyHorizonColor = horizon, GroundHorizonColor = horizon.Darkened(0.25f), GroundBottomColor = new Color(0.12f, 0.14f, 0.17f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.9f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _env.Environment = env;
        _sun.LightColor = new Color(1f, 0.97f, 0.92f).Lerp(tintN.Clamp(), 0.25f);
    }

    // ---------------------------------------------------------------- turn loop

    private async Task RunLoop()
    {
        var s = _engine.State;
        try
        {
            while (_running && s.Phase == MatchPhase.Playing)
            {
                var me = _engine.Current;
                _rig.ReleaseFollow();
                SetCurrentRing(me.Index);
                SnapshotDisplay();
                _hud.SetTurn(s, me, _disp[me.Index].hull, _disp[me.Index].shield);
                _hud.SetLocked(true);
                ClearAimLine();
                if (await HandleRequest()) break;

                var begin = _engine.BeginTurn();
                if (begin.Count > 0) { _hud.SetStatus("..."); await Sequence(begin); }
                if (s.Phase != MatchPhase.Playing) break;
                me = _engine.Current;
                SetCurrentRing(me.Index);
                SnapshotDisplay();
                _hud.SetTurn(s, me, _disp[me.Index].hull, _disp[me.Index].shield);
                _views[me.Index - 1].SetAim(me.Bearing, me.Elevation);
                if (await HandleRequest()) break;

                if (me.IsComputer || _autoplay || (ScreenshotPath != null && _turnsPlayed < ScreenshotAfterShots)) await ComputerTurn(me);
                else if (_net != null && _net.IsRemoteHuman(me.Index)) { if (!await RemoteTurn(me)) return; }
                else
                {
                    var input = AutoHuman ? TestShot(s, me) : await HumanTurn(me);
                    if (!_running) return;
                    if (input == null) { if (await HandleRequest()) break; continue; }
                    _hud.SetLocked(true); ClearAimLine(); _ghostMesh.ClearSurfaces();
                    _hud.SetStatus("");
                    _net?.Send('A', me.Index, NetSession.EncodeShot(input, NetSession.StateHash(s)));
                    await Sequence(_engine.Fire(input));
                }
                _turnsPlayed++;
                SyncAllViews();
            }
        }
        catch (Exception ex) { GD.PushError($"Battle loop failed: {ex}"); }
        if (!_running) return;
        SyncAllViews();
        _rig.ReleaseFollow();
        await Wait(0.4);
        Finished?.Invoke();
    }

    private async Task<bool> HandleRequest()
    {
        var r = _req; _req = Req.None;
        if (r == Req.None) return false;
        if (r == Req.SelfDestruct) await Sequence(_engine.SelfDestruct());
        else _engine.EndRoundNow();
        return r == Req.EndRound || _engine.State.Phase != MatchPhase.Playing;
    }

    /// <summary>Menu action from the pause overlay: destroy the current tank / end the round.</summary>
    public void RequestSelfDestruct()
    {
        if (!_awaitingInput) { _hud.Toast("Self-destruct only on your own turn"); return; }
        _net?.Send('A', _engine.CurrentPlayer, NetSession.SelfDestructMsg);
        _req = Req.SelfDestruct; AbortInput();
    }
    public void RequestEndRound() { _req = Req.EndRound; AbortInput(); }
    private void AbortInput() { if (_awaitingInput) { CancelTargeting(); _inputTcs?.TrySetResult(null); } }

    private async Task ComputerTurn(Tank me)
    {
        _hud.SetStatus("CPU thinking…");
        _cpuChoice = null;
        var ev = await Task.Run(() => _engine.PlayComputerTurn());
        _hud.SetStatus("");
        if (_cpuChoice is { } c) await AnimateAim(me, c);
        await Sequence(ev);
    }

    /// <summary>Shows a computer's / remote player's chosen aim: HUD readout and the barrel swinging into place.</summary>
    private async Task AnimateAim(Tank me, ShotInput c)
    {
        _hud.ShowAim(c.Power, c.Bearing, c.Elevation);
        var tv = _views[me.Index - 1];
        double fromB = _lastShownBearing.TryGetValue(me.Index, out var lb) ? lb : c.Bearing;
        double fromE = _lastShownElev.TryGetValue(me.Index, out var le) ? le : c.Elevation;
        double dB = ((c.Bearing - fromB + 540) % 360) - 180;
        double dur = 0.6 / Speed;
        var tw = CreateTween();
        tw.TweenMethod(Callable.From<float>(k => tv.SetAim(fromB + dB * k, fromE + (c.Elevation - fromE) * k)), 0f, 1f, dur);
        await ToSignal(tw, Tween.SignalName.Finished);
        _hud.SetStatus($"{me.Name} fires {ItemCatalog.Get(c.Weapon).Name}");
    }

    /// <summary>Online: waits for the seat's owner to send their action, then replays it on the local engine. False = session aborted.</summary>
    private async Task<bool> RemoteTurn(Tank me)
    {
        _hud.SetLocked(true); _hud.SetStatus($"Waiting for {me.Name}…");
        var msg = await _net!.Next('A', me.Index);
        if (msg == null || !_running) return false;
        _hud.SetStatus("");
        var act = NetSession.DecodeAction(msg);
        if (act == null) { _hud.Toast($"Invalid turn from {me.Name}, turn skipped"); await Sequence(_engine.Fire(new ShotInput(WeaponId.None, CasingId.Impact, 0, 0, 0))); return true; }
        var (input, hash) = act.Value;
        if (input == null) { await Sequence(_engine.SelfDestruct()); return true; }
        if (hash != NetSession.StateHash(_engine.State)) { GD.PushWarning("Online desync detected"); _hud.Toast("Warning: game state out of sync with " + me.Name, 4f); }
        await AnimateAim(me, input);
        await Sequence(_engine.Fire(input));
        return true;
    }

    private readonly Dictionary<int, double> _lastShownBearing = new(), _lastShownElev = new();

    /// <summary>Test-only human stand-in: aims at the nearest enemy, never touches the shared RNG (so peers stay in lockstep).</summary>
    private static ShotInput TestShot(MatchState s, Tank me)
    {
        var foe = s.LivingTanks.Where(t => t.Index != me.Index).OrderBy(t => (t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y)).First();
        double b = Math.Round(((360 - Math.Atan2(foe.Y - me.Y, foe.X - me.X) * 180 / Math.PI) % 360 + 360) % 360) % 360;
        return new ShotInput(WeaponId.BasicShell, CasingId.Impact, 40 + (s.TurnCounter * 7) % 50, b, 45);
    }

    private async Task<ShotInput?> HumanTurn(Tank me)
    {
        _awaitingInput = true; _hud.SetLocked(false); _hud.SetStatus("Aim and fire");
        _aimDirty = true;
        _inputTcs = new TaskCompletionSource<ShotInput?>();
        if (ScreenshotPath != null && _turnsPlayed >= ScreenshotAfterShots) _ = TakeScreenshot();
        var res = await _inputTcs.Task;
        _awaitingInput = false;
        return res;
    }

    private async Task TakeScreenshot()
    {
        for (int i = 0; i < 40; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(ScreenshotPath);
        GD.Print($"shot saved {ScreenshotPath}");
        GetTree().Quit();
    }

    private void OnFire()
    {
        if (!_awaitingInput || _hud.Locked || _targeting) return;
        var w = ItemCatalog.Get(_hud.Weapon);
        if (w.Effect is WeaponEffect.AirStrike or WeaponEffect.SatelliteAim) { BeginTargeting(); return; }
        _inputTcs?.TrySetResult(CurrentInput(null));
    }

    private ShotInput CurrentInput((int, int)? target) => new(_hud.Weapon, _hud.Casing, _hud.Power, _hud.Bearing, _hud.Elevation, target);

    private void BeginTargeting()
    {
        _targeting = true; _viewBeforeTarget = _rig.View;
        _rig.SetView(ViewPreset.Plan);
        _hud.SetStatus("Click a target cell  (Esc / right-click cancels)");
        ClearAimLine();
    }

    private void CancelTargeting()
    {
        if (!_targeting) return;
        _targeting = false; _cursor.Visible = false;
        _rig.SetView(_viewBeforeTarget);
        _hud.SetStatus("Aim and fire");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("menu"))
        {
            if (_targeting) CancelTargeting(); else MenuRequested?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_awaitingInput) return;
        if (_targeting)
        {
            if (e is InputEventMouseButton { Pressed: true } mb)
            {
                if (mb.ButtonIndex == MouseButton.Left && !_hud.OverPanel(mb.Position) && _rig.Pick(mb.Position) is { } hit)
                {
                    _targeting = false; _cursor.Visible = false;
                    _inputTcs?.TrySetResult(CurrentInput((hit.x, hit.y)));
                }
            }
            return;
        }
        if (e.IsActionPressed("fire")) { OnFire(); GetViewport().SetInputAsHandled(); }
    }

    // ---------------------------------------------------------------- aim

    private void OnAimChanged()
    {
        if (!_awaitingInput) return;
        var me = _engine.Current;
        _views[me.Index - 1].SetAim(_hud.Bearing, _hud.Elevation);
        _aimDirty = true;
    }

    private void UpdateAimLine()
    {
        _aimMesh.ClearSurfaces();
        var s = _engine.State; var me = _engine.Current;
        var w = ItemCatalog.Get(_hud.Weapon);
        if (!me.Has(UtilityId.AimComputer) || !w.Projectile || _hud.Weapon == WeaponId.SatelliteLaunch) return;
        var casing = _hud.Casing;
        double elev = casing is CasingId.Cruise or CasingId.SmartCruise ? 0 : _hud.Elevation;
        var fr = Ballistics.Fly(s, me.Index, _hud.Weapon, casing, Ballistics.LaunchPoint(s, me),
            Ballistics.LaunchVelocity(me, _hud.Power, _hud.Bearing, elev), true);
        var path = fr.Path;
        if (path.Count < 2) return;
        _aimMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var col = new Color(1f, 1f, 0.5f, 0.8f);
        for (int i = 0; i + 1 < path.Count; i += 2)
        {
            _aimMesh.SurfaceSetColor(col); _aimMesh.SurfaceAddVertex(WorldMath.ToGodot(path[i]));
            _aimMesh.SurfaceSetColor(col); _aimMesh.SurfaceAddVertex(WorldMath.ToGodot(path[i + 1]));
        }
        _aimMesh.SurfaceEnd();
    }

    private void ClearAimLine() => _aimMesh.ClearSurfaces();

    private void ReplayLast()
    {
        if (_lastFlight == null || _lastFlight.Path.Count < 2) { _hud.Toast("No shot to replay yet", 1.5f); return; }
        DrawGhost(_lastFlight);
        var pv = new ProjectileView { TrailColor = new Color(0.6f, 0.8f, 1f) };
        AddChild(pv);
        pv.Play(_lastFlight.Path, 160);
    }

    private void DrawGhost(ProjectileFlightEvent f)
    {
        _ghostMesh.ClearSurfaces();
        if (f.Path.Count < 2) return;
        _ghostMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        var col = new Color(0.6f, 0.8f, 1f, 0.35f);
        foreach (var p in f.Path) { _ghostMesh.SurfaceSetColor(col); _ghostMesh.SurfaceAddVertex(WorldMath.ToGodot(p)); }
        _ghostMesh.SurfaceEnd();
    }

    public override void _Process(double delta)
    {
        if (_awaitingInput && _aimDirty && !_targeting) { _aimDirty = false; UpdateAimLine(); }
        if (_targeting)
        {
            var mp = GetViewport().GetMousePosition();
            if (!_hud.OverPanel(mp) && _rig.Pick(mp) is { } hit)
            {
                _cursor.Visible = true;
                var (x, y) = (hit.x, hit.y);
                float h = _terrain.SampleHeight(WorldMath.ToGodot(x, y, 0).X, WorldMath.ToGodot(x, y, 0).Z);
                var g = WorldMath.ToGodot(x, y, 0);
                _cursor.Position = new Vector3(g.X, (float.IsNegativeInfinity(h) ? 0 : h) + 0.3f, g.Z);
                _cursor.RotationDegrees = new Vector3(0, 0, 0);
            }
            else _cursor.Visible = false;
        }
        if (_shake > 0.001f)
        {
            _rig.Camera.HOffset = (float)GD.RandRange(-1, 1) * _shake;
            _rig.Camera.VOffset = (float)GD.RandRange(-1, 1) * _shake;
            _shake = Mathf.Max(0, _shake - (float)delta * 2.2f);
        }
        else { _rig.Camera.HOffset = 0; _rig.Camera.VOffset = 0; }
    }

    // ---------------------------------------------------------------- event sequencer

    private float Speed => Math.Max(0.25f, (float)UiSettings.AnimationSpeed);

    private async Task Wait(double seconds)
    {
        if (!IsInsideTree()) return;
        var tw = CreateTween();
        tw.TweenInterval(Math.Max(0.001, seconds));
        await ToSignal(tw, Tween.SignalName.Finished);
    }

    private void SnapshotDisplay()
    {
        foreach (var t in _engine.State.AllTanks)
        {
            _disp[t.Index] = (t.Hull, t.Shield);
            _lastShownBearing[t.Index] = t.Bearing; _lastShownElev[t.Index] = t.Elevation;
        }
    }

    private void SyncAllViews()
    {
        foreach (var t in _engine.State.AllTanks) _views[t.Index - 1].SyncFrom(t);
        SetCurrentRing(_engine.State.Phase == MatchPhase.Playing ? _engine.CurrentPlayer : -1);
        SnapshotDisplay();
    }

    private void SetCurrentRing(int idx) { foreach (var v in _views) v.SetCurrent(v.PlayerIndex == idx); }

    private Vector3 TankPos(Core.Util.Vec3d p) => WorldMath.ToGodot(p.X + 0.5, p.Y + 0.5, p.Z - 0.5);

    /// <summary>Plays the events of one shot in order. The sim already resolved everything; this is presentation only.</summary>
    private async Task Sequence(List<GameEvent> events)
    {
        bool follow = UiSettings.FollowProjectile;
        foreach (var e in events)
        {
            if (!_running) return;
            switch (e)
            {
                case ProjectileFlightEvent pf:
                    _lastFlight = pf;
                    if (pf.Path.Count < 2) break;
                    {
                        var pv = new ProjectileView();
                        AddChild(pv);
                        if (follow) pv.Moved += p => _rig.FollowPoint(p);
                        pv.Play(pf.Path, 120 * Speed);
                        await ToSignal(pv, ProjectileView.SignalName.Finished);
                    }
                    break;
                case ExplosionEvent ex:
                    AddChild(new ExplosionView().Setup(ex.Position, ex.Radius, ex.Kind));
                    if (follow) _rig.FollowPoint(WorldMath.ToGodot(ex.Position));
                    _shake = Math.Max(_shake, Mathf.Clamp(0.03f + ex.Radius * 0.012f, 0.03f, 0.5f));
                    await Wait((0.15 + Math.Min(ex.Radius, 20) * 0.012) / Speed);
                    break;
                case TerrainChangedEvent: _terrain.Rebuild(); break;
                case TankDamagedEvent d:
                {
                    _disp[d.Tank] = (d.Hull, d.Shield);
                    var v = _views[d.Tank - 1];
                    v.SetShield(d.Shield);
                    if (d.HullDamage > 0) Float(v.Position, $"-{d.HullDamage}", new Color(1f, 0.35f, 0.3f));
                    if (d.ShieldDamage > 0) Float(v.Position + new Vector3(0, 0.6f, 0), $"-{d.ShieldDamage}", new Color(0.4f, 0.7f, 1f));
                    if (d.Tank == _engine.CurrentPlayer) _hud.SetBars(_engine.State[d.Tank], d.Hull, d.Shield);
                    await Wait(0.08 / Speed);
                    break;
                }
                case TankMovedEvent m:
                {
                    var v = _views[m.Tank - 1];
                    var to = TankPos(m.To);
                    if (m.Kind == MoveKind.Teleport) { v.Position = to; await Wait(0.15 / Speed); break; }
                    v.Position = TankPos(m.From);
                    var tw = CreateTween();
                    tw.TweenProperty(v, "position", to, Math.Max(0.05, 0.4 / Speed));
                    await ToSignal(tw, Tween.SignalName.Finished);
                    break;
                }
                case TankDiedEvent died:
                    _views[died.Tank - 1].Visible = false;
                    _disp[died.Tank] = (0, 0);
                    _shake = Math.Max(_shake, 0.25f);
                    Float(_views[died.Tank - 1].Position, "destroyed", Colors.Orange);
                    _hud.RefreshInfo(_engine.State);
                    await Wait(0.25 / Speed);
                    break;
                case FlameEvent fl:
                    AddChild(new FlameView().Setup(fl));
                    await Wait(0.9 / Speed);
                    break;
                case BeamEvent bm:
                    AddChild(new BeamView().Setup(bm.Target));
                    if (follow) _rig.FollowPoint(WorldMath.ToGodot(bm.Target));
                    await Wait(0.8 / Speed);
                    break;
                case SoundEvent snd: Sfx.Play(snd.Sound); break;
                case MessageEvent msg: _hud.Toast(msg.Text); break;
                case ScoreEvent sc:
                    if (sc.Delta != 0 && sc.Tank >= 1 && sc.Tank <= _views.Count)
                        Float(_views[sc.Tank - 1].Position + new Vector3(0, 1.4f, 0), sc.Delta > 0 ? $"+{sc.Delta}" : sc.Delta.ToString(), new Color(1f, 0.9f, 0.3f), 0.6f);
                    break;
            }
        }
        _rig.ReleaseFollow();
        await Wait(0.25 / Speed);
        _hud.RefreshInfo(_engine.State);
    }

    private void Float(Vector3 at, string text, Color color, float size = 1f)
    {
        var l = new Label3D
        {
            Text = text, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 72, PixelSize = 0.012f * size,
            Modulate = color, OutlineModulate = Colors.Black, OutlineSize = 14, Position = at + new Vector3(0, 2.2f, 0), TopLevel = true,
        };
        AddChild(l);
        var tw = l.CreateTween().SetParallel();
        float dur = 1.4f / Speed;
        tw.TweenProperty(l, "position:y", l.Position.Y + 2.5f, dur);
        tw.TweenProperty(l, "modulate:a", 0f, dur).SetDelay(dur * 0.5);
        tw.Chain().TweenCallback(Callable.From(l.QueueFree));
    }
}
