using Godot;
using Artillery.Core.Util;

namespace Artillery.Game.World;

/// <summary>Satellite beam: a bright column from the sky to the target that flashes and fades.</summary>
public partial class BeamView : Node3D
{
    [Signal] public delegate void FinishedEventHandler();
    [Export] public bool AutoFree { get; set; } = true;
    private const double Duration = 0.9;
    private double _t;
    private MeshInstance3D? _col;
    private StandardMaterial3D? _mat;
    private const float Height = 40f;

    public BeamView Setup(Vec3d target)
    {
        Position = WorldMath.ToGodot(target);
        return this;
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.9f, 0.5f, 1f, 0.9f) };
        _col = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = Height, RadialSegments = 10, Rings = 1 }, MaterialOverride = _mat,
            Position = new Vector3(0, Height / 2f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_col);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float k = (float)Math.Min(_t / Duration, 1);
        float flash = 0.6f + 0.4f * Mathf.Sin(k * 40f);
        if (_col != null) _col.Scale = new Vector3(Math.Max(0.05f, 1f - k), 1, Math.Max(0.05f, 1f - k));
        if (_mat != null) _mat.AlbedoColor = new Color(0.9f, 0.5f, 1f, (1f - k) * flash);
        if (_t >= Duration) { SetProcess(false); EmitSignal(SignalName.Finished); if (AutoFree) QueueFree(); }
    }
}
