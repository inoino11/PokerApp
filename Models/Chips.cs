using System.Globalization;

namespace PokerApp.Models;

public static class Chips
{
    public static string Format(long amount) => amount.ToString("N0");

    /// <summary>
    /// 入力された額を読む。全角数字・カンマ・空白は許す。0 以上 1 兆未満の整数でなければ false。
    /// </summary>
    public static bool TryParse(string? text, out long amount)
    {
        // ブラウザでは Unicode 正規化が使えないので、全角の数字とカンマは自前で半角にする
        var s = new string((text ?? "")
            .Select(c => c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c == '，' ? ',' : c)
            .Where(c => c is not (',' or ' ' or '　'))
            .ToArray());
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount < 1_000_000_000_000;
    }

    // 区切りは改行しない空白で挟む（ボタンの中などで「50 /」と「100」の間で折り返さないように）
    public static string Format(BlindLevel l) =>
        l.Third > 0 ? $"{Format(l.Sb)}\u00A0/\u00A0{Format(l.Bb)}\u00A0/\u00A0{Format(l.Third)}" : $"{Format(l.Sb)}\u00A0/\u00A0{Format(l.Bb)}";

    /// <summary>
    /// スタックが BB の何倍かを「32 BB」の形で返す。残りが少ないほど細かさが要るので、10 BB 未満は小数 1 桁にする（どちらも切り捨て）。
    /// スタックが 0 か BB が 0 以下なら空。
    /// </summary>
    public static string FormatBb(long stack, long bb)
    {
        if (stack <= 0 || bb <= 0) return "";
        if (stack >= bb * 10) return $"{stack / bb:N0} BB";
        var tenths = stack * 10 / bb;
        return $"{tenths / 10}.{tenths % 10} BB";
    }

    /// <summary>
    /// プレイヤーの色。ColorIndex で引く。
    /// </summary>
    public static readonly string[] PlayerColors =
        ["#e53935", "#1e88e5", "#43a047", "#fb8c00", "#8e24aa", "#00acc1", "#f4511e", "#3949ab", "#c0ca33", "#6d4c41"];
}
