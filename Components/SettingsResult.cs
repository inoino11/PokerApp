using PokerApp.Models;

namespace PokerApp.Components;

public enum SettingsAction { Apply, NewGame, Export, Import }

/// <summary>
/// 設定ダイアログの結果。Apply なら Settings、Import なら Json を持つ。
/// </summary>
public sealed record SettingsResult(SettingsAction Action, GameSettings? Settings, string? Json);
