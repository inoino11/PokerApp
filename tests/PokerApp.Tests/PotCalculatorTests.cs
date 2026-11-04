using PokerApp.Services;
using C = PokerApp.Services.PotCalculator.Contribution;

namespace PokerApp.Tests;

public class PotCalculatorTests
{
    [Fact]
    public void フォールドした人の額もポットに入るが権利はない()
    {
        var pots = PotCalculator.BuildPots([new C(1, 500, true), new C(2, 200, false), new C(3, 600, false)]);
        Assert.Equal([200L * 3, 300L + 400L], pots.Select(p => p.Amount));
        Assert.Equal([2, 3], pots[0].EligibleIds);
        Assert.Equal([3], pots[1].EligibleIds);
    }

    [Fact]
    public void 残っている人より多く出して降りた分は最後のポットに入る()
    {
        var pots = PotCalculator.BuildPots([new C(1, 800, true), new C(2, 300, false), new C(3, 300, false)]);
        Assert.Single(pots);
        Assert.Equal(1400, pots[0].Amount);
    }

    [Fact]
    public void 超過分の返却()
    {
        var (contribs, to, amount) = PotCalculator.ReturnUncalled([new C(1, 1000, false), new C(2, 400, false), new C(3, 100, true)]);
        Assert.Equal(1, to);
        Assert.Equal(600, amount);
        Assert.Equal(400, contribs.Single(c => c.PlayerId == 1).Amount);
    }

    [Fact]
    public void 同額なら返却しない()
    {
        var (_, to, _) = PotCalculator.ReturnUncalled([new C(1, 400, false), new C(2, 400, false)]);
        Assert.Null(to);
    }

    [Fact]
    public void 勝者の組_上位の組に権利がなければ次の入力を求める()
    {
        var pots = PotCalculator.BuildPots([new C(1, 100, false), new C(2, 300, false), new C(3, 300, false), new C(4, 500, false)]);
        // メイン(1,2,3,4) / サイド1(2,3,4) / サイド2(4 のみ)
        var (w, pending) = PotCalculator.ResolveWinners(pots, [[1]]);
        Assert.Equal(1, pending);
        (w, pending) = PotCalculator.ResolveWinners(pots, [[1], [2, 3]]);
        Assert.Null(pending);
        Assert.Equal([1], w[0]);
        Assert.Equal([2, 3], w[1]);
        Assert.Equal([4], w[2]);
    }

    [Fact]
    public void チョップの勝者がいるポットは_権利のある勝者だけで分ける()
    {
        var pots = PotCalculator.BuildPots([new C(1, 100, false), new C(2, 300, false), new C(3, 300, false)]);
        var (w, pending) = PotCalculator.ResolveWinners(pots, [[1, 2]]);
        Assert.Null(pending);
        Assert.Equal([1, 2], w[0]);
        Assert.Equal([2], w[1]);
    }

    [Theory]
    [InlineData(300, 3, 25, new long[] { 100, 100, 100 })]
    [InlineData(350, 2, 50, new long[] { 200, 150 })]
    [InlineData(100, 3, 25, new long[] { 50, 25, 25 })]
    [InlineData(101, 2, 1, new long[] { 51, 50 })]
    [InlineData(110, 2, 25, new long[] { 60, 50 })]
    public void 分配は単位ごとで_端数は先頭から(long amount, int n, long unit, long[] expected)
    {
        var ids = Enumerable.Range(1, n).ToList();
        var r = PotCalculator.Split(amount, ids, unit);
        Assert.Equal(expected, ids.Select(id => r[id]));
        Assert.Equal(amount, r.Values.Sum());
    }
}

public class ChipsTests
{
    [Theory]
    [InlineData("12000", 12000L)]
    [InlineData("12,000", 12000L)]
    [InlineData("１２，０００", 12000L)]
    [InlineData(" 500 ", 500L)]
    [InlineData("0", 0L)]
    public void 読める額(string text, long expected)
    {
        Assert.True(Models.Chips.TryParse(text, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-100")]
    [InlineData("1.5")]
    [InlineData("1000000000000")]
    public void 読めない額(string text) => Assert.False(Models.Chips.TryParse(text, out _));

    [Theory]
    [InlineData(3250L, 100L, "32 BB")]
    [InlineData(1000L, 100L, "10 BB")]
    [InlineData(999L, 100L, "9.9 BB")]
    [InlineData(450L, 100L, "4.5 BB")]
    [InlineData(5L, 100L, "0.0 BB")]
    [InlineData(250000L, 100L, "2,500 BB")]
    [InlineData(0L, 100L, "")]
    [InlineData(1000L, 0L, "")]
    public void BB換算(long stack, long bb, string expected) => Assert.Equal(expected, Models.Chips.FormatBb(stack, bb));
}
