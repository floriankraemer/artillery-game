using Godot;
using Artillery.Game.World;

namespace Artillery.Game.Camera;

public enum ViewPreset { Side, Iso, Plan }

/// <summary>
/// Orbit camera rig (GODOT-PLAN section 4): Side / Iso / Plan(orthographic top-down) presets, Q/E (cam_rotate_left/right) and right-mouse-drag orbit,
/// wheel zoom, V (cam_view_cycle) cycles views, <see cref="FollowPoint"/> for shot follow and <see cref="Pick"/> for target clicking.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Camera { get; private set; } = null!;
    /// <summary>Terrain used by <see cref="Pick"/>.</summary>
    public TerrainView? Terrain { get; set; }
    public ViewPreset View { get; private set; } = ViewPreset.Iso;
    /// <summary>Point the rig orbits around (map centre by default).</summary>
    public Vector3 Focus { get; set; } = new(0, 6f, 0);
    [Export] public float RotateSpeedDeg { get; set; } = 70f;
    [Export] public bool HandleInput { get; set; } = true;

    private float _yaw, _pitch = 38f, _dist = 74f;                // current (deg, deg, units)
    private float _tYaw, _tPitch = 38f, _tDist = 74f;             // targets
    private Vector3 _focusCur = new(0, 6f, 0);
    private Vector3? _follow;
    private bool _dragging;

    private static (float pitch, float dist) Preset(ViewPreset v) => v switch
    {
        ViewPreset.Side => (13f, 82f),
        ViewPreset.Iso => (38f, 74f),
        _ => (90f, 60f),
    };

    public override void _Ready()
    {
        Camera = new Camera3D { Name = "Camera3D", Current = true, Fov = 42f, Far = 600f, Near = 0.3f };
        AddChild(Camera);
        SetView(View, true);
    }

    /// <summary>Switch preset (yaw is kept; Plan is orthographic).</summary>
    public void SetView(ViewPreset v, bool instant = false)
    {
        View = v;
        (_tPitch, _tDist) = Preset(v);
        Camera.Projection = v == ViewPreset.Plan ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
        if (instant) { _pitch = _tPitch; _dist = _tDist; _focusCur = Focus; _yaw = _tYaw; }
        Apply();
    }

    public void CycleView() => SetView((ViewPreset)(((int)View + 1) % 3));
    public void Rotate(float deg) => _tYaw += deg;
    /// <summary>Follow a world point (e.g. a projectile) with the orbit focus. Call every frame while following.</summary>
    public void FollowPoint(Vector3 p) => _follow = p;
    public void ReleaseFollow() => _follow = null;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!HandleInput) return;
        if (e.IsActionPressed("cam_view_cycle")) { CycleView(); return; }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _dragging = mb.Pressed;
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _tDist = Mathf.Clamp(_tDist * 0.9f, 12f, 160f);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _tDist = Mathf.Clamp(_tDist * 1.1f, 12f, 160f);
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            _tYaw -= mm.Relative.X * 0.4f;
            if (View != ViewPreset.Plan) _tPitch = Mathf.Clamp(_tPitch + mm.Relative.Y * 0.3f, 4f, 85f);
        }
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        if (HandleInput)
        {
            if (Input.IsActionPressed("cam_rotate_left")) _tYaw -= RotateSpeedDeg * d;
            if (Input.IsActionPressed("cam_rotate_right")) _tYaw += RotateSpeedDeg * d;
        }
        float k = 1f - Mathf.Exp(-8f * d);
        _yaw = Mathf.Lerp(_yaw, _tYaw, k);
        _pitch = Mathf.Lerp(_pitch, _tPitch, k);
        _dist = Mathf.Lerp(_dist, _tDist, k);
        var f = _follow ?? Focus;
        _focusCur = _focusCur.Lerp(f, 1f - Mathf.Exp(-(_follow.HasValue ? 12f : 6f) * d));
        Apply();
    }

    private void Apply()
    {
        if (Camera == null) return;
        Position = _focusCur;
        Rotation = new Vector3(Mathf.DegToRad(-_pitch), Mathf.DegToRad(_yaw), 0);
        Camera.Position = new Vector3(0, 0, _dist);
        if (Camera.Projection == Camera3D.ProjectionType.Orthogonal) Camera.Size = _dist * 0.95f;
    }

    /// <summary>Grid cell (sim x row, y column, 1..50) under a screen position, found by marching the camera ray against the heightfield.</summary>
    public (int x, int y)? Pick(Vector2 screenPos)
    {
        if (Terrain?.World == null) return null;
        var o = Camera.ProjectRayOrigin(screenPos);
        var dir = Camera.ProjectRayNormal(screenPos);
        float prevT = 0, t = 0;
        const float step = 0.25f, max = 400f;
        for (; t < max; t += step)
        {
            var p = o + dir * t;
            float gh = Terrain.SampleHeight(p.X, p.Z);
            if (!float.IsNegativeInfinity(gh) && p.Y <= gh)
            {
                float a = prevT, b = t; // bisect for precision
                for (int i = 0; i < 8; i++)
                {
                    float m = (a + b) * 0.5f; var q = o + dir * m;
                    float hq = Terrain.SampleHeight(q.X, q.Z);
                    if (!float.IsNegativeInfinity(hq) && q.Y <= hq) b = m; else a = m;
                }
                var hit = o + dir * b;
                var (sx, sy) = WorldMath.ToSim(hit);
                int ix = (int)Math.Round(sx, MidpointRounding.AwayFromZero), iy = (int)Math.Round(sy, MidpointRounding.AwayFromZero);
                return (Math.Clamp(ix, 1, 50), Math.Clamp(iy, 1, 50));
            }
            prevT = t;
        }
        return null;
    }
}
