using System.Text.Json;
using Blazored.LocalStorage;
using PokerApp.Models;

namespace PokerApp.Services;

/// <summary>
/// 保存形式。最初の状態と操作の履歴を持ち、読み込むときに履歴を再生して今の状態を作る。
/// </summary>
public sealed record SavedGame(int Version, GameState Base, List<GameCommand> Commands, List<GameCommand> Redo);

/// <summary>
/// ゲームの状態と UNDO / REDO の履歴を持つ。操作は必ず ExecuteAsync を通し、そのたびに端末へ保存する。
/// </summary>
public sealed class GameStateService(ILocalStorageService storage)
{
    public const int MaxHistory = 500;
    private const int FormatVersion = 1;

    private GameState _base = GameEngine.NewGame(PresetService.DefaultSettings);
    private readonly List<GameCommand> _commands = [];
    private readonly List<GameState> _states = [];  // _states[i] は _commands[i] を当てたあとの状態
    private readonly List<GameCommand> _redo = [];

    public GameState State => _states.Count > 0 ? _states[^1] : _base;
    public bool CanUndo => _commands.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await storage.GetItemAsStringAsync(StorageKeys.Game);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize(json, AppJsonContext.Default.SavedGame) is { } saved)
                Load(saved);
        }
        catch
        {
            // 読めなければ新しいゲームで続ける（起動は止めない）
            Load(new SavedGame(FormatVersion, GameEngine.NewGame(PresetService.DefaultSettings), [], []));
        }
    }

    /// <summary>
    /// 操作を当てる。できない操作ならその理由を返し、状態は変えない。
    /// </summary>
    public async Task<string?> ExecuteAsync(GameCommand command)
    {
        GameState next;
        try
        {
            next = GameEngine.Apply(State, command);
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }

        _commands.Add(command);
        _states.Add(next);
        _redo.Clear();
        // 古い履歴は最初の状態に畳み込む
        if (_commands.Count > MaxHistory)
        {
            _base = _states[0];
            _commands.RemoveAt(0);
            _states.RemoveAt(0);
        }
        await SaveAsync();
        return null;
    }

    public async Task UndoAsync()
    {
        if (!CanUndo) return;
        _redo.Add(_commands[^1]);
        _commands.RemoveAt(_commands.Count - 1);
        _states.RemoveAt(_states.Count - 1);
        await SaveAsync();
    }

    public async Task RedoAsync()
    {
        if (!CanRedo) return;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _commands.Add(command);
        _states.Add(GameEngine.Apply(State, command));
        await SaveAsync();
    }

    public SavedGame ToSaved() => new(FormatVersion, _base, [.. _commands], [.. _redo]);

    /// <summary>
    /// 履歴を再生する。途中で当てられない操作があれば、そこから先は捨てる（保存データが壊れていても起動を止めない）。
    /// </summary>
    private void Load(SavedGame saved)
    {
        _base = saved.Base;
        _commands.Clear();
        _states.Clear();
        _redo.Clear();
        var state = _base;
        foreach (var command in saved.Commands)
        {
            try
            {
                state = GameEngine.Apply(state, command);
            }
            catch (Exception)
            {
                return;
            }
            _commands.Add(command);
            _states.Add(state);
        }
        _redo.AddRange(saved.Redo);
        Changed?.Invoke();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        await storage.SetItemAsStringAsync(StorageKeys.Game, JsonSerializer.Serialize(ToSaved(), AppJsonContext.Default.SavedGame));
    }
}
