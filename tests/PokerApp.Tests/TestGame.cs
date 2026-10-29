using System.Collections.Immutable;
using PokerApp.Models;
using PokerApp.Services;

namespace PokerApp.Tests;

/// <summary>
/// テスト用にゲームを組み立てて操作を続けて当てる補助。
/// </summary>
sealed class TestGame
{
    public GameState State { get; private set; }

    public TestGame(params long[] stacks) : this(new BlindLevel(50, 100), false, stacks) { }

    public TestGame(BlindLevel level, bool triple, params long[] stacks)
    {
        State = GameEngine.NewGame(new GameSettings([level], 0, triple, 10000, null, true));
        foreach (var stack in stacks) Do(new AddPlayer(stack));
    }

    public TestGame Do(GameCommand command)
    {
        State = GameEngine.Apply(State, command);
        return this;
    }

    public TestGame Start() => Do(new StartHand(0));

    public HandState Hand => State.Hand!;

    public int ToAct => Hand.ToActId!.Value;

    public TestGame Fold() => Do(new Act(ToAct, ActionKind.Fold));
    public TestGame Check() => Do(new Act(ToAct, ActionKind.Check));
    public TestGame Call() => Do(new Act(ToAct, ActionKind.Call));
    public TestGame Raise(long to) => Do(new Act(ToAct, ActionKind.Raise, to));
    public TestGame AllIn() => Do(new Act(ToAct, ActionKind.AllIn));
    public TestGame Win(params int[] ids) => Do(new ChooseWinners(ids.ToImmutableList()));

    public long Stack(int id) => State.GetPlayer(id).Stack;

    public long TotalChips => State.Players.Sum(p => p.Stack) + (State.Hand?.Seats.Sum(x => x.Contributed) ?? 0);
}
