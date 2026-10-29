namespace PokerApp.Models;

public static class Chips
{
    public static string Format(long amount) => amount.ToString("N0");

    public static string Format(BlindLevel l) =>
        l.Third > 0 ? $"{Format(l.Sb)}/{Format(l.Bb)}/{Format(l.Third)}" : $"{Format(l.Sb)}/{Format(l.Bb)}";

    /// <summary>
    /// プレイヤーの色。ColorIndex で引く。
    /// </summary>
    public static readonly string[] PlayerColors =
        ["#e53935", "#1e88e5", "#43a047", "#fb8c00", "#8e24aa", "#00acc1", "#f4511e", "#3949ab", "#c0ca33", "#6d4c41"];
}
