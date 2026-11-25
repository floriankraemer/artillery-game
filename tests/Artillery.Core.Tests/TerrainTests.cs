using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;
using Xunit;

namespace Artillery.Core.Tests;

public class TerrainTests
{
    private static WorldState Gen(int id, uint seed)
    {
        var w = new WorldState();
        TerrainGenerator.Generate(w, LandscapeCatalog.Get(id), new TpRandom(seed));
        return w;
    }

    public static IEnumerable<object[]> Ids() => Enumerable.Range(1, 27).Select(i => new object[] { i });

    [Fact]
    public void CatalogHas27()
    {
        Assert.Equal(27, LandscapeCatalog.All.Count);
        for (int i = 1; i <= 27; i++) Assert.Equal(i, LandscapeCatalog.Get(i).Id);
    }

    [Theory, MemberData(nameof(Ids))]
    public void GeneratesDeterministicSaneMap(int id)
    {
        var a = Gen(id, 777); var b = Gen(id, 777);
        for (int x = 1; x <= 50; x++)
        for (int y = 1; y <= 50; y++)
        {
            Assert.Equal(a.Height[x, y], b.Height[x, y]);
            Assert.Equal(a.Props[x, y], b.Props[x, y]);
            Assert.InRange(a.Height[x, y], -60, 700);
        }
        for (int i = 1; i <= 50; i++)
            Assert.True(a.Height[i, 1] == 0 && a.Height[1, i] == 0 && a.Height[50, i] == 0 && a.Height[i, 50] == 0);
        Assert.Equal(LandscapeCatalog.Get(id).Gravity, a.Gravity);
    }

    [Fact]
    public void DifferentSeedsDiffer()
    {
        var a = Gen(5, 1); var b = Gen(5, 2);
        bool diff = false;
        for (int x = 1; x <= 50 && !diff; x++) for (int y = 1; y <= 50; y++) if (a.Height[x, y] != b.Height[x, y]) { diff = true; break; }
        Assert.True(diff);
    }

    [Fact]
    public void BlackHoleCentreLowerThanEdges()
    {
        var w = Gen(22, 42);
        Assert.True(w.Height[25, 25] < w.Height[8, 8] - 20);
        Assert.True(LandscapeCatalog.Get(22).ForceAir);
        Assert.Equal(1.01, w.AirResistance);
    }

    [Fact]
    public void VolcanoCentreHigherAndHasLava()
    {
        var w = Gen(24, 42);
        int n = 0, sx = 0, sy = 0;
        for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++) if (Effect.IsLava(w.Effects[x, y])) { n++; sx += x; sy += y; }
        Assert.Equal(13, n);
        Assert.True(w.Height[sx / n, sy / n] > 35);
        Assert.True(w.Height[sx / n, sy / n] > w.Height[3, 3] + 20);
    }

    [Fact]
    public void MorphKeepsHeightsBounded()
    {
        var w = Gen(10, 9);
        TerrainGenerator.Morph(w, new TpRandom(3));
        for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++) Assert.InRange(w.Height[x, y], -25, 110);
    }

    [Fact]
    public void CityStreetIndexAlwaysSet()
    {
        for (uint seed = 1; seed < 30; seed++)
        {
            var w = Gen(26, seed);
            int tall = 0;
            for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++) if (w.Height[x, y] >= 10) tall++;
            Assert.InRange(tall, 1, 2500);
        }
    }

    [Theory]
    [InlineData(2), InlineData(3), InlineData(4)]
    public void PlacementSeparatedAndPadsFlat(int players)
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            var st = new MatchState(new MatchSettings { Seed = seed });
            for (int i = 1; i <= players; i++) st.Tanks.Add(new Tank { Index = i, Name = "P" + i, Hull = 100 });
            TerrainGenerator.Generate(st.World, LandscapeCatalog.Get(13), st.Rng);
            TankPlacement.Place(st);
            for (int i = 1; i <= players; i++)
            {
                var t = st[i];
                Assert.InRange(t.X, 5, 44); Assert.InRange(t.Y, 5, 44);
                int g = st.World.Height[(int)t.X, (int)t.Y];
                for (int dx = -1; dx <= 2; dx++) for (int dy = -1; dy <= 2; dy++)
                    Assert.Equal(g, st.World.Height[(int)t.X + dx, (int)t.Y + dy]);
                Assert.Equal(g + 0.5, t.Z, 6);
                for (int j = 1; j < i; j++)
                    Assert.True(Math.Max(Math.Abs(t.X - st[j].X), Math.Abs(t.Y - st[j].Y)) >= 5);
            }
        }
    }

    [Fact]
    public void HeightAtIsBilinear()
    {
        var h = new Heightmap();
        h[3, 4] = 0; h[4, 4] = 10; h[3, 5] = 20; h[4, 5] = 30;
        Assert.Equal(0, h.HeightAt(3, 4), 9);
        Assert.Equal(5, h.HeightAt(3.5, 4), 9);
        Assert.Equal(10, h.HeightAt(3, 4.5), 9);
        Assert.Equal(15, h.HeightAt(3.5, 4.5), 9);
        Assert.Equal(30, h.HeightAt(4, 5), 9);
    }
}
