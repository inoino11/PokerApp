using PokerApp.Models;
using PokerApp.Services;

namespace PokerApp.Tests;

public class ClockServiceTests
{
    private static readonly GameSettings Settings =
        new([new BlindLevel(50, 100), new BlindLevel(100, 200), new BlindLevel(200, 400)], 10, false, 10000, null, true);

    [Fact]
    public async Task 時間が来るとレベルが上がり_最後のレベルで止まる()
    {
        var time = new FakeTime();
        var clock = new ClockService(FakeStorage.Create().Service, time);
        await clock.StartIfFirstAsync();

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(await clock.TickAsync(Settings));
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Remaining(Settings));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await clock.TickAsync(Settings));
        Assert.Equal(1, clock.LevelIndex(Settings));
        Assert.Equal(TimeSpan.FromMinutes(9), clock.Remaining(Settings));

        time.Advance(TimeSpan.FromMinutes(100));
        Assert.True(await clock.TickAsync(Settings));
        Assert.Equal(2, clock.LevelIndex(Settings));
        Assert.True(clock.IsLastLevel(Settings));
    }

    [Fact]
    public async Task 一時停止中は進まない()
    {
        var time = new FakeTime();
        var clock = new ClockService(FakeStorage.Create().Service, time);
        await clock.StartIfFirstAsync();
        time.Advance(TimeSpan.FromMinutes(3));
        await clock.ToggleAsync();
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.False(await clock.TickAsync(Settings));
        Assert.Equal(TimeSpan.FromMinutes(7), clock.Remaining(Settings));
        await clock.ToggleAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(6), clock.Remaining(Settings));
    }

    [Fact]
    public async Task 開き直しても時計は続きから進む()
    {
        var time = new FakeTime();
        var (storage, _) = FakeStorage.Create();
        var clock = new ClockService(storage, time);
        await clock.StartIfFirstAsync();
        time.Advance(TimeSpan.FromMinutes(4));

        var reopened = new ClockService(storage, time);
        await reopened.InitializeAsync();
        Assert.Equal(TimeSpan.FromMinutes(6), reopened.Remaining(Settings));
    }

    [Fact]
    public async Task 残り時間とレベルを手で変えられる()
    {
        var time = new FakeTime();
        var clock = new ClockService(FakeStorage.Create().Service, time);
        await clock.StartIfFirstAsync();
        time.Advance(TimeSpan.FromMinutes(5));
        await clock.AddMinutesAsync(Settings, 2);
        Assert.Equal(TimeSpan.FromMinutes(7), clock.Remaining(Settings));
        await clock.SetLevelAsync(Settings, 1);
        Assert.Equal(1, clock.LevelIndex(Settings));
        Assert.Equal(TimeSpan.FromMinutes(10), clock.Remaining(Settings));
    }
}

public class GameStateServiceTests
{
    [Fact]
    public async Task 元に戻すとやり直すができ_開き直しても履歴が残る()
    {
        var (storage, _) = FakeStorage.Create();
        var game = new GameStateService(storage);
        await game.InitializeAsync();
        await game.ExecuteAsync(new AddPlayer(10000));
        await game.ExecuteAsync(new AddPlayer(10000));
        await game.ExecuteAsync(new StartHand(0));
        Assert.NotNull(game.State.Hand);

        await game.UndoAsync();
        Assert.Null(game.State.Hand);
        Assert.True(game.CanRedo);

        var reopened = new GameStateService(storage);
        await reopened.InitializeAsync();
        Assert.Equal(2, reopened.State.Players.Count);
        Assert.True(reopened.CanRedo);
        await reopened.RedoAsync();
        Assert.NotNull(reopened.State.Hand);
    }

    [Fact]
    public async Task 新しい操作をするとやり直しは消える()
    {
        var game = new GameStateService(FakeStorage.Create().Service);
        await game.InitializeAsync();
        await game.ExecuteAsync(new AddPlayer(10000));
        await game.UndoAsync();
        await game.ExecuteAsync(new AddPlayer(5000));
        Assert.False(game.CanRedo);
    }

    [Fact]
    public async Task できない操作は理由を返して状態を変えない()
    {
        var game = new GameStateService(FakeStorage.Create().Service);
        await game.InitializeAsync();
        var error = await game.ExecuteAsync(new StartHand(0));
        Assert.NotNull(error);
        Assert.False(game.CanUndo);
    }

    [Fact]
    public async Task 履歴は上限を超えると古いものから畳む()
    {
        var game = new GameStateService(FakeStorage.Create().Service);
        await game.InitializeAsync();
        await game.ExecuteAsync(new AddPlayer(10000));
        for (var i = 0; i < GameStateService.MaxHistory + 10; i++)
            await game.ExecuteAsync(new RenamePlayer(1, $"N{i}"));
        Assert.Equal(GameStateService.MaxHistory, game.ToSaved().Commands.Count);
        Assert.Equal($"N{GameStateService.MaxHistory + 9}", game.State.GetPlayer(1).Name);
        for (var i = 0; i < GameStateService.MaxHistory; i++) await game.UndoAsync();
        Assert.False(game.CanUndo);
        Assert.Single(game.State.Players);
    }

    [Fact]
    public async Task 壊れた保存データでも起動できる()
    {
        var (storage, fake) = FakeStorage.Create();
        fake.Data[StorageKeys.Game] = "{ broken";
        var game = new GameStateService(storage);
        await game.InitializeAsync();
        Assert.Empty(game.State.Players);
    }
}
