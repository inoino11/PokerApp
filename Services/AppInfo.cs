using System.Reflection;

namespace PokerApp.Services;

/// <summary>
/// アプリの名前とバージョン。名前を変えるときは index.html（title・起動画面・OGP）と manifest.json もそろえる。
/// </summary>
public static class AppInfo
{
    public const string DisplayName = "シマリス";
    public const string Subtitle = "ポーカーチップ管理";

    // ブラウザのタブに出す名前
    public const string PageTitle = $"{DisplayName} - {Subtitle} -";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
}
