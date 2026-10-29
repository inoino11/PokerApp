namespace PokerApp;

/// <summary>
/// localStorage のキー。リリース後は値を変えない（利用者の端末に保存済みのデータが読めなくなる）。
/// </summary>
public static class StorageKeys
{
    public const string Game = "pokerapp.game";
    public const string Clock = "pokerapp.clock";
    public const string Presets = "pokerapp.presets";
}
