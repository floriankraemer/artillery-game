using Godot;
using Artillery.Core.Match;
using Artillery.Core.Util;

namespace Artillery.Game.World;

/// <summary>Expanding fire sphere + one-shot GpuParticles3D, sized by blast radius (cells) and coloured per ExplosionKind.</summary>
public partial class ExplosionView : Node3D
{
    [Signal] public delegate void FinishedEventHandler();
    [Export] public bool AutoFree { get; set; } = true;

    private MeshInstance3D? _sphere;
    private StandardMaterial3D? _mat;
    private double _t, _dur = 0.8;
    private float _radius;
    private Color _color;

    public static Color KindColor(ExplosionKind k) => k switch
    {
        ExplosionKind.Fire => new Color(1f, 0.4f, 0.08f),
        ExplosionKind.Acid => new Color(0.45f, 1f, 0.2f),
        ExplosionKind.Goo => new Color(0.6f, 0.75f, 0.4f),
        ExplosionKind.Dirt => new Color(0.6f, 0.42f, 0.22f),
        ExplosionKind.Shock => new Color(0.7f, 0.85f, 1f),
        ExplosionKind.Nuke => new Color(1f, 0.95f, 0.65f),
        ExplosionKind.Beam => new Color(0.85f, 0.45f, 1f),
        _ => new Color(1f, 0.62f, 0.2f),
    };

    /// <summary>Configure from a sim position (set before or after adding to the tree; plays on entering the tree).</summary>
    public ExplosionView Setup(Vec3d at, int radiusCells, ExplosionKind kind)
    {
        Position = WorldMath.ToGodot(at);
        _radius = Math.Max(radiusCells, 1) * 0.9f + 0.3f;
        _color = KindColor(kind);
        _dur = kind == ExplosionKind.Nuke ? 1.8 : 0.5 + Math.Min(radiusCells, 20) * 0.04;
        return this;
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = _color };
        _sphere = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 }, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = Vector3.One * 0.05f };
        AddChild(_sphere);

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = _radius * 0.4f,
            Direction = Vector3.Up, Spread = 120f, InitialVelocityMin = _radius * 0.8f, InitialVelocityMax = _radius * 2.2f,
            Gravity = new Vector3(0, -6f, 0), ScaleMin = 0.5f, ScaleMax = 1.4f, Color = _color,
        };
        var dot = new SphereMesh { Radius = 0.12f + _radius * 0.03f, Height = 0.24f + _radius * 0.06f, RadialSegments = 6, Rings = 3 };
        dot.Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = _color.Lightened(0.15f) };
        AddChild(new GpuParticles3D
        {
            Amount = 24 + (int)(_radius * 8), Lifetime = Math.Max(0.6, _dur), OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = pm, DrawPass1 = dot, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(Vector3.One * -_radius * 6, Vector3.One * _radius * 12),
        });
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float k = (float)Math.Min(_t / _dur, 1);
        float ease = 1f - (1f - k) * (1f - k);
        if (_sphere != null) _sphere.Scale = Vector3.One * Math.Max(0.05f, _radius * ease);
        if (_mat != null) _mat.AlbedoColor = new Color(_color, (1f - k) * 0.75f);
        if (_t >= _dur + 0.4)
        {
            SetProcess(false);
            EmitSignal(SignalName.Finished);
            if (AutoFree) QueueFree();
        }
    }
}
