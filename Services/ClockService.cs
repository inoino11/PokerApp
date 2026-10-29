using System.Text.Json;
using Blazored.LocalStorage;
using PokerApp.Models;

namespace PokerApp.Services;

/// <summary>
/// 時計の保存形式。残り時間は「AnchorUtc 時点で経過していた ElapsedMs」と今の時刻から毎回計算する。
/// WASM のタイマーは裏に回ると止まるが、こうしておけば開き直しても時計がずれない。
/// </summary>
public sealed record ClockData(int LevelIndex, bool Running, bool Started, long ElapsedMs, DateTimeOffset AnchorUtc);

/// <summary>
/// ブラインドの時計。時間は戻らないので UNDO の対象外。
/// </summary>
public sealed class ClockService(ILocalStorageService storage, TimeProvider time)
{
    private readonly TimeProvider _time = time;
    private ClockData _d = new(0, false, false, 0, DateTimeOffset.MinValue);

    public bool Running => _d.Running;
    public bool Started => _d.Started;

    public int LevelIndex(GameSettings s) => Math.Clamp(_d.LevelIndex, 0, s.Levels.Count - 1);

    public BlindLevel Level(GameSettings s) => s.Levels[LevelIndex(s)];

    public bool IsLastLevel(GameSettings s) => LevelIndex(s) >= s.Levels.Count - 1;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await storage.GetItemAsStringAsync(StorageKeys.Clock);
            if (!string.IsNullOrEmpty(json)) _d = JsonSerializer.Deserialize(json, AppJsonContext.Default.ClockData) ?? _d;
        }
        catch
        {
            // 読めなければ初期値で続ける
        }
    }

    private long Elapsed => _d.ElapsedMs + (_d.Running ? (long)(_time.GetUtcNow() - _d.AnchorUtc).TotalMilliseconds : 0);

    public TimeSpan Remaining(GameSettings s)
    {
        if (s.LevelMinutes <= 0) return TimeSpan.Zero;
        var left = s.LevelMinutes * 60_000L - Elapsed;
        return TimeSpan.FromMilliseconds(Math.Max(0, left));
    }

    /// <summary>
    /// 時間が来ていればレベルを上げる。上がったら true。最後のレベルでは止まる（頭打ち）。
    /// </summary>
    public async Task<bool> TickAsync(GameSettings s)
    {
        if (s.LevelMinutes <= 0 || !_d.Running) return false;
        var duration = s.LevelMinutes * 60_000L;
        var elapsed = Elapsed;
        var level = LevelIndex(s);
        var last = s.Levels.Count - 1;
        var advanced = false;
        while (elapsed >= duration && level < last)
        {
            level++;
            elapsed -= duration;
            advanced = true;
        }
        if (!advanced) return false;
        await SaveAsync(_d with { LevelIndex = level, ElapsedMs = elapsed, AnchorUtc = _time.GetUtcNow() });
        return true;
    }

    /// <summary>
    /// 最初のハンドを始めたときに時計を動かす。一度動かしたあとは一時停止の状態を変えない。
    /// </summary>
    public Task StartIfFirstAsync() =>
        _d.Started ? Task.CompletedTask : SaveAsync(_d with { Started = true, Running = true, ElapsedMs = 0, AnchorUtc = _time.GetUtcNow() });

    public Task ToggleAsync() => _d.Running
        ? SaveAsync(_d with { Running = false, ElapsedMs = Elapsed, AnchorUtc = _time.GetUtcNow() })
        : SaveAsync(_d with { Running = true, Started = true, AnchorUtc = _time.GetUtcNow() });

    public Task SetLevelAsync(GameSettings s, int level) =>
        SaveAsync(_d with { LevelIndex = Math.Clamp(level, 0, s.Levels.Count - 1), ElapsedMs = 0, AnchorUtc = _time.GetUtcNow() });

    /// <summary>
    /// 残り時間を増減する（minutes が正なら残りが増える）。
    /// </summary>
    public Task AddMinutesAsync(GameSettings s, int minutes)
    {
        var duration = s.LevelMinutes * 60_000L;
        var elapsed = Math.Clamp(Elapsed - minutes * 60_000L, 0, duration);
        return SaveAsync(_d with { ElapsedMs = elapsed, AnchorUtc = _time.GetUtcNow() });
    }

    public Task ResetAsync() => SaveAsync(new ClockData(0, false, false, 0, _time.GetUtcNow()));

    public ClockData Snapshot => _d;

    public Task RestoreAsync(ClockData data) => SaveAsync(data);

    private async Task SaveAsync(ClockData d)
    {
        _d = d;
        try
        {
            await storage.SetItemAsStringAsync(StorageKeys.Clock, JsonSerializer.Serialize(d, AppJsonContext.Default.ClockData));
        }
        catch
        {
            // 保存できなくても時計は動かし続ける
        }
    }
}
