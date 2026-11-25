using Artillery.Core.Util;
using Xunit;

namespace Artillery.Core.Tests;

public class TpRandomTests
{
    [Fact]
    public void Next100_SequenceIsDeterministic()
    {
        var a = new TpRandom(0);
        var b = new TpRandom(0);
        var seqA = Enumerable.Range(0, 200).Select(_ => a.Next(100)).ToArray();
        var seqB = Enumerable.Range(0, 200).Select(_ => b.Next(100)).ToArray();

        Assert.Equal(seqB, seqA);
        // Seed 0: first step gives seed 1 -> (1·100)>>32 = 0; next seed 0x08088406 -> 3.
        Assert.Equal(0, seqA[0]);
        Assert.Equal(3, seqA[1]);
        Assert.All(seqA, v => Assert.InRange(v, 0, 99));
    }
}
