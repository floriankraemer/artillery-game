using Godot;
using Artillery.Core.Match;

namespace Artillery.Game.World;

/// <summary>Flamethrower stream from a FlameEvent: particles fired along the bearing for Reach cells.</summary>
public partial class FlameView : Node3D
{
    [Signal] public delegate void FinishedEventHandler();
    [Export] public bool AutoFree { get; set; } = true;
    private double _t;
    private const double Duration = 1.1;
    private GpuParticles3D? _p;

    /// <summary>FlameEvent.BearingRad is the launch angle (360-bearing): sim direction (cos, sin).</summary>
    public FlameView Setup(FlameEvent e)
    {
        var dir = WorldMath.SimDir(Math.Cos(e.BearingRad), Math.Sin(e.BearingRad)).Normalized();
        var from = WorldMath.ToGodot(e.From);
        Transform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), from);
        float speed = (float)e.Reach / 0.5f;
        var pm = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 0, -1), Spread = 8f, InitialVelocityMin = speed * 0.8f, InitialVelocityMax = speed,
            Gravity = new Vector3(0, 1.5f, 0), ScaleMin = 0.6f, ScaleMax = 1.3f, Color = new Color(1f, 0.55f, 0.1f),
        };
        var dot = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 6, Rings = 3,
            Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.6f, 0.15f) } };
        _p = new GpuParticles3D { Amount = 90, Lifetime = 0.5, Emitting = true, ProcessMaterial = pm, DrawPass1 = dot,
            LocalCoords = false, VisibilityAabb = new Aabb(Vector3.One * -12, Vector3.One * 24), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_p);
        return this;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_p != null && _t > Duration) _p.Emitting = false;
        if (_t > Duration + 0.6) { SetProcess(false); EmitSignal(SignalName.Finished); if (AutoFree) QueueFree(); }
    }
}
