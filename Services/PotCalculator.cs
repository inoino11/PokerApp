using System.Collections.Immutable;
using PokerApp.Models;

namespace PokerApp.Services;

/// <summary>
/// ポットの分け方と配り方。状態を持たない純粋な計算。
/// </summary>
public static class PotCalculator
{
    public readonly record struct Contribution(int PlayerId, long Amount, bool Folded);

    /// <summary>
    /// 誰もコールしなかった超過分を返す。いちばん多く出した人が 1 人だけなら、2 番目との差がその人に戻る。
    /// 戻り値は（返却後の出した額, 返す相手, 返す額）。
    /// </summary>
    public static (ImmutableList<Contribution> Contributions, int? ReturnTo, long ReturnAmount) ReturnUncalled(IReadOnlyList<Contribution> contributions)
    {
        if (contributions.Count < 2) return (contributions.ToImmutableList(), null, 0);
        var sorted = contributions.OrderByDescending(c => c.Amount).ToList();
        var top = sorted[0];
        var second = sorted[1].Amount;
        if (top.Amount <= second) return (contributions.ToImmutableList(), null, 0);
        var excess = top.Amount - second;
        var adjusted = contributions.Select(c => c.PlayerId == top.PlayerId ? c with { Amount = second } : c).ToImmutableList();
        return (adjusted, top.PlayerId, excess);
    }

    /// <summary>
    /// メインポットとサイドポットを作る。フォールドした人の額もポットに入るが、受け取る権利はない。
    /// 先頭がメインポットで、後ろほど権利を持つ人が少ない。
    /// </summary>
    public static ImmutableList<Pot> BuildPots(IReadOnlyList<Contribution> contributions)
    {
        var live = contributions.Where(c => !c.Folded).ToList();
        var total = contributions.Sum(c => c.Amount);
        var levels = live.Select(c => c.Amount).Where(a => a > 0).Distinct().Order().ToList();
        if (levels.Count == 0)
        {
            return total == 0 && live.Count == 0
                ? []
                : [new Pot(total, live.Select(c => c.PlayerId).ToImmutableList())];
        }

        var pots = ImmutableList.CreateBuilder<Pot>();
        long prev = 0;
        foreach (var level in levels)
        {
            var amount = contributions.Sum(c => Math.Min(c.Amount, level) - Math.Min(c.Amount, prev));
            var eligible = live.Where(c => c.Amount >= level).Select(c => c.PlayerId).ToImmutableList();
            pots.Add(new Pot(amount, eligible));
            prev = level;
        }

        // 残っている人の誰よりも多く出してからフォールドした分は、最後のポットに入れる
        var overflow = contributions.Sum(c => Math.Max(0, c.Amount - prev));
        if (overflow > 0) pots[^1] = pots[^1] with { Amount = pots[^1].Amount + overflow };
        return pots.ToImmutable();
    }

    /// <summary>
    /// 各ポットの勝者を決める。WinnerTiers は強い順の勝者の組で、ポットの権利者のうち最も強い組がそのポットを取る。
    /// 決められないポットに来たらそこで止め、その位置を返す（全部決まれば null）。
    /// 権利者が 1 人だけのポットは入力なしでその人のものにする。
    /// </summary>
    public static (ImmutableList<ImmutableList<int>> Winners, int? PendingIndex) ResolveWinners(
        IReadOnlyList<Pot> pots, IReadOnlyList<IReadOnlyList<int>> winnerTiers)
    {
        var winners = ImmutableList.CreateBuilder<ImmutableList<int>>();
        for (var i = 0; i < pots.Count; i++)
        {
            var eligible = pots[i].EligibleIds;
            if (eligible.Count == 1)
            {
                winners.Add(eligible);
                continue;
            }
            var tier = winnerTiers.Select(t => eligible.Where(t.Contains).ToImmutableList()).FirstOrDefault(w => w.Count > 0);
            if (tier is null) return (winners.ToImmutable(), i);
            winners.Add(tier);
        }
        return (winners.ToImmutable(), null);
    }

    /// <summary>
    /// 1 つのポットを勝者で分ける。unit 単位で均等に分け、余りは winnersInSeatOrder の先頭から unit ずつ配る。
    /// </summary>
    public static ImmutableDictionary<int, long> Split(long amount, IReadOnlyList<int> winnersInSeatOrder, long unit)
    {
        if (unit < 1) unit = 1;
        var n = winnersInSeatOrder.Count;
        var share = amount / n / unit * unit;
        var result = winnersInSeatOrder.ToDictionary(id => id, _ => share);
        var rest = amount - share * n;
        for (var i = 0; rest > 0; i = (i + 1) % n)
        {
            var chunk = Math.Min(unit, rest);
            result[winnersInSeatOrder[i]] += chunk;
            rest -= chunk;
        }
        return result.ToImmutableDictionary();
    }
}
