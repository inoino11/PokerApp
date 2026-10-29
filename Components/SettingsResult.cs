using PokerApp.Models;

namespace PokerApp.Components;

public enum SettingsAction { Apply, NewGame }

/// <summary>
/// 設定ダイアログの結果。Apply なら Settings を持つ。
/// </summary>
public sealed record SettingsResult(SettingsAction Action, GameSettings? Settings);
