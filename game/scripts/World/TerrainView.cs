using Godot;
using Artillery.Core.Match;
using Artillery.Core.World;

namespace Artillery.Game.World;

/// <summary>
/// Flat-shaded banded low-poly terrain slab (GODOT-PLAN section 4 "banded look").
/// Call <see cref="Build"/> once per round, <see cref="Rebuild"/> after every TerrainChangedEvent (heights, effects, props).
/// </summary>
public partial class TerrainView : Node3D
{
    private const int Lo = 1, Hi = 50;
    private const int Cells = Hi - Lo; // 49

    /// <summary>Overall colour brightness multiplier for the palette ramp.</summary>
    [Export] public float Brightness { get; set; } = 1.0f;

    public WorldState? World { get; private set; }

    private MeshInstance3D? _ground, _fx, _water;
    private readonly ArrayMesh _groundMesh = new(), _fxMesh = new();
    private ShaderMaterial _groundMat = null!; private StandardMaterial3D _fxMat = null!, _waterMat = null!, _propMat = null!;
    private readonly Dictionary<int, MultiMeshInstance3D> _props = new();
    private Color[] _ramp = Array.Empty<Color>();
    private Color _waterCol;
    private int _scheme = -1;
    private readonly Vector3[] _grid = new Vector3[(Hi + 2) * (Hi + 2)];

    private static Color C(double r, double g, double b) => new((float)(r / 63), (float)(g / 63), (float)(b / 63));

    // Flat shading via screen-space derivatives: no normal array needed (cheaper rebuilds). Vertex colours are authored in sRGB.
    private const string FlatShader = @"
shader_type spatial;
varying vec3 vcol;
void vertex() {
    vec3 c = COLOR.rgb;
    vcol = mix(pow((c + vec3(0.055)) / 1.055, vec3(2.4)), c / 12.92, vec3(lessThan(c, vec3(0.04045))));
}
void fragment() {
    ALBEDO = vcol;
    ROUGHNESS = 1.0;
    NORMAL = -normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
}";

    private void Init()
    {
        if (_ground != null) return;
        _groundMat = new ShaderMaterial { Shader = new Shader { Code = FlatShader } };
        _fxMat = WorldMath.VertexColorMaterial(true, true);
        _propMat = WorldMath.VertexColorMaterial();
        _waterMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f, Metallic = 0.1f };
        _ground = new MeshInstance3D { Name = "Ground", Mesh = _groundMesh, MaterialOverride = _groundMat };
        _fx = new MeshInstance3D { Name = "Effects", Mesh = _fxMesh, MaterialOverride = _fxMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _water = new MeshInstance3D { Name = "Water", Visible = false, MaterialOverride = _waterMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ground); AddChild(_fx); AddChild(_water);
        foreach (var (key, mesh) in PropMeshes())
        {
            var mm = new MultiMeshInstance3D { Name = "Props_" + key, MaterialOverride = _propMat, Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh } };
            _props[key] = mm; AddChild(mm);
        }
    }

    /// <summary>Binds a world, picks the palette from its landscape and rebuilds everything.</summary>
    public void Build(WorldState world)
    {
        Init();
        World = world;
        _scheme = world.Landscape?.PaletteScheme ?? 4;
        _ramp = Palette.Ramp(_scheme);
        for (int i = 0; i < _ramp.Length; i++) _ramp[i] = Lighten(_ramp[i], Brightness);
        _waterCol = Palette.Water(_scheme);
        Rebuild();
    }

    private static Color Lighten(Color c, float k) => new(Math.Min(c.R * k, 1f), Math.Min(c.G * k, 1f), Math.Min(c.B * k, 1f), c.A);

    /// <summary>Rebuilds terrain, effect overlays, water and props from the bound WorldState (a few ms).</summary>
    public void Rebuild()
    {
        if (World == null) return;
        Init();
        var w = World; var h = w.Height; var ld = w.Landscape;
        bool shade = ld?.SlopeShading ?? true; double light = ld?.Light ?? 0.8; bool water = w.HasWater;
        int maxBand = _scheme == 12 ? Palette.Bands - 1 : 16;

        const int stride = Hi + 2;
        for (int x = Lo; x <= Hi; x++)
        for (int y = Lo; y <= Hi; y++) _grid[x * stride + y] = WorldMath.ToGodot(x, y, h[x, y]);

        int nTop = Cells * Cells * 6, nSkirt = 4 * Cells * 6;
        var pos = new Vector3[nTop + nSkirt]; var col = new Color[nTop + nSkirt];
        int n = 0;
        void T(Vector3 a, Vector3 b, Vector3 c, Color cc, Vector3 hint)
        {
            var nn = (b - a).Cross(c - a);
            if (nn.Dot(hint) > 0) { (b, c) = (c, b); nn = -nn; } // Godot front faces are clockwise
            nn = nn.LengthSquared() > 1e-12f ? -nn.Normalized() : hint;
            pos[n] = a; pos[n + 1] = b; pos[n + 2] = c; col[n] = col[n + 1] = col[n + 2] = cc; n += 3;
        }

        List<Vector3>? fxPos = null; List<Color>? fxCol = null;
        for (int x = Lo; x < Hi; x++)
        for (int y = Lo; y < Hi; y++)
        {
            int h00 = h[x, y], h10 = h[x + 1, y], h01 = h[x, y + 1], h11 = h[x + 1, y + 1];
            int sh = shade ? (int)Math.Round(light * (h00 - h11), MidpointRounding.AwayFromZero) : 0;
            int b1 = FloorDiv(h00 + h10 + h01 + h11, 12) + sh, b2 = FloorDiv(h10 + h11 + h01, 9) + sh;
            int lo = water ? 2 : 0;
            Color c1 = _ramp[Math.Clamp(b1, lo, maxBand)], c2 = _ramp[Math.Clamp(b2, lo, maxBand)];

            byte fx = w.Effects[x, y];
            if (Effect.IsAcid(fx)) { c1 = c1.Lerp(new Color(0.35f, 0.95f, 0.2f), 0.7f); c2 = c2.Lerp(new Color(0.35f, 0.95f, 0.2f), 0.7f); }
            else if (Effect.IsGoo(fx)) { c1 = c1.Lerp(new Color(0.45f, 0.55f, 0.35f), 0.75f); c2 = c2.Lerp(new Color(0.45f, 0.55f, 0.35f), 0.75f); }
            else if (fx != 0)
            {
                fxPos ??= new(); fxCol ??= new();
                Color fc = Effect.IsLava(fx) ? new Color(1f, 0.32f, 0.03f, 0.95f) : new Color(1f, 0.55f + 0.05f * (fx % 5), 0.1f, 0.9f);
                var lift = Vector3.Up * 0.04f;
                Vector3 a = _grid[(x + 1) * stride + y] + lift, b = _grid[x * stride + y] + lift, c = _grid[x * stride + y + 1] + lift, d = _grid[(x + 1) * stride + y + 1] + lift;
                // winding fixed via the flat up-facing hint at draw time: emit CCW seen from above (normals unused by the unshaded overlay)
                fxPos.AddRange(new[] { a, b, c, d, a, c }); for (int k = 0; k < 6; k++) fxCol.Add(fc);
            }

            Vector3 p10 = _grid[(x + 1) * stride + y], p00 = _grid[x * stride + y], p01 = _grid[x * stride + y + 1], p11 = _grid[(x + 1) * stride + y + 1];
            T(p10, p00, p01, c1, Vector3.Up);
            T(p11, p10, p01, c2, Vector3.Up);
        }

        // skirt: vertical walls down to BaseY so the map reads as a floating slab
        float by = WorldMath.BaseY;
        void Wall(Vector3 a, Vector3 b, Vector3 outward, int band)
        {
            // one flat colour for both triangles: differing shades made a sawtooth of dark spikes along the diagonal
            var cWall = _ramp[Math.Min(maxBand, 3)].Darkened(0.55f); // constant slab colour: per-column band colours showed through the water as spikes
            var a0 = new Vector3(a.X, by, a.Z); var b0 = new Vector3(b.X, by, b.Z);
            T(a, b, b0, cWall, outward); T(a, b0, a0, cWall, outward);
        }
        for (int i = Lo; i < Hi; i++)
        {
            Wall(_grid[Lo * stride + i], _grid[Lo * stride + i + 1], Vector3.Back, FloorDiv(h[Lo, i] + h[Lo, i + 1], 6));
            Wall(_grid[Hi * stride + i], _grid[Hi * stride + i + 1], Vector3.Forward, FloorDiv(h[Hi, i] + h[Hi, i + 1], 6));
            Wall(_grid[i * stride + Lo], _grid[(i + 1) * stride + Lo], Vector3.Right, FloorDiv(h[i, Lo] + h[i + 1, Lo], 6));
            Wall(_grid[i * stride + Hi], _grid[(i + 1) * stride + Hi], Vector3.Left, FloorDiv(h[i, Hi] + h[i + 1, Hi], 6));
        }

        _groundMesh.ClearSurfaces();
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = pos; arr[(int)Mesh.ArrayType.Color] = col;
        _groundMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        _fxMesh.ClearSurfaces();
        if (fxPos != null)
        {
            var fa = new Godot.Collections.Array();
            fa.Resize((int)Mesh.ArrayType.Max);
            fa[(int)Mesh.ArrayType.Vertex] = fxPos.ToArray(); fa[(int)Mesh.ArrayType.Color] = fxCol!.ToArray();
            _fxMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, fa);
            _fxMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        RebuildWater(water);
        RebuildProps();
    }

    private void RebuildWater(bool water)
    {
        _water!.Visible = water;
        if (!water) return;
        float top = World!.WaterLevel * WorldMath.HeightScale, h = Math.Max(top - WorldMath.BaseY, 0.01f);
        var wsz = new Vector3(Cells - 0.02f, h, Cells - 0.02f);
        if (_water.Mesh is BoxMesh bm) { if (bm.Size != wsz) bm.Size = wsz; } else _water.Mesh = new BoxMesh { Size = wsz };
        _water.Position = new Vector3(0, WorldMath.BaseY + h / 2f, 0);
        var wc = _waterCol; wc = Lighten(wc, 1.6f); wc.A = _scheme == 13 ? 0.85f : 0.6f;
        _waterMat.AlbedoColor = wc;
        _waterMat.EmissionEnabled = _scheme == 13;
        if (_scheme == 13) { _waterMat.Emission = new Color(1f, 0.3f, 0.02f); _waterMat.EmissionEnergyMultiplier = 1.4f; }
    }

    private void RebuildProps()
    {
        var w = World!; var h = w.Height;
        var lists = new Dictionary<int, List<Transform3D>>();
        foreach (var k in _props.Keys) lists[k] = new();
        for (int x = Lo; x < Hi; x++)
        for (int y = Lo; y < Hi; y++)
        {
            byte p = w.Props[x, y];
            if (p == 0) continue;
            int key = _props.ContainsKey(p) ? p : (p & 0x10) != 0 ? 0x12 : 1;
            uint hs = (uint)(x * 73856093 ^ y * 19349663) * 2654435761u;
            float ox = 0.15f + (hs & 0xFF) / 255f * 0.7f, oy = 0.15f + ((hs >> 8) & 0xFF) / 255f * 0.7f;
            float rot = ((hs >> 16) & 0xFF) / 255f * Mathf.Tau, sc = 0.85f + ((hs >> 24) & 0x7) / 7f * 0.4f;
            var at = WorldMath.ToGodot(x + ox, y + oy, h.HeightAt(x + ox, y + oy));
            lists[key].Add(new Transform3D(Basis.FromEuler(new Vector3(0, rot, 0)).Scaled(Vector3.One * sc), at));
        }
        foreach (var (key, mmi) in _props)
        {
            var mm = mmi.Multimesh; var l = lists[key];
            var buf = new float[l.Count * 12];
            for (int i = 0; i < l.Count; i++)
            {
                var t = l[i]; var b = t.Basis; int o = i * 12;
                buf[o] = b.Row0.X; buf[o + 1] = b.Row0.Y; buf[o + 2] = b.Row0.Z; buf[o + 3] = t.Origin.X;
                buf[o + 4] = b.Row1.X; buf[o + 5] = b.Row1.Y; buf[o + 6] = b.Row1.Z; buf[o + 7] = t.Origin.Y;
                buf[o + 8] = b.Row2.X; buf[o + 9] = b.Row2.Y; buf[o + 10] = b.Row2.Z; buf[o + 11] = t.Origin.Z;
            }
            if (mm.InstanceCount != l.Count) mm.InstanceCount = l.Count;
            mm.Buffer = buf;
        }
    }

    private static IEnumerable<(int, ArrayMesh)> PropMeshes()
    {
        var trunk = C(30, 20, 12); var dark = C(10, 8, 4);
        MeshBuilder B() => new();
        var t1 = B(); t1.Cone(Vector3.Zero, 0.09f, 0.07f, 0.35f, 5, trunk); t1.Cone(new Vector3(0, 0.25f, 0), 0.42f, 0.02f, 0.95f, 6, C(4, 50, 12), C(2, 40, 10));
        yield return (Prop.Tree, t1.ToMesh());
        var t2 = B(); t2.Cone(Vector3.Zero, 0.09f, 0.07f, 0.3f, 5, trunk); t2.Cone(new Vector3(0, 0.2f, 0), 0.4f, 0.02f, 0.85f, 6, C(8, 30, 4), C(6, 24, 3));
        yield return (Prop.TreeDark, t2.ToMesh());
        var pm = B(); pm.Cone(Vector3.Zero, 0.07f, 0.05f, 1.0f, 5, C(40, 30, 16)); pm.Cone(new Vector3(0, 0.95f, 0), 0.55f, 0.02f, 0.2f, 6, C(8, 45, 10), C(14, 52, 14));
        yield return (Prop.Palm, pm.ToMesh());
        var bu = B(); bu.Cone(Vector3.Zero, 0.35f, 0.08f, 0.3f, 5, C(10, 36, 8), C(6, 28, 6));
        yield return (Prop.Bush, bu.ToMesh());
        var gr = B(); gr.Box(new Vector3(0, 0.22f, 0), new Vector3(0.1f, 0.44f, 0.1f), C(30, 30, 32)); gr.Box(new Vector3(0, 0.32f, 0), new Vector3(0.34f, 0.09f, 0.1f), C(30, 30, 32));
        yield return (Prop.Grave, gr.ToMesh());
        var db = B(); db.Cone(Vector3.Zero, 0.22f, 0.02f, 0.5f, 4, dark, C(16, 11, 6));
        yield return (Prop.DeadBush, db.ToMesh());
        var st = B(); st.Cone(Vector3.Zero, 0.17f, 0.13f, 0.22f, 5, C(14, 10, 6));
        yield return (Prop.Stump, st.ToMesh());
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>World-space terrain height (Godot Y) at a Godot XZ position; NegativeInfinity outside the slab.</summary>
    public float SampleHeight(float gx, float gz)
    {
        if (World == null) return float.NegativeInfinity;
        var (x, y) = WorldMath.ToSim(new Vector3(gx, 0, gz));
        if (x < Lo || x > Hi || y < Lo || y > Hi) return float.NegativeInfinity;
        return (float)(World.Height.HeightAt(x, y) * WorldMath.HeightScale);
    }
}
