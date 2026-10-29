using System.Collections.Immutable;
using System.Text.Json;
using Blazored.LocalStorage;
using PokerApp.Models;

namespace PokerApp.Services;

public sealed record BlindPreset(string Name, ImmutableList<BlindLevel> Levels, int LevelMinutes, bool TripleBlind, long StartingStack, bool Builtin = false)
{
    public static BlindPreset From(string name, GameSettings s) => new(name, s.Levels, s.LevelMinutes, s.TripleBlind, s.StartingStack);

    /// <summary>
    /// プリセットを今の設定に当てる。チップ単位と丸めは今の設定のまま残す。
    /// </summary>
    public GameSettings ApplyTo(GameSettings s) =>
        s with { Levels = Levels, LevelMinutes = LevelMinutes, TripleBlind = TripleBlind, StartingStack = StartingStack };
}

/// <summary>
/// ブラインドのプリセット。組み込みのものと、利用者が保存したもの。UNDO の対象外。
/// </summary>
public sealed class PresetService(ILocalStorageService storage)
{
    static ImmutableList<BlindLevel> L(params (long Sb, long Bb)[] levels) =>
        levels.Select(l => new BlindLevel(l.Sb, l.Bb)).ToImmutableList();

    public static readonly ImmutableList<BlindPreset> Builtins =
    [
        new("キャッシュゲーム（固定）", L((50, 100), (100, 200), (200, 400), (500, 1000)), 0, false, 10000, true),
        new("キャッシュゲーム（トリプル）", [new(50, 100, 200), new(100, 200, 400), new(200, 400, 800)], 0, true, 20000, true),
        new("短いトーナメント（10分）", L((50, 100), (100, 200), (150, 300), (200, 400), (300, 600), (400, 800), (500, 1000),
            (700, 1400), (1000, 2000), (1500, 3000), (2000, 4000), (3000, 6000), (5000, 10000)), 10, false, 10000, true),
        new("標準トーナメント（20分）", L((100, 200), (150, 300), (200, 400), (300, 600), (400, 800), (500, 1000), (600, 1200),
            (800, 1600), (1000, 2000), (1500, 3000), (2000, 4000), (3000, 6000), (4000, 8000), (6000, 12000), (8000, 16000), (10000, 20000)), 20, false, 20000, true),
    ];

    public static GameSettings DefaultSettings { get; } =
        Builtins[0].ApplyTo(new GameSettings([], 0, false, 0, null, true));

    private ImmutableList<BlindPreset> _user = [];

    public IEnumerable<BlindPreset> All => Builtins.Concat(_user);

    public async Task InitializeAsync()
    {
        try
        {
            var json = await storage.GetItemAsStringAsync(StorageKeys.Presets);
            _user = string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, AppJsonContext.Default.ListBlindPreset)?.ToImmutableList() ?? [];
        }
        catch
        {
            _user = [];
        }
    }

    /// <summary>
    /// 同じ名前の自作プリセットがあれば上書きする。組み込みと同じ名前は使えない。
    /// </summary>
    public async Task<string?> SaveAsync(BlindPreset preset)
    {
        var name = preset.Name.Trim();
        if (name.Length == 0) return "名前を入れてください。";
        if (Builtins.Any(b => b.Name == name)) return "組み込みのプリセットと同じ名前は使えません。";
        preset = preset with { Name = name, Builtin = false };
        _user = _user.RemoveAll(p => p.Name == name).Add(preset);
        await PersistAsync();
        return null;
    }

    public async Task DeleteAsync(string name)
    {
        _user = _user.RemoveAll(p => p.Name == name);
        await PersistAsync();
    }

    public ImmutableList<BlindPreset> UserPresets => _user;

    private ValueTask PersistAsync() =>
        storage.SetItemAsStringAsync(StorageKeys.Presets, JsonSerializer.Serialize(_user.ToList(), AppJsonContext.Default.ListBlindPreset));

    public async Task ReplaceUserPresetsAsync(IEnumerable<BlindPreset> presets)
    {
        _user = presets.Where(p => !p.Builtin && Builtins.All(b => b.Name != p.Name)).ToImmutableList();
        await PersistAsync();
    }
}
