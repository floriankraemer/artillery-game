using Godot;
using Artillery.Core.Match;

namespace Artillery.Game.World;

/// <summary>
/// Low-poly tank. Origin = ground point under the tank centre. Barrel at bearing 0 points to sim +X (Godot -Z);
/// <see cref="SetAim"/> yaws by -bearing (sim bearing increases toward sim -Y, see <see cref="WorldMath"/>) and pitches by +elevation,
/// matching Ballistics.LaunchVelocity direction (cos(360-b), sin(360-b)).
/// </summary>
public partial class TankView : Node3D
{
    /// <summary>Visual enlargement over the 1-cell footprint so tanks read on a 49-cell map.</summary>
    [Export] public float VisualScale { get; set; } = 1.6f;

    public int PlayerIndex { get; private set; }
    private Node3D _turret = null!, _barrel = null!;
    private MeshInstance3D _body = null!, _shield = null!, _ring = null!;
    private StandardMaterial3D _bodyMat = null!, _shieldMat = null!, _ringMat = null!;
    private bool _current;
    private double _t;
    private bool _built;

    /// <summary>Creates the meshes for player 1..4 (call right after construction).</summary>
    public TankView Init(int playerIndex)
    {
        PlayerIndex = playerIndex;
        if (_built) return this;
        _built = true;
        var main = WorldMath.PlayerColor(playerIndex);
        var dark = main.Darkened(0.45f);
        var light = WorldMath.PlayerColor(playerIndex, true);
        var mat = WorldMath.VertexColorMaterial();
        _bodyMat = mat;

        var hull = new MeshBuilder();
        hull.Box(new Vector3(0, 0.1f, 0), new Vector3(0.62f, 0.14f, 0.8f), main);
        hull.Box(new Vector3(-0.36f, 0.09f, 0), new Vector3(0.14f, 0.18f, 0.9f), dark);
        hull.Box(new Vector3(0.36f, 0.09f, 0), new Vector3(0.14f, 0.18f, 0.9f), dark);
        hull.Box(new Vector3(0, 0.2f, 0.18f), new Vector3(0.5f, 0.06f, 0.3f), light);
        _body = new MeshInstance3D { Name = "Hull", Mesh = hull.ToMesh(), MaterialOverride = mat };

        _turret = new Node3D { Name = "Turret", Position = new Vector3(0, 0.26f, 0.02f) };
        var tb = new MeshBuilder();
        tb.Cone(new Vector3(0, -0.06f, 0), 0.26f, 0.2f, 0.16f, 6, main, light);
        _turret.AddChild(new MeshInstance3D { Name = "TurretMesh", Mesh = tb.ToMesh(), MaterialOverride = mat });
        _barrel = new Node3D { Name = "Barrel", Position = new Vector3(0, 0.03f, -0.15f) };
        var bb = new MeshBuilder();
        bb.Box(new Vector3(0, 0, -0.3f), new Vector3(0.07f, 0.07f, 0.6f), dark.Lightened(0.15f));
        bb.Box(new Vector3(0, 0, -0.62f), new Vector3(0.1f, 0.1f, 0.08f), dark);
        _barrel.AddChild(new MeshInstance3D { Name = "BarrelMesh", Mesh = bb.ToMesh(), MaterialOverride = mat });
        _turret.AddChild(_barrel);

        var visual = new Node3D { Name = "Visual", Scale = Vector3.One * VisualScale };
        visual.AddChild(_body); visual.AddChild(_turret);
        AddChild(visual);

        _shieldMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(light, 0.3f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _shield = new MeshInstance3D { Name = "Shield", Mesh = new SphereMesh { Radius = 1.15f, Height = 2.3f, RadialSegments = 20, Rings = 10 }, MaterialOverride = _shieldMat, Position = new Vector3(0, 0.3f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_shield);

        _ringMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.9f, 0.2f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _ring = new MeshInstance3D { Name = "CurrentRing", Mesh = new TorusMesh { InnerRadius = 1.15f, OuterRadius = 1.3f, Rings = 24, RingSegments = 4 }, MaterialOverride = _ringMat, Position = new Vector3(0, 0.05f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ring);
        return this;
    }

    /// <summary>Turret yaw from sim bearing (deg, 0 = sim +X "north"), barrel pitch from elevation (deg).</summary>
    public void SetAim(double bearingDeg, double elevationDeg)
    {
        _turret.Rotation = new Vector3(0, WorldMath.BearingToYaw(bearingDeg), 0);
        _barrel.Rotation = new Vector3(Mathf.DegToRad((float)elevationDeg), 0, 0);
    }

    /// <summary>Shield bubble: visible when shield &gt; 0, alpha scales with the amount (about 100 = fully opaque-ish).</summary>
    public void SetShield(int shield)
    {
        _shield.Visible = shield > 0;
        if (shield > 0) _shieldMat.AlbedoColor = new Color(_shieldMat.AlbedoColor, Mathf.Clamp(0.12f + shield / 100f * 0.35f, 0.12f, 0.5f));
    }

    /// <summary>Shows the pulsing ring that marks the active player.</summary>
    public void SetCurrent(bool current) { _current = current; _ring.Visible = current; }

    /// <summary>Moves to the tank position and copies aim, shield and alive state.</summary>
    public void SyncFrom(Tank t)
    {
        Position = WorldMath.ToGodot(t.X + 0.5, t.Y + 0.5, t.Z - 0.5);
        SetAim(t.Bearing, t.Elevation);
        SetShield(t.Shield);
        bool alive = t.Alive;
        _bodyMat.AlbedoColor = alive ? Colors.White : new Color(0.25f, 0.25f, 0.25f);
        _barrel.Visible = alive;
        Visible = t.Hull > Tank.HullGone;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_current) _ringMat.AlbedoColor = new Color(1f, 0.9f, 0.2f, 0.65f + 0.35f * (float)Math.Sin(_t * 5));
    }
}
