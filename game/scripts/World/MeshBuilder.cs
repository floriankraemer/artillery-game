using Godot;

namespace Artillery.Game.World;

/// <summary>Tiny flat-shaded low-poly mesh builder (vertex colours, per-face normals).</summary>
public sealed class MeshBuilder
{
    private readonly List<Vector3> _v = new(), _n = new();
    private readonly List<Color> _c = new();

    /// <summary>Adds a triangle; winding is fixed so the normal points toward <paramref name="hint"/>.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 hint)
    {
        var n = (b - a).Cross(c - a);
        if (n.LengthSquared() < 1e-12f) return;
        n = n.Normalized();
        if (n.Dot(hint) > 0) { (b, c) = (c, b); n = -n; } // Godot front faces are clockwise
        n = -n;
        _v.Add(a); _v.Add(b); _v.Add(c); _n.Add(n); _n.Add(n); _n.Add(n); _c.Add(col); _c.Add(col); _c.Add(col);
    }

    private void TriOut(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 center) => Tri(a, b, c, col, (a + b + c) / 3f - center);

    public void Box(Vector3 center, Vector3 size, Color col)
    {
        var h = size / 2f;
        Vector3 P(int sx, int sy, int sz) => center + new Vector3(sx * h.X, sy * h.Y, sz * h.Z);
        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { TriOut(a, b, c, col, center); TriOut(a, c, d, col, center); }
        Q(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1));
        Q(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Q(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        Q(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1));
        Q(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        Q(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
    }

    /// <summary>Upright frustum/cone with n sides; base at <paramref name="baseCenter"/>.</summary>
    public void Cone(Vector3 baseCenter, float r0, float r1, float height, int sides, Color col, Color? alt = null)
    {
        var mid = baseCenter + Vector3.Up * height / 2f;
        var top = baseCenter + Vector3.Up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            var b0 = baseCenter + new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0);
            var b1 = baseCenter + new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
            var t0 = top + new Vector3(Mathf.Cos(a0) * r1, 0, Mathf.Sin(a0) * r1);
            var t1 = top + new Vector3(Mathf.Cos(a1) * r1, 0, Mathf.Sin(a1) * r1);
            var sc = alt is { } ac && (i & 1) == 1 ? ac : col;
            TriOut(b0, b1, t1, sc, mid);
            if (r1 > 0.0001f) TriOut(b0, t1, t0, sc, mid);
            TriOut(baseCenter, b1, b0, col, mid);
            if (r1 > 0.0001f) TriOut(top, t0, t1, col, mid);
        }
    }

    public ArrayMesh ToMesh()
    {
        var m = new ArrayMesh();
        if (_v.Count == 0) return m;
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arr[(int)Mesh.ArrayType.Color] = _c.ToArray();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }
}
