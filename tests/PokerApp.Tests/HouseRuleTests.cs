using System.Text.Json;
using PokerApp.Models;
using PokerApp.Services;

namespace PokerApp.Tests;

/// <summary>
/// ハウスルール（Run it Twice / Thrice と 72o）のテスト。
/// </summary>
public class HouseRuleTests
{
    static TestGame With(TestGame g, int maxRuns = 1, int sevenDeuceBb = 0) =>
        g.Do(new UpdateSettings(g.State.Settings with { MaxRuns = maxRuns, SevenDeuceBb = sevenDeuceBb }));

    // --- Run it ---

    [Fact]
    public void プリフロップのオールインでRunitの回数を選び_回ごとに勝者を選ぶ()
    {
        var g = With(new TestGame(10000, 10000), maxRuns: 2).Start().AllIn().Call();
        Assert.True(GameEngine.RunsPending(g.Hand));
        Assert.Null(GameEngine.PendingPot(g.State));

        g.Do(new ChooseRuns(2));
        var first = GameEngine.PendingPot(g.State)!.Value;
        Assert.Equal((0, 0, 10000L), (first.Run, first.Index, first.Amount));
        Assert.Equal([(1, 0, 10000L)], GameEngine.LaterPots(g.State).Select(p => (p.Run, p.Index, p.Amount)));

        g.Win(1);
        Assert.Equal(1, GameEngine.PendingPot(g.State)!.Value.Run);
        g.Win(2);
        Assert.Null(g.State.Hand);
        Assert.Equal((10000L, 10000L), (g.Stack(1), g.Stack(2)));
        Assert.Equal(2, g.State.LastResult!.Runs);
        Assert.All(g.State.LastResult.Players, p => Assert.Equal(1, p.RunsWon));
    }

    [Fact]
    public void Twiceではメインとサイドの両方を等分し_回ごとの勝者で配る()
    {
        // P1 が 1000 でオールイン、P2・P3 が 5000 ずつ。メイン 3000、サイド 8000
        var g = With(new TestGame(1000, 5000, 5000), maxRuns: 2).Start().AllIn().AllIn().Call();
        g.Do(new ChooseRuns(2));
        Assert.Equal(1500, GameEngine.PendingPot(g.State)!.Value.Amount);
        g.Win(1);                                    // 1 回目のメインは P1
        Assert.Equal(4000, GameEngine.PendingPot(g.State)!.Value.Amount);
        g.Win(3);                                    // 1 回目のサイドは P3
        g.Win(2);                                    // 2 回目は P2 がメインもサイドも取る
        Assert.Null(g.State.Hand);
        Assert.Equal((1500L, 5500L, 4000L), (g.Stack(1), g.Stack(2), g.Stack(3)));
    }

    [Fact]
    public void Thriceの回ごとのチョップも分けられる()
    {
        var g = With(new TestGame(9000, 9000), maxRuns: 3).Start().AllIn().Call();
        g.Do(new ChooseRuns(3));
        g.Win(1).Win(1, 2).Win(2);
        Assert.Equal((9000L, 9000L), (g.Stack(1), g.Stack(2)));
        Assert.All(g.State.LastResult!.Players, p => Assert.Equal(2, p.RunsWon));
    }

    [Theory]
    [InlineData(1050, 2, 50, 550, 500)]
    [InlineData(1000, 3, 50, 400, 300)]
    [InlineData(1001, 2, 1, 501, 500)]
    public void 回ごとの取り分は最小チップで等分し端数は1回目(long amount, int runs, long unit, long first, long rest)
    {
        Assert.Equal(first, GameEngine.RunShare(amount, runs, 0, unit));
        Assert.All(Enumerable.Range(1, runs - 1), r => Assert.Equal(rest, GameEngine.RunShare(amount, runs, r, unit)));
    }

    [Fact]
    public void リバーのあとのオールインや設定なしでは回数を聞かない()
    {
        var river = With(new TestGame(10000, 10000, 10000), maxRuns: 3).Start().Call().Call().Check();
        for (var i = 0; i < 6; i++) river.Check();
        river.AllIn().Call().Call();
        Assert.False(GameEngine.RunsPending(river.Hand));
        Assert.NotNull(GameEngine.PendingPot(river.State));

        var off = new TestGame(10000, 10000).Start().AllIn().Call();
        Assert.False(GameEngine.RunsPending(off.Hand));
        Assert.NotNull(GameEngine.PendingPot(off.State));
    }

    [Fact]
    public void 上限を超える回数や選ぶ場面以外では選べない()
    {
        var g = With(new TestGame(10000, 10000), maxRuns: 2).Start();
        Assert.Throws<InvalidOperationException>(() => g.Do(new ChooseRuns(2)));
        g.AllIn().Call();
        Assert.Throws<InvalidOperationException>(() => g.Do(new ChooseRuns(3)));
        g.Do(new ChooseRuns(1));
        Assert.Equal(0, GameEngine.PendingPot(g.State)!.Value.Run);
        Assert.Equal(1, GameEngine.RunCount(g.Hand));
    }

    // --- 72o ---

    static int Winner(TestGame g) => g.State.LastResult!.Players.Single(p => p.RunsWon > 0).PlayerId;

    [Fact]
    public void 配られた全員が前のハンドのBBで払う()
    {
        var g = With(new TestGame(10000, 10000, 10000), sevenDeuceBb: 2).Start().Fold().Fold();
        var w = Winner(g);
        var before = g.State.Players.ToDictionary(p => p.Id, p => p.Stack);
        g.Do(new ClaimSevenDeuce(w));
        Assert.All(g.State.Players.Where(p => p.Id != w), p => Assert.Equal(before[p.Id] - 200, p.Stack));
        Assert.Equal(before[w] + 400, g.Stack(w));
        Assert.Equal(w, g.State.LastResult!.SevenDeuceId);
        Assert.Equal(400, g.State.LastResult.Players.Single(p => p.PlayerId == w).Bonus);
        Assert.Equal(g.TotalChips, before.Values.Sum());

        Assert.Throws<InvalidOperationException>(() => g.Do(new ClaimSevenDeuce(w)));
    }

    [Fact]
    public void 退席した人は払わず_足りない人は手元の全部を払う()
    {
        var g = With(new TestGame(10000, 10000, 10000, 10000), sevenDeuceBb: 3).Start().Fold().Fold().Fold();
        var w = Winner(g);
        var others = g.State.Players.Where(p => p.Id != w).Select(p => p.Id).ToList();
        g.Do(new SetStatus(others[0], PlayerStatus.Left)).Do(new SetStack(others[1], 100, false));
        g.Do(new ClaimSevenDeuce(w));
        Assert.Equal(PlayerStatus.SittingOut, g.State.GetPlayer(others[1]).Status);
        Assert.Equal(0, g.Stack(others[1]));
        Assert.Equal(0, g.State.LastResult!.Players.Single(p => p.PlayerId == others[0]).Bonus);
        Assert.Equal(400, g.State.LastResult.Players.Single(p => p.PlayerId == w).Bonus);
    }

    [Fact]
    public void Twiceで1回だけ取ったらボーナスは半分()
    {
        var g = With(new TestGame(10000, 10000), maxRuns: 2, sevenDeuceBb: 1).Start().AllIn().Call();
        g.Do(new ChooseRuns(2)).Win(1).Win(2);
        g.Do(new ClaimSevenDeuce(1));
        Assert.Equal((10050L, 9950L), (g.Stack(1), g.Stack(2)));
    }

    [Fact]
    public void ポットを取っていない人やハンドの途中や設定なしでは入れられない()
    {
        var g = With(new TestGame(10000, 10000, 10000), sevenDeuceBb: 1).Start().Fold().Fold();
        var loser = g.State.LastResult!.Players.First(p => p.RunsWon == 0).PlayerId;
        Assert.Throws<InvalidOperationException>(() => g.Do(new ClaimSevenDeuce(loser)));
        g.Start();
        Assert.Throws<InvalidOperationException>(() => g.Do(new ClaimSevenDeuce(loser)));

        var off = new TestGame(10000, 10000, 10000).Start().Fold().Fold();
        Assert.Throws<InvalidOperationException>(() => off.Do(new ClaimSevenDeuce(Winner(off))));
    }

    [Fact]
    public void 古い保存データはハウスルールなしとして読む()
    {
        var state = new TestGame(10000, 10000).Start().Fold().State;
        var json = JsonSerializer.Serialize(new SavedGame(1, state, [], []), AppJsonContext.Default.SavedGame);
        Assert.Contains(",\"SevenDeuceBb\":0,\"MaxRuns\":1", json);
        var old = json.Replace(",\"SevenDeuceBb\":0,\"MaxRuns\":1", "").Replace(",\"RunsWon\":1", "").Replace(",\"RunsWon\":0", "");
        var loaded = JsonSerializer.Deserialize(old, AppJsonContext.Default.SavedGame)!;
        Assert.Equal((0, 1), (loaded.Base.Settings.SevenDeuceBb, loaded.Base.Settings.MaxRuns));
        Assert.All(loaded.Base.LastResult!.Players, p => Assert.Equal(0, p.RunsWon));
    }
}
