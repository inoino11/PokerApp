using System.Reflection;
using Blazored.LocalStorage;

namespace PokerApp.Tests;

/// <summary>
/// 文字列の読み書きだけを受け持つ localStorage の代わり。アプリは文字列でしか読み書きしない。
/// </summary>
public class FakeStorage : DispatchProxy
{
    public Dictionary<string, string> Data { get; } = [];

    public static (ILocalStorageService Service, FakeStorage Fake) Create()
    {
        var service = Create<ILocalStorageService, FakeStorage>();
        return (service, (FakeStorage)(object)service);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "GetItemAsStringAsync" => new ValueTask<string?>(Data.GetValueOrDefault((string)args![0]!)),
        "SetItemAsStringAsync" => Set((string)args![0]!, (string)args[1]!),
        _ => throw new NotSupportedException(method?.Name),
    };

    private ValueTask Set(string key, string value)
    {
        Data[key] = value;
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan span) => Now += span;
}
