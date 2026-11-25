namespace Artillery.Core.World;

/// <summary>Terrain generator algorithm family (original "mode" of FUN_3164_1e37 plus special types).</summary>
public enum GenMode
{
    Plain = 0, Pyramids = 1, Craters = 2, CanyonRiver = 3, BigPyramid = 4, Sine = 5, ValleyRiver = 6,
    BlackHole = 7, Island = 8, Volcano = 9, Mandelbrot = 10, City = 11, JungleRiver = 12, Terraces = 13,
}

/// <summary>Object placement call: Place(radius, count, maxH, minH, kind).</summary>
public readonly record struct PropScatter(int Radius, int Count, int MaxHeight, int MinHeight, byte Kind);

/// <summary>Colour scheme id for the presentation layer (original palette scheme 1..15). Interpretation lives in the Godot layer.</summary>
public sealed record LandscapeDef
{
    public required int Id { get; init; }                 // 1..27
    public required string Name { get; init; }
    public required GenMode Mode { get; init; }
    public int Base { get; init; }
    public int SpikeMax { get; init; }
    public int Spikes { get; init; }
    public int SmoothPasses { get; init; }
    public int PaletteScheme { get; init; } = 4;
    public double Gravity { get; init; } = Balance.DefaultGravity;
    public double AirResistance { get; init; } = Balance.DefaultAir;
    /// <summary>Apply air resistance even when the option is off (Black Hole: 1.01 accelerates shells).</summary>
    public bool ForceAir { get; init; }
    public bool SlopeShading { get; init; } = true;
    public double Light { get; init; } = 0.8;
    public bool HasWater { get; init; }
    public int WaterLevel { get; init; }
    public bool MeltsTerrain { get; init; }               // fire lowers terrain (snow maps)
    public bool TankDrift { get; init; }                  // Black Hole: tanks jitter each turn
    public bool Morphs { get; init; }                     // terrain morphs every turn, sky tints toward leader
    public bool VolcanoPlacement { get; init; }           // tanks must not be placed above height 45
    public IReadOnlyList<PropScatter> Props { get; init; } = Array.Empty<PropScatter>();
}
