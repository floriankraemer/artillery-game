namespace Artillery.Core.World;

/// <summary>All 27 landscapes (GAME-MECHANICS §8).</summary>
public static class LandscapeCatalog
{
    private const double Hi = 0.9999;

    private static PropScatter P(int r, int n, int maxH, int minH, byte k) => new(r, n, maxH, minH, k);

    private static LandscapeDef D(int id, string name, GenMode mode, int b, int spMax, int sp, int smooth, int pal,
        double light = 0.8, bool shade = true, double? g = null, double? air = null, int water = -1,
        IReadOnlyList<PropScatter>? props = null, bool melts = false, bool force = false,
        bool drift = false, bool morph = false, bool volcano = false) => new()
    {
        Id = id, Name = name, Mode = mode, Base = b, SpikeMax = spMax, Spikes = sp, SmoothPasses = smooth,
        PaletteScheme = pal, Light = light, SlopeShading = shade,
        Gravity = g ?? Balance.DefaultGravity, AirResistance = air ?? Balance.DefaultAir, ForceAir = force,
        HasWater = water >= 0, WaterLevel = Math.Max(water, 0), MeltsTerrain = melts, TankDrift = drift,
        Morphs = morph, VolcanoPlacement = volcano, Props = props ?? Array.Empty<PropScatter>(),
    };

    private static readonly LandscapeDef[] _all =
    {
        D(1, "Red Planet", GenMode.Plain, 10, 500, 40, 4, 3, g: 0.008, air: Hi),
        D(2, "Lunar Craters", GenMode.Craters, 15, 250, 20, 2, 1, g: 0.008, air: Hi),
        D(3, "Ice Giant", GenMode.Plain, 1, 600, 200, 10, 2, g: 0.012, air: Hi),
        D(4, "Graveyard", GenMode.Plain, 20, 600, 30, 15, 4,
            props: new[] { P(30, 10, 40, 0, Prop.Grave), P(30, 10, 40, 0, Prop.DeadBush), P(30, 10, 40, 0, Prop.Stump) }),
        D(5, "Blue Hills", GenMode.Plain, 10, 550, 20, 4, 5, g: 0.012, air: 0.996),
        D(6, "Green Craters", GenMode.Craters, 3, 300, 50, 2, 6, air: Hi),
        D(7, "Plateaus", GenMode.Terraces, 5, 500, 150, 14, 15, light: 0.5),
        D(8, "Grid World", GenMode.Plain, 5, 500, 100, 10, 7, shade: false),
        D(9, "Wireframe", GenMode.Plain, 10, 400, 100, 7, 8),
        D(10, "Shifting Lands", GenMode.Plain, 10, 20, 20, 5, 4, morph: true),
        D(11, "Spikes", GenMode.Plain, 5, 200, 50, 1, 5),
        D(12, "Twin Pyramids", GenMode.Pyramids, 20, 600, 30, 15, 4, props: new[] { P(40, 20, 30, 10, Prop.Palm) }),
        D(13, "Canyon", GenMode.CanyonRiver, 45, 10, 100, 2, 4, props: new[] { P(50, 30, 70, 30, Prop.Palm) }),
        D(14, "Rolling Hills", GenMode.Plain, 10, 200, 1000, 15, 6,
            props: new[] { P(20, 40, 40, 10, Prop.Tree), P(20, 40, 40, 10, Prop.TreeDark), P(20, 40, 40, 10, Prop.Tree), P(20, 40, 40, 10, Prop.TreeDark) }),
        D(15, "Glacier", GenMode.Plain, 5, 700, 200, 16, 11, light: 0.7, shade: false, water: 5, melts: true),
        D(16, "Archipelago", GenMode.Plain, -5, 450, 300, 10, 12, light: 0.7, shade: false, water: 7,
            props: new[] { P(12, 30, 20, 12, Prop.Tree), P(12, 30, 30, 12, Prop.TreeDark) }),
        D(17, "Lava Sea", GenMode.Plain, -8, 550, 250, 5, 13, light: 0.9, g: 0.011, air: 0.99, water: 9),
        D(18, "Great Pyramid", GenMode.BigPyramid, 1, 1, 1, 1, 4, shade: false),
        D(19, "Ripples", GenMode.Sine, 1, 1, 1, 1, 1),
        D(20, "River Valley", GenMode.ValleyRiver, 5, 270, 90, 6, 6, light: 1.0, water: 5,
            props: new[] { P(20, 70, 30, 8, Prop.Tree), P(20, 70, 15, 8, Prop.TreeDark), P(20, 70, 30, 8, Prop.Tree), P(20, 70, 15, 8, Prop.TreeDark) }),
        D(21, "Frozen River", GenMode.CanyonRiver, 25, 270, 90, 6, 11, light: 1.0, water: 10, melts: true),
        D(22, "Gravity Well", GenMode.BlackHole, 20, 0, 0, 0, 1, light: 1.2, air: 1.01, force: true, drift: true),
        D(23, "Desert Island", GenMode.Island, 15, 200, 200, 5, 14, water: 5,
            props: new[] { P(30, 20, 20, 10, Prop.Palm), P(30, 20, 20, 10, Prop.Bush) }),
        D(24, "Volcano", GenMode.Volcano, 10, 25, 100, 4, 4, light: 0.6, volcano: true,
            props: new[] { P(30, 100, 30, 0, Prop.Tree), P(30, 100, 30, 0, Prop.Tree) }),
        D(25, "Fractal Lake", GenMode.Mandelbrot, 0, 0, 0, 0, 4, light: 0.7, shade: false, water: 5,
            props: new[] { P(10, 30, 15, 5, Prop.TreeDark), P(10, 30, 15, 5, Prop.TreeDark), P(10, 30, 15, 5, Prop.TreeDark) }),
        D(26, "City Blocks", GenMode.City, 0, 0, 0, 0, 1, light: 0.1),
        D(27, "Jungle River", GenMode.JungleRiver, 15, 270, 90, 5, 6, light: 1.0, water: 5,
            props: new[] { 1, 2, 1, 1, 2, 1, 2 }.Select(k => P(30, 300, 25, 5, (byte)k)).ToArray()),
    };

    public static IReadOnlyList<LandscapeDef> All => _all;

    /// <summary>Landscape by type code 1..27.</summary>
    public static LandscapeDef Get(int id) => _all[id - 1];
}
