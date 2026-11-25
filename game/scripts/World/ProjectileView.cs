using Godot;
using Artillery.Core.Util;

namespace Artillery.Game.World;

/// <summary>Replays a ProjectileFlightEvent path over time with a fading trail. Emits Finished when the last step is reached.</summary>
public partial class ProjectileView : Node3D
{
    [Signal] public delegate void FinishedEventHandler();
    /// <summary>Emitted each frame with the current world position (for camera follow).</summary>
    [Signal] public delegate void MovedEventHandler(Vector3 position);

    /// <summary>Simulation steps replayed per second.</summary>
    [Export] public double StepsPerSecond { get; set; } = 120;
    [Export] public Color TrailColor { get; set; } = new(1f, 0.85f, 0.4f);
    /// <summary>Free the node after the trail faded.</summary>
    [Export] public bool AutoFree { get; set; } = true;

    private Vector3[] _pts = Array.Empty<Vector3>();
    private double _pos;
    private bool _playing;
    private float _fade;
    private MeshInstance3D _body = null!, _trail = null!;
    private ImmediateMesh _im = new();

    public Vector3 CurrentPosition => _body.Position;

    public override void _Ready()
    {
        _body = new MeshInstance3D { Name = "Shell", Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 8, Rings = 4 }, Visible = false,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.95f, 0.7f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _trail = new MeshInstance3D { Name = "Trail", Mesh = _im, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
        AddChild(_body); AddChild(_trail);
    }

    /// <summary>Starts playback of a sim path (sim coordinates).</summary>
    public void Play(IReadOnlyList<Vec3d> path, double? stepsPerSecond = null)
    {
        if (stepsPerSecond is { } s) StepsPerSecond = s;
        _pts = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++) _pts[i] = WorldMath.ToGodot(path[i]);
        _pos = 0; _fade = 0; _playing = _pts.Length > 0;
        if (!_playing) { EmitSignal(SignalName.Finished); if (AutoFree) QueueFree(); return; }
        _body.Visible = true; _body.Position = _pts[0];
    }

    public override void _Process(double delta)
    {
        if (_fade > 0)
        {
            _fade -= (float)delta;
            DrawTrail(_pts.Length, Mathf.Clamp(_fade / 1.2f, 0, 1));
            if (_fade <= 0) { _im.ClearSurfaces(); if (AutoFree) QueueFree(); }
            return;
        }
        if (!_playing) return;
        _pos += delta * StepsPerSecond;
        int idx = (int)_pos;
        if (idx >= _pts.Length - 1)
        {
            _body.Position = _pts[^1]; _body.Visible = false; _playing = false;
            DrawTrail(_pts.Length, 1f);
            EmitSignal(SignalName.Moved, _pts[^1]);
            _fade = 1.2f;
            EmitSignal(SignalName.Finished);
            return;
        }
        var cur = _pts[idx].Lerp(_pts[idx + 1], (float)(_pos - idx));
        _body.Position = cur;
        DrawTrail(idx + 1, 1f, cur);
        EmitSignal(SignalName.Moved, cur);
    }

    private void DrawTrail(int count, float alpha, Vector3? head = null)
    {
        _im.ClearSurfaces();
        int n = Math.Min(count, _pts.Length);
        if (n < 1) return;
        _im.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        int start = Math.Max(0, n - 600);
        for (int i = start; i < n; i++)
        {
            float k = (float)(i - start) / Math.Max(1, n - start);
            _im.SurfaceSetColor(new Color(TrailColor, alpha * (0.15f + 0.85f * k)));
            _im.SurfaceAddVertex(_pts[i]);
        }
        if (head is { } h) { _im.SurfaceSetColor(new Color(TrailColor, alpha)); _im.SurfaceAddVertex(h); }
        _im.SurfaceEnd();
    }
}
