using System.Text.Json;
using PokerApp.Models;
using PokerApp.Services;

namespace PokerApp.Tests;

public class SerializationTests
{
    [Fact]
    public void 保存した履歴を再生すると同じ状態になる()
    {
        var g = new TestGame(1000, 5000, 5000).Start().AllIn().Call().Call().Raise(2000);
        var commands = new List<GameCommand>
        {
            new AddPlayer(1000), new AddPlayer(5000), new AddPlayer(5000), new StartHand(0),
            new Act(1, ActionKind.AllIn), new Act(2, ActionKind.Call), new Act(3, ActionKind.Call), new Act(2, ActionKind.Raise, 2000),
        };
        var start = GameEngine.NewGame(new GameSettings([new BlindLevel(50, 100)], 0, false, 10000, null, true));
        var saved = new SavedGame(1, start, commands, [new RenamePlayer(1, "あ")]);

        var json = JsonSerializer.Serialize(saved, AppJsonContext.Default.SavedGame);
        var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.SavedGame)!;

        var state = loaded.Commands.Aggregate(loaded.Base, GameEngine.Apply);
        Assert.Equal(JsonSerializer.Serialize(new SavedGame(1, g.State, [], []), AppJsonContext.Default.SavedGame), JsonSerializer.Serialize(new SavedGame(1, state, [], []), AppJsonContext.Default.SavedGame));
        Assert.IsType<RenamePlayer>(Assert.Single(loaded.Redo));
    }
}
