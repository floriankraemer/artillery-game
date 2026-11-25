namespace Artillery.Core.Util;

/// <summary>Turbo Pascal numeric semantics used by the original formulas.</summary>
public static class Pas
{
    /// <summary>Pascal Round: half away from zero.</summary>
    public static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);
    /// <summary>Pascal Trunc: toward zero.</summary>
    public static int Trunc(double x) => (int)Math.Truncate(x);
    public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
