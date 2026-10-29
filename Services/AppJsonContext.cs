using System.Text.Json.Serialization;

namespace PokerApp.Services;

/// <summary>
/// 端末に保存する形式の JSON。公開版はトリミングされるので、リフレクションに頼らずソース生成で読み書きする。
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(SavedGame))]
[JsonSerializable(typeof(BackupFile))]
[JsonSerializable(typeof(ClockData))]
[JsonSerializable(typeof(List<BlindPreset>))]
public sealed partial class AppJsonContext : JsonSerializerContext;
