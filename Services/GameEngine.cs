using System.Collections.Immutable;
using PokerApp.Models;

namespace PokerApp.Services;

/// <summary>
/// ゲームの進行規則。GameState と GameCommand から次の GameState を作る純粋な関数の集まり。
/// 同じ状態に同じ操作を当てれば必ず同じ結果になる（端末に保存した操作の履歴を再生して状態を戻すため）。
/// できない操作は InvalidOperationException にする。
/// </summary>
public static class GameEngine
{
    public static GameState NewGame(GameSettings settings) =>
        new([], settings, null, null, 1, 0, null);

    public static GameState Apply(GameState s, GameCommand command) => DropStaleButtonChoice(command switch
    {
        AddPlayer c => AddPlayer(s, c),
        RenamePlayer c => Rename(s, c),
        SetStack c => SetStack(s, c),
        EditPlayer c => Edit(s, c),
        SetStatus c => SetStatus(s, c),
        // MovePlayer は旧版の履歴を読むためだけに残しているので、ハンド中の制限はかけない
        MovePlayer c => Seat(s, c.PlayerId, s.Seated.ToList().FindIndex(p => p.Id == c.PlayerId) + Math.Sign(c.Direction)),
        SeatPlayer c => s.Hand is null ? Seat(s, c.PlayerId, c.ToIndex) : throw new InvalidOperationException("ハンドの合間のみ席替え可能"),
        ChooseButton c => ChooseButton(s, c),
        RestartGame c => Restart(s, c),
        StartHand c => StartHand(s, c),
        Act c => Act(s, c),
        ChooseWinners c => ChooseWinners(s, c),
        ChooseRuns c => ChooseRuns(s, c),
        ClaimSevenDeuce c => ClaimSevenDeuce(s, c),
        UpdateSettings c => s with { Settings = Validate(c.Settings) },
        _ => throw new InvalidOperationException("不明な操作"),
    });

    // 選んだ D の人が離席・退席などで参加できなくなったら、選んだことを取り消す（戻ってきても選び直すまでは自動の規則）
    static GameState DropStaleButtonChoice(GameState s) =>
        s.ChosenButtonId is int id && !Eligible(s).Any(p => p.Id == id) ? s with { ChosenButtonId = null } : s;

    // --- 問い合わせ（画面から使う） ---

    public static long PotTotal(HandState hand) => hand.Seats.Sum(x => x.Contributed);

    public static long CallAmount(HandState hand, HandSeat seat, Player player) =>
        Math.Min(hand.CurrentBet - seat.StreetBet, player.Stack);

    /// <summary>
    /// 最小レイズの「合計額」。これ未満のレイズは打てるが画面で注意を出す。
    /// </summary>
    public static long MinRaiseTo(HandState hand) =>
        hand.CurrentBet == 0 ? hand.Blinds.Bb : hand.CurrentBet + hand.MinRaise;

    public static long MaxTo(HandSeat seat, Player player) => seat.StreetBet + player.Stack;

    /// <summary>
    /// 丸めや分配に使うチップの単位。設定がなければハンドの SB（ハンド外なら最初のレベルの SB）。
    /// </summary>
    public static long ChipUnit(GameState s) =>
        Math.Max(1, s.Settings.ChipUnit ?? s.Hand?.Blinds.Sb ?? s.Settings.Levels[0].Sb);

    /// <summary>
    /// 「今のポットの pct %」を新しく出すときの合計額。今のポットは場に出ているチップ全部で、100% ならポットがちょうど 2 倍になる。
    /// 丸めが有効なら unit のいちばん近い倍数にし、手元の額を超えたら全部にする。
    /// </summary>
    public static long PotPercentTo(GameState s, HandSeat seat, Player player, int pct)
    {
        var hand = s.Hand!;
        var to = seat.StreetBet + PotTotal(hand) * pct / 100;
        if (s.Settings.RoundBets) to = RoundToUnit(to, ChipUnit(s));
        return Math.Min(to, MaxTo(seat, player));
    }

    public static long RoundToUnit(long amount, long unit) =>
        unit <= 1 ? amount : (amount + unit / 2) / unit * unit;

    public static bool NeedsAction(HandState hand, HandSeat seat) =>
        !seat.Folded && !seat.AllIn && (!seat.Acted || seat.StreetBet < hand.CurrentBet);

    public static bool IsInPot(GameState s, int playerId) =>
        s.Hand?.Seats.FirstOrDefault(x => x.PlayerId == playerId) is { Folded: false, Contributed: > 0 };

    public static ImmutableList<Pot> Pots(HandState hand) =>
        PotCalculator.BuildPots(PotCalculator.ReturnUncalled(Contributions(hand)).Contributions);

    /// <summary>
    /// ショーダウンで次に勝者を選ぶポット。Run は何回目か（0 始まり）、Amount はその回の取り分。選ぶ必要がなければ null。
    /// </summary>
    public static PendingShowdown? PendingPot(GameState s)
    {
        var hand = s.Hand;
        if (hand is not { Street: Street.Showdown } || RunsPending(hand)) return null;
        var pots = Pots(hand);
        var runs = RunCount(hand);
        for (var r = 0; r < runs; r++)
        {
            var (_, pending) = PotCalculator.ResolveWinners(pots, Tiers(hand, r));
            if (pending is int i) return new(r, i, pots[i], RunShare(pots[i].Amount, runs, r, SplitUnit(s)));
        }
        return null;
    }

    public readonly record struct PendingShowdown(int Run, int Index, Pot Pot, long Amount);

    /// <summary>
    /// 今選んでいるポットのあとに決めるポット（同じ回の残りと、そのあとの回の全部）。
    /// </summary>
    public static IEnumerable<PendingShowdown> LaterPots(GameState s)
    {
        if (PendingPot(s) is not { } now) yield break;
        var pots = Pots(s.Hand!);
        var runs = RunCount(s.Hand!);
        for (var r = now.Run; r < runs; r++)
            for (var i = r == now.Run ? now.Index + 1 : 0; i < pots.Count; i++)
                yield return new(r, i, pots[i], RunShare(pots[i].Amount, runs, r, SplitUnit(s)));
    }

    /// <summary>
    /// オールインのあと、残りのカードを何回配るかを選ぶ場面か。
    /// </summary>
    public static bool RunsPending(HandState hand) => hand.Street == Street.Showdown && hand.Runs == 0;

    public static int RunCount(HandState hand) => hand.Runs is int r && r > 1 ? r : 1;

    static IReadOnlyList<IReadOnlyList<int>> Tiers(HandState hand, int run) =>
        RunCount(hand) == 1 ? hand.WinnerTiers : hand.RunTiers is { } t && run < t.Count ? t[run] : [];

    /// <summary>
    /// ポットを回数で分けたときの、run 回目の取り分。unit 単位で等分し、端数は 1 回目に足す。
    /// </summary>
    public static long RunShare(long amount, int runs, int run, long unit)
    {
        if (runs <= 1) return amount;
        unit = Math.Max(1, unit);
        var share = amount / runs / unit * unit;
        return run == 0 ? amount - share * (runs - 1) : share;
    }

    // チョップや Run it で分けるときの単位。丸めを切っていれば 1 チップ単位で分ける
    static long SplitUnit(GameState s) => s.Settings.RoundBets ? ChipUnit(s) : 1;

    /// <summary>
    /// 72o で 1 人が払う額。前のハンドの BB × 設定の BB 数を、Run it で取った回の割合で減らす（Twice で 1 回なら半分）。
    /// </summary>
    public static long SevenDeuceAmount(GameState s, HandResult result, PlayerResult winner)
    {
        var full = result.Bb * s.Settings.SevenDeuceBb;
        if (winner.RunsWon >= result.Runs) return full;
        var part = full * winner.RunsWon / Math.Max(1, result.Runs);
        // 前のハンドの SB は残していないので、最小チップの設定がなければ BB の半分で丸める
        var unit = s.Settings.ChipUnit ?? Math.Max(1, result.Bb / 2);
        return s.Settings.RoundBets ? RoundToUnit(part, unit) : part;
    }

    public static IEnumerable<Player> Eligible(GameState s) =>
        s.Players.Where(p => p.Status == PlayerStatus.Active && p.Stack > 0);

    /// <summary>
    /// 次のハンドのボタン。前のボタンの人の次の席へ移る。前のボタンの人が抜けていても、その人の席の位置から数える。
    /// 席替えで前のボタンの人を動かすと、ボタンもその人について動く。参加できる人が 2 人未満なら null。
    /// ユーザーが選んだ人がいて、その人が参加できるなら、その人にする（離席などで参加できなくなったら自動の規則に戻る）。
    /// </summary>
    public static int? NextButtonId(GameState s)
    {
        var eligible = Eligible(s).ToList();
        if (eligible.Count < 2) return null;
        if (s.ChosenButtonId is int chosen && eligible.Any(p => p.Id == chosen)) return chosen;
        if (s.LastButtonId is not int last || s.Players.FindIndex(p => p.Id == last) is not (var lastPos and >= 0))
            return eligible[0].Id;
        return (eligible.FirstOrDefault(p => s.Players.IndexOf(p) > lastPos) ?? eligible[0]).Id;
    }

    // --- プレイヤー ---

    static GameState AddPlayer(GameState s, AddPlayer c)
    {
        if (s.Seated.Count() >= GameState.MaxSeats) throw new InvalidOperationException($"席は {GameState.MaxSeats} まで");
        if (c.Stack < 0) throw new InvalidOperationException("額が不正");
        var id = s.NextPlayerId;
        var n = FreeNumber(s);
        var player = new Player(id, $"P{n}", (n - 1) % Chips.PlayerColors.Length, c.Stack,
            c.Stack > 0 ? PlayerStatus.Active : PlayerStatus.SittingOut, c.Stack);
        return s with { Players = s.Players.Add(player), NextPlayerId = id + 1 };
    }

    // 内部の番号（Id）は履歴と結果が人を指すのに使うので使い回さないが、画面の名前と色は卓にいる人の中でかぶらなければよい。
    // 退席した人の番号と色を次に追加した人が使い、入れ替わりが多くても P1〜P10 と 10 色に収める
    static int FreeNumber(GameState s)
    {
        var present = s.Players.Where(p => p.Status != PlayerStatus.Left).ToList();
        var n = 1;
        while (present.Any(p => p.ColorIndex % Chips.PlayerColors.Length == (n - 1) % Chips.PlayerColors.Length || p.Name == $"P{n}")) n++;
        return n;
    }

    static GameState Rename(GameState s, RenamePlayer c)
    {
        var name = c.Name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("プレイヤー名未入力");
        return UpdatePlayer(s, c.PlayerId, p => p with { Name = name });
    }

    static GameState Edit(GameState s, EditPlayer c)
    {
        var player = s.GetPlayer(c.PlayerId);
        if (c.Name.Trim() != player.Name) s = Rename(s, new RenamePlayer(c.PlayerId, c.Name));
        return c.Stack == player.Stack ? s : SetStack(s, new SetStack(c.PlayerId, c.Stack, CountAsBuyIn: true));
    }

    static GameState SetStack(GameState s, SetStack c)
    {
        if (c.Stack < 0) throw new InvalidOperationException("額が不正");
        var player = s.GetPlayer(c.PlayerId);
        if (player.Status == PlayerStatus.Left) throw new InvalidOperationException("退席済みのため額の変更不可");
        var delta = c.Stack - player.Stack;
        var status = player.Status;
        if (c.Stack == 0) status = PlayerStatus.SittingOut;
        else if (player.Stack == 0 && status == PlayerStatus.SittingOut) status = PlayerStatus.Active;

        // ハンドで降りていない人は、手元が 0 ならオールイン扱い。増えたら行動を続けられる
        var seat = s.Hand?.Seats.FirstOrDefault(x => x.PlayerId == c.PlayerId);
        if (seat is { Folded: false })
        {
            status = player.Status;
            s = UpdateSeat(s, c.PlayerId, x => x with { AllIn = c.Stack == 0 });
        }

        s = UpdatePlayer(s, c.PlayerId, p => p with
        {
            Stack = c.Stack,
            Status = status,
            BuyIn = p.BuyIn + (c.CountAsBuyIn && delta > 0 ? delta : 0),
        });
        return seat is { Folded: false } ? Normalize(s) : s;
    }

    static GameState SetStatus(GameState s, SetStatus c)
    {
        var player = s.GetPlayer(c.PlayerId);
        if (player.Status == PlayerStatus.Left) throw new InvalidOperationException("退席済み");
        if (c.Status == PlayerStatus.Active)
        {
            if (player.Stack <= 0) throw new InvalidOperationException("手元のチップなし");
            return UpdatePlayer(s, c.PlayerId, p => p with { Status = PlayerStatus.Active });
        }

        if (IsInPot(s, c.PlayerId)) throw new InvalidOperationException("ポットに参加中のため、ハンド終了まで離席不可");
        // まだチップを出していない参加者は、降りたことにして抜ける
        if (s.Hand?.Seats.FirstOrDefault(x => x.PlayerId == c.PlayerId) is { Folded: false })
        {
            s = UpdateSeat(s, c.PlayerId, x => x with { Folded = true, Acted = true });
            s = UpdatePlayer(s, c.PlayerId, p => p with { Status = c.Status });
            return Normalize(s);
        }
        return UpdatePlayer(s, c.PlayerId, p => p with { Status = c.Status });
    }

    /// <summary>
    /// 着席者の中で toIndex の位置へ動かす。退席した人はリストの元の位置に残す（前のボタンの席から数えるため）。
    /// </summary>
    static GameState Seat(GameState s, int playerId, int toIndex)
    {
        var seated = s.Seated.ToList();
        var from = seated.FindIndex(p => p.Id == playerId);
        if (from < 0 || toIndex < 0 || toIndex >= seated.Count || toIndex == from) return s;
        var moving = seated[from];
        seated.RemoveAt(from);
        seated.Insert(toIndex, moving);

        var next = 0;
        var players = s.Players.Select(p => p.Status == PlayerStatus.Left ? p : seated[next++]).ToImmutableList();
        return s with { Players = players };
    }

    // ハンドの途中に変えると、払ったブラインドと手番の順を組み直すことになるので、合間だけにする
    static GameState ChooseButton(GameState s, ChooseButton c)
    {
        if (s.Hand is not null) throw new InvalidOperationException("ハンドの合間のみ D の変更可能");
        if (!Eligible(s).Any(p => p.Id == c.PlayerId)) throw new InvalidOperationException("次のハンドの参加者のみ D に指定可能");
        return s with { ChosenButtonId = c.PlayerId };
    }

    // 途中のハンドは捨てる。次の D は最初のゲームと同じく先頭の人
    static GameState Restart(GameState s, RestartGame c)
    {
        if (!c.KeepPlayers) return NewGame(s.Settings);
        var stack = s.Settings.StartingStack;
        var players = s.Players
            .Where(p => p.Status != PlayerStatus.Left)
            .Select(p => p with { Stack = stack, BuyIn = stack, Status = stack > 0 ? PlayerStatus.Active : PlayerStatus.SittingOut })
            .ToImmutableList();
        return NewGame(s.Settings) with { Players = players, NextPlayerId = s.NextPlayerId };
    }

    // --- ハンドの開始 ---

    static GameState StartHand(GameState s, StartHand c)
    {
        if (s.Hand is not null) throw new InvalidOperationException("ハンド進行中");
        var eligible = Eligible(s).ToList();
        var buttonId = NextButtonId(s) ?? throw new InvalidOperationException("参加できる人が 2 人以上必要");
        var bi = eligible.FindIndex(p => p.Id == buttonId);
        var button = eligible[bi];
        var order = eligible.Skip(bi + 1).Concat(eligible.Take(bi + 1)).ToList();
        var n = order.Count;

        var level = s.Settings.Levels[Math.Clamp(c.LevelIndex, 0, s.Settings.Levels.Count - 1)];
        var useThird = s.Settings.TripleBlind && level.Third > 0 && n >= 4;
        var blinds = useThird ? level : level with { Third = 0 };

        // 2 人のときはボタンが SB
        int[] blindIdx = n == 2 ? [n - 1, 0] : useThird ? [0, 1, 2] : [0, 1];
        var amounts = new[] { blinds.Sb, blinds.Bb, blinds.Third };

        var seats = order.Select(p => new HandSeat(p.Id, 0, 0, false, false, false)).ToList();
        var players = s.Players;
        for (var k = 0; k < blindIdx.Length; k++)
        {
            var p = order[blindIdx[k]];
            var stack = players.First(x => x.Id == p.Id).Stack;
            var put = Math.Min(amounts[k], stack);
            seats[blindIdx[k]] = seats[blindIdx[k]] with { StreetBet = put, Contributed = put, AllIn = put == stack };
            players = players.Replace(players.First(x => x.Id == p.Id), players.First(x => x.Id == p.Id) with { Stack = stack - put });
        }

        var top = Math.Max(blinds.Bb, blinds.Third);
        var first = (blindIdx[^1] + 1) % n;
        var hand = new HandState(
            s.HandCount + 1, button.Id, blinds,
            blindIdx.Select(i => order[i].Id).ToImmutableList(),
            Street.Preflop, seats.ToImmutableList(), order[first].Id, top, top, []);
        return Normalize(s with { Players = players, Hand = hand, ChosenButtonId = null });
    }

    // --- アクション ---

    static GameState Act(GameState s, Act c)
    {
        var hand = s.Hand ?? throw new InvalidOperationException("ハンド未開始");
        if (hand.Street == Street.Showdown || hand.ToActId != c.PlayerId) throw new InvalidOperationException("手番外");
        var idx = hand.Seats.FindIndex(x => x.PlayerId == c.PlayerId);
        var seat = hand.Seats[idx];
        var player = s.GetPlayer(c.PlayerId);

        long put = 0;
        var folded = false;
        switch (c.Kind)
        {
            case ActionKind.Fold:
                folded = true;
                break;
            case ActionKind.Check:
                if (seat.StreetBet != hand.CurrentBet) throw new InvalidOperationException("チェック不可");
                break;
            case ActionKind.Call:
                if (seat.StreetBet >= hand.CurrentBet) throw new InvalidOperationException("コール額なし");
                put = CallAmount(hand, seat, player);
                break;
            case ActionKind.Raise:
                if (c.To <= hand.CurrentBet) throw new InvalidOperationException("今のベットより多い額が必要");
                put = c.To - seat.StreetBet;
                if (put > player.Stack) throw new InvalidOperationException("手元のチップ不足");
                break;
            case ActionKind.AllIn:
                put = player.Stack;
                break;
        }

        var newBet = seat.StreetBet + put;
        seat = seat with
        {
            StreetBet = newBet,
            Contributed = seat.Contributed + put,
            Folded = folded,
            AllIn = !folded && put == player.Stack && put > 0,
            Acted = true,
        };
        var seats = hand.Seats.SetItem(idx, seat);
        var currentBet = hand.CurrentBet;
        var minRaise = hand.MinRaise;
        if (newBet > currentBet)
        {
            // 最小レイズに届かないレイズでも、ほかの人には行動の機会を戻す（厳密なリオープン判定はしない）
            minRaise = Math.Max(minRaise, newBet - currentBet);
            currentBet = newBet;
            seats = seats.Select((x, i) => i == idx ? x : x with { Acted = false }).ToImmutableList();
        }

        hand = hand with { Seats = seats, CurrentBet = currentBet, MinRaise = minRaise };
        hand = hand with { ToActId = NextNeedingAction(hand, idx) };
        s = UpdatePlayer(s, c.PlayerId, p => p with { Stack = p.Stack - put });
        return Normalize(s with { Hand = hand });
    }

    static int? NextNeedingAction(HandState hand, int afterIndex)
    {
        var n = hand.Seats.Count;
        for (var k = 1; k <= n; k++)
        {
            var seat = hand.Seats[(afterIndex + k) % n];
            if (NeedsAction(hand, seat)) return seat.PlayerId;
        }
        return null;
    }

    /// <summary>
    /// 手番・ストリート・ハンドの終わりを整える。どの操作のあとにも呼ぶ。
    /// </summary>
    static GameState Normalize(GameState s)
    {
        var hand = s.Hand;
        if (hand is null) return s;
        if (hand.Street == Street.Showdown) return TryFinish(s);

        var live = hand.Seats.Where(x => !x.Folded).ToList();
        if (live.Count <= 1) return TryFinish(s with { Hand = hand with { Street = Street.Showdown, ToActId = null } });

        // 行動できるのが 1 人だけで、その人がベットに直面していなければ、もう行動は要らない
        var canAct = live.Count(x => !x.AllIn);
        var mustAct = canAct > 1 || live.Any(x => !x.AllIn && x.StreetBet < hand.CurrentBet);
        if (mustAct && hand.Seats.Any(x => NeedsAction(hand, x)))
        {
            var cur = hand.Seats.FindIndex(x => x.PlayerId == hand.ToActId);
            if (cur >= 0 && NeedsAction(hand, hand.Seats[cur])) return s;
            return s with { Hand = hand with { ToActId = NextNeedingAction(hand, cur < 0 ? hand.Seats.Count - 1 : cur) } };
        }

        // ベットがそろった。River より前にオールインで決着したら、残りのカードを何回配るかを選ばせる（Run it を使う設定のときだけ）
        if (canAct <= 1 || hand.Street == Street.River)
        {
            int? runs = canAct <= 1 && hand.Street < Street.River && s.Settings.MaxRuns > 1 ? 0 : null;
            return TryFinish(s with { Hand = hand with { Street = Street.Showdown, ToActId = null, Runs = runs } });
        }

        var next = hand with
        {
            Street = hand.Street + 1,
            Seats = hand.Seats.Select(x => x with { StreetBet = 0, Acted = false }).ToImmutableList(),
            CurrentBet = 0,
            MinRaise = hand.Blinds.Bb,
        };
        // ポストフロップはボタンの次（Seats の先頭）から
        next = next with { ToActId = NextNeedingAction(next, next.Seats.Count - 1) };
        return s with { Hand = next };
    }

    // --- ショーダウン ---

    static GameState ChooseWinners(GameState s, ChooseWinners c)
    {
        var hand = s.Hand ?? throw new InvalidOperationException("ハンド未開始");
        var pending = PendingPot(s) ?? throw new InvalidOperationException("勝者を選ぶポットなし");
        var ids = c.PlayerIds.Distinct().ToImmutableList();
        if (ids.Count == 0 || ids.Any(id => !pending.Pot.EligibleIds.Contains(id)))
            throw new InvalidOperationException("このポットの権利がない人を含む");
        if (RunCount(hand) == 1) return TryFinish(s with { Hand = hand with { WinnerTiers = hand.WinnerTiers.Add(ids) } });
        var runTiers = hand.RunTiers ?? EmptyRunTiers(RunCount(hand));
        return TryFinish(s with { Hand = hand with { RunTiers = runTiers.SetItem(pending.Run, runTiers[pending.Run].Add(ids)) } });
    }

    static ImmutableList<ImmutableList<ImmutableList<int>>> EmptyRunTiers(int runs) =>
        Enumerable.Repeat(ImmutableList<ImmutableList<int>>.Empty, runs).ToImmutableList();

    static GameState ChooseRuns(GameState s, ChooseRuns c)
    {
        var hand = s.Hand ?? throw new InvalidOperationException("ハンド未開始");
        if (!RunsPending(hand)) throw new InvalidOperationException("回数の選択不可");
        if (c.Count < 1 || c.Count > Math.Max(1, s.Settings.MaxRuns)) throw new InvalidOperationException("回数が不正");
        return TryFinish(s with
        {
            Hand = hand with { Runs = c.Count == 1 ? null : c.Count, RunTiers = c.Count == 1 ? null : EmptyRunTiers(c.Count) },
        });
    }

    static ImmutableList<PotCalculator.Contribution> Contributions(HandState hand) =>
        hand.Seats.Select(x => new PotCalculator.Contribution(x.PlayerId, x.Contributed, x.Folded)).ToImmutableList();

    /// <summary>
    /// すべてのポットの勝者が決まっていれば配ってハンドを終える。決まっていなければそのまま返す。
    /// </summary>
    static GameState TryFinish(GameState s)
    {
        var hand = s.Hand!;
        if (RunsPending(hand)) return s;
        var (contribs, returnTo, returnAmount) = PotCalculator.ReturnUncalled(Contributions(hand));
        var pots = PotCalculator.BuildPots(contribs);
        var runs = RunCount(hand);
        var winnersByRun = new List<ImmutableList<ImmutableList<int>>>();
        for (var r = 0; r < runs; r++)
        {
            var (winners, pending) = PotCalculator.ResolveWinners(pots, Tiers(hand, r));
            if (pending is not null) return s;
            winnersByRun.Add(winners);
        }

        var seatOrder = hand.Seats.Select(x => x.PlayerId).ToList();
        var unit = SplitUnit(s);
        var won = seatOrder.ToDictionary(id => id, _ => 0L);
        var runsWon = seatOrder.ToDictionary(id => id, _ => 0);
        for (var r = 0; r < runs; r++)
        {
            for (var i = 0; i < pots.Count; i++)
            {
                var ordered = seatOrder.Where(winnersByRun[r][i].Contains).ToList();
                foreach (var (id, amount) in PotCalculator.Split(RunShare(pots[i].Amount, runs, r, unit), ordered, unit)) won[id] += amount;
            }
            foreach (var id in winnersByRun[r].SelectMany(w => w).Distinct()) runsWon[id]++;
        }

        var players = s.Players;
        foreach (var seat in hand.Seats)
        {
            var back = won[seat.PlayerId] + (seat.PlayerId == returnTo ? returnAmount : 0);
            var p = players.First(x => x.Id == seat.PlayerId);
            var stack = p.Stack + back;
            var status = stack == 0 && p.Status == PlayerStatus.Active ? PlayerStatus.SittingOut : p.Status;
            players = players.Replace(p, p with { Stack = stack, Status = status });
        }

        var result = new HandResult(hand.Number, contribs
            .Select(x => new PlayerResult(x.PlayerId, won[x.PlayerId], x.Amount, runsWon[x.PlayerId]))
            .ToImmutableList(), hand.Blinds.Bb, runs);
        return s with
        {
            Players = players,
            Hand = null,
            LastButtonId = hand.ButtonId,
            HandCount = hand.Number,
            LastResult = result,
        };
    }

    // --- 72o ---

    // 払うのは前のハンドに配られた全員（降りた人も含む）。その後に退席した人は持ち帰ったので払わない。足りない人は手元の全部を払う
    static GameState ClaimSevenDeuce(GameState s, ClaimSevenDeuce c)
    {
        if (s.Hand is not null) throw new InvalidOperationException("ハンドの合間のみ 72o 入力可能");
        var result = s.LastResult ?? throw new InvalidOperationException("前のハンドなし");
        if (s.Settings.SevenDeuceBb <= 0) throw new InvalidOperationException("72o ボーナス未設定");
        if (result.Bb <= 0) throw new InvalidOperationException("このハンドは 72o 入力不可");
        if (result.SevenDeuceId is not null) throw new InvalidOperationException("72o は入力済み");
        var me = result.Players.FirstOrDefault(p => p.PlayerId == c.PlayerId);
        if (me is not { RunsWon: > 0 }) throw new InvalidOperationException("72o の対象外");
        if (s.GetPlayer(c.PlayerId).Status == PlayerStatus.Left) throw new InvalidOperationException("退席済み");

        var amount = SevenDeuceAmount(s, result, me);
        var paid = new Dictionary<int, long>();
        foreach (var r in result.Players.Where(r => r.PlayerId != c.PlayerId))
        {
            var p = s.GetPlayer(r.PlayerId);
            if (p.Status == PlayerStatus.Left) continue;
            var pay = Math.Min(amount, p.Stack);
            paid[p.Id] = pay;
            var stack = p.Stack - pay;
            s = UpdatePlayer(s, p.Id, x => x with
            {
                Stack = stack,
                Status = stack == 0 && x.Status == PlayerStatus.Active ? PlayerStatus.SittingOut : x.Status,
            });
        }
        var total = paid.Values.Sum();
        s = UpdatePlayer(s, c.PlayerId, x => x with { Stack = x.Stack + total });
        var players = result.Players
            .Select(r => r with { Bonus = r.PlayerId == c.PlayerId ? total : -paid.GetValueOrDefault(r.PlayerId) })
            .ToImmutableList();
        return s with { LastResult = result with { Players = players, SevenDeuceId = c.PlayerId } };
    }

    // --- 補助 ---

    static GameSettings Validate(GameSettings settings)
    {
        if (settings.Levels.Count == 0) throw new InvalidOperationException("ブラインドのレベル未設定");
        if (settings.Levels.Any(l => l.Sb < 0 || l.Bb <= 0 || l.Third < 0)) throw new InvalidOperationException("ブラインドの額が不正");
        if (settings.LevelMinutes < 0) throw new InvalidOperationException("レベルの時間が不正");
        if (settings.SevenDeuceBb < 0) throw new InvalidOperationException("72o ボーナスが不正");
        if (settings.MaxRuns is < 1 or > 3) throw new InvalidOperationException("Run it の回数が不正");
        return settings;
    }

    static GameState UpdatePlayer(GameState s, int id, Func<Player, Player> f)
    {
        var p = s.GetPlayer(id);
        return s with { Players = s.Players.Replace(p, f(p)) };
    }

    static GameState UpdateSeat(GameState s, int id, Func<HandSeat, HandSeat> f)
    {
        var hand = s.Hand!;
        var i = hand.Seats.FindIndex(x => x.PlayerId == id);
        return s with { Hand = hand with { Seats = hand.Seats.SetItem(i, f(hand.Seats[i])) } };
    }
}
