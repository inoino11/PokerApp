using System.Reflection;

namespace PokerApp.Services;

/// <summary>
/// アプリの名前とバージョン。表示名を変えるときは index.html の title と manifest.json もそろえる。
/// </summary>
public static class AppInfo
{
    public const string DisplayName = "ポーカーチップ";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
}
