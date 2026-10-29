using PokerApp.Models;
using PokerApp.Services;

namespace PokerApp.Tests;

public class GameEngineTests
{
    // 3 人なら最初のボタンは P1、SB は P2、BB は P3、最初に行動するのは P1

    [Fact]
    public void ブラインドを払い_BBの次の人から行動する()
    {
        var g = new TestGame(10000, 10000, 10000).Start();
        Assert.Equal(1, g.Hand.ButtonId);
        Assert.Equal([2, 3], g.Hand.BlindIds);
        Assert.Equal(9950, g.Stack(2));
        Assert.Equal(9900, g.Stack(3));
        Assert.Equal(1, g.ToAct);
        Assert.Equal(150, GameEngine.PotTotal(g.Hand));
    }

    [Fact]
    public void 全員が降りたら残った人がポットを取る()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Fold().Fold();
        Assert.Null(g.State.Hand);
        Assert.Equal(10050, g.Stack(3));
        Assert.Equal(9950, g.Stack(2));
        Assert.Equal(30000, g.TotalChips);
        Assert.Equal(50, g.State.LastResult!.Players.Single(p => p.PlayerId == 3).Net);
    }

    [Fact]
    public void ボタンは次のハンドで隣へ移る()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Fold().Fold().Start();
        Assert.Equal(2, g.Hand.ButtonId);
        Assert.Equal([3, 1], g.Hand.BlindIds);
    }

    [Fact]
    public void ボタンの人が退席しても_その席の次へ移る()
    {
        var g = new TestGame(10000, 10000, 10000, 10000).Start().Fold().Fold().Fold();
        g.Do(new SetStatus(2, PlayerStatus.Left)).Start();
        // 前のボタンは P1。次の P2 が抜けたので P3
        Assert.Equal(3, g.Hand.ButtonId);
    }

    [Fact]
    public void 二人のときはボタンがSBで_プリフロップは先に_フロップ以降は後に行動する()
    {
        var g = new TestGame(10000, 10000).Start();
        Assert.Equal(1, g.Hand.ButtonId);
        Assert.Equal([1, 2], g.Hand.BlindIds);
        Assert.Equal(1, g.ToAct);
        g.Call().Check();
        Assert.Equal(Street.Flop, g.Hand.Street);
        Assert.Equal(2, g.ToAct);
    }

    [Fact]
    public void ベットがそろうと次のストリートへ進み_SBから行動する()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Call().Call().Check();
        Assert.Equal(Street.Flop, g.Hand.Street);
        Assert.Equal(2, g.ToAct);
        Assert.Equal(0, g.Hand.CurrentBet);
        g.Check().Check().Check();
        Assert.Equal(Street.Turn, g.Hand.Street);
    }

    [Fact]
    public void BBはプリフロップでコールされてもオプションがある()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Call().Call();
        Assert.Equal(3, g.ToAct);
        g.Raise(400);
        Assert.Equal(1, g.ToAct);
    }

    [Fact]
    public void レイズされたら行動済みの人にも手番が戻る()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Raise(300).Call().Raise(900);
        Assert.Equal(1, g.ToAct);
        Assert.Equal(600, g.Hand.MinRaise);
        Assert.Equal(1500, GameEngine.MinRaiseTo(g.Hand));
    }

    [Fact]
    public void リバーのあとはショーダウンになり_勝者を選ぶと配られる()
    {
        var g = new TestGame(10000, 10000, 10000).Start().Call().Call().Check();
        for (var i = 0; i < 9; i++) g.Check();
        Assert.Equal(Street.Showdown, g.Hand.Street);
        Assert.Equal(0, GameEngine.PendingPot(g.Hand)!.Value.Index);
        g.Win(1);
        Assert.Null(g.State.Hand);
        Assert.Equal(10200, g.Stack(1));
    }

    [Fact]
    public void サイドポット_ショートが勝てばサイドは残りの二人で決める()
    {
        var g = new TestGame(1000, 5000, 5000).Start().AllIn().Call().Call();
        // フロップ: P2 が 2000 ベット、P3 コール
        g.Raise(2000).Call();
        g.Check().Check().Check().Check();
        Assert.Equal(Street.Showdown, g.Hand.Street);
        var pots = GameEngine.Pots(g.Hand);
        Assert.Equal([3000L, 4000L], pots.Select(p => p.Amount));
        g.Win(1);
        var pending = GameEngine.PendingPot(g.Hand)!.Value;
        Assert.Equal(1, pending.Index);
        Assert.Equal([2, 3], pending.Pot.EligibleIds);
        g.Win(3);
        Assert.Equal(3000, g.Stack(1));
        Assert.Equal(2000, g.Stack(2));
        Assert.Equal(6000, g.Stack(3));
        Assert.Equal(11000, g.TotalChips);
    }

    [Fact]
    public void サイドポット_全部に権利がある人が勝てば入力は1回で済む()
    {
        var g = new TestGame(1000, 5000, 5000).Start().AllIn().Call().Call();
        g.Raise(2000).Call().Check().Check().Check().Check();
        g.Win(2);
        Assert.Null(g.State.Hand);
        Assert.Equal(0, g.Stack(1));
        Assert.Equal(9000, g.Stack(2));
    }

    [Fact]
    public void 全員オールインなら残りのストリートを飛ばす()
    {
        var g = new TestGame(1000, 2000, 3000).Start().AllIn().AllIn().AllIn();
        Assert.Equal(Street.Showdown, g.Hand.Street);
        // P3 の超過分 1000 は返る。メイン 3000（全員）、サイド 2000（P2, P3）
        Assert.Equal([3000L, 2000L], GameEngine.Pots(g.Hand).Select(p => p.Amount));
    }

    [Fact]
    public void 一人以外がオールインなら_残りの人がコールした時点でショーダウン()
    {
        var g = new TestGame(10000, 1000, 10000).Start().Call().AllIn().Call();
        Assert.Equal(1, g.ToAct);
        g.Call();
        Assert.Equal(Street.Flop, g.Hand.Street);
        g.Fold();
        Assert.Equal(Street.Showdown, g.Hand.Street);
    }

    [Fact]
    public void コールされなかった額は返る()
    {
        var g = new TestGame(10000, 3000).Start().AllIn().AllIn();
        Assert.Equal(Street.Showdown, g.Hand.Street);
        Assert.Equal([6000L], GameEngine.Pots(g.Hand).Select(p => p.Amount));
        g.Win(2);
        Assert.Equal(7000, g.Stack(1));
        Assert.Equal(6000, g.Stack(2));
    }

    [Fact]
    public void チョップの端数はボタンの次の人から配る()
    {
        // ポット 350 を P3 と P1 で分ける。単位 50 なので 150 ずつ、余り 50 はボタンの次から数えて先の P3 へ
        var g = new TestGame(10000, 10000, 10000).Start().Call().Fold().Raise(150).Call();
        g.Check().Check().Check().Check().Check().Check();
        g.Win(1, 3);
        Assert.Equal(10000 - 150 + 150, g.Stack(1));
        Assert.Equal(10000 - 150 + 200, g.Stack(3));
    }

    [Fact]
    public void 負けて手元が0になったら一時離席になる()
    {
        var g = new TestGame(10000, 1000).Start().AllIn().Call().Win(1);
        Assert.Equal(0, g.Stack(2));
        Assert.Equal(PlayerStatus.SittingOut, g.State.GetPlayer(2).Status);
        g.Do(new SetStack(2, 2000, true));
        Assert.Equal(PlayerStatus.Active, g.State.GetPlayer(2).Status);
        Assert.Equal(3000, g.State.GetPlayer(2).BuyIn);
    }

    [Fact]
    public void ポットにチップを出している間は抜けられないが_降りたあとなら抜けられる()
    {
        var g = new TestGame(10000, 10000, 10000).Start();
        Assert.Throws<InvalidOperationException>(() => g.Do(new SetStatus(2, PlayerStatus.Left)));
        g.Do(new SetStatus(1, PlayerStatus.SittingOut));
        Assert.True(g.Hand.Seats.Single(x => x.PlayerId == 1).Folded);
        Assert.Equal(2, g.ToAct);
    }

    [Fact]
    public void トリプルブラインドは4人以上のときだけ使う()
    {
        var level = new BlindLevel(100, 200, 400);
        var g4 = new TestGame(level, true, 10000, 10000, 10000, 10000).Start();
        Assert.Equal([2, 3, 4], g4.Hand.BlindIds);
        Assert.Equal(400, g4.Hand.CurrentBet);
        Assert.Equal(1, g4.ToAct);
        g4.Call().Call().Call();
        Assert.Equal(4, g4.ToAct); // 3 番目のブラインドが最後に行動する

        var g3 = new TestGame(level, true, 10000, 10000, 10000).Start();
        Assert.Equal([2, 3], g3.Hand.BlindIds);
        Assert.Equal(200, g3.Hand.CurrentBet);
    }

    [Fact]
    public void ポットのパーセントは今のポットを基準に丸める()
    {
        var g = new TestGame(10000, 10000, 10000).Start();
        var seat = g.Hand.Seats.Single(x => x.PlayerId == 1);
        var p = g.State.GetPlayer(1);
        Assert.Equal(150, GameEngine.PotPercentTo(g.State, seat, p, 100));
        Assert.Equal(100, GameEngine.PotPercentTo(g.State, seat, p, 50)); // 75 → 100
        g.Raise(300);
        // P2 の今の額 50、ポット 450。100% なら 50 + 450 = 500
        seat = g.Hand.Seats.Single(x => x.PlayerId == 2);
        Assert.Equal(500, GameEngine.PotPercentTo(g.State, seat, g.State.GetPlayer(2), 100));
    }

    [Fact]
    public void ハンド中に手元を0にしたらオールイン扱いで手番が進む()
    {
        var g = new TestGame(10000, 10000, 10000).Start();
        g.Do(new SetStack(1, 0, false));
        Assert.True(g.Hand.Seats.Single(x => x.PlayerId == 1).AllIn);
        Assert.Equal(2, g.ToAct);
    }

    [Fact]
    public void 権利のない人を勝者にはできない()
    {
        var g = new TestGame(1000, 5000, 5000).Start().AllIn().Call().Call();
        g.Raise(2000).Call().Check().Check().Check().Check().Win(1);
        Assert.Throws<InvalidOperationException>(() => g.Win(1));
    }

    [Fact]
    public void 席は10まで()
    {
        var g = new TestGame(1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        Assert.Throws<InvalidOperationException>(() => g.Do(new AddPlayer(1)));
    }

    [Fact]
    public void 席の入れ替え()
    {
        var g = new TestGame(1, 1, 1).Do(new MovePlayer(3, -1));
        Assert.Equal([1, 3, 2], g.State.Players.Select(p => p.Id));
    }

    [Fact]
    public void 席替えは離れた位置へ一度に動かせる()
    {
        var g = new TestGame(1, 1, 1, 1).Do(new SeatPlayer(4, 0));
        Assert.Equal([4, 1, 2, 3], g.State.Players.Select(p => p.Id));
        g.Do(new SeatPlayer(4, 3));
        Assert.Equal([1, 2, 3, 4], g.State.Players.Select(p => p.Id));
    }

    [Fact]
    public void ハンドの途中は席替えできない()
    {
        var g = new TestGame(10000, 10000, 10000).Start();
        Assert.Throws<InvalidOperationException>(() => g.Do(new SeatPlayer(3, 0)));
        g.Fold().Fold();
        g.Do(new SeatPlayer(3, 0));
        Assert.Equal([3, 1, 2], g.State.Players.Select(p => p.Id));
    }

    [Fact]
    public void 次のボタンは前のボタンの人の次で_席替えするとその人について動く()
    {
        var g = new TestGame(10000, 10000, 10000, 10000).Start().Fold().Fold().Fold();
        Assert.Equal(2, GameEngine.NextButtonId(g.State));
        // 前のボタン P1 を最後の席へ。次のボタンは P1 の次＝先頭の P2 のまま
        g.Do(new SeatPlayer(1, 3));
        Assert.Equal(2, GameEngine.NextButtonId(g.State));
        // P1 を 2 番目へ動かすと、そのすぐ後ろの P3 が次のボタン
        g.Do(new SeatPlayer(1, 1));
        Assert.Equal([2, 1, 3, 4], g.State.Players.Select(p => p.Id));
        Assert.Equal(3, GameEngine.NextButtonId(g.State));
        g.Start();
        Assert.Equal(3, g.Hand.ButtonId);
    }

    [Fact]
    public void 席替えでは退席した人の位置は動かさない()
    {
        var g = new TestGame(1, 1, 1, 1).Do(new SetStatus(2, PlayerStatus.Left)).Do(new SeatPlayer(4, 0));
        // 着席者の並びは 4,1,3。退席した P2 は 2 番目の位置に残る
        Assert.Equal([4, 2, 1, 3], g.State.Players.Select(p => p.Id));
        Assert.Equal([4, 1, 3], g.State.Seated.Select(p => p.Id));
    }
}
