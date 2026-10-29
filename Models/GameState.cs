using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PokerApp.Models;

public enum PlayerStatus { Active, SittingOut, Left }

public enum Street { Preflop, Flop, Turn, River, Showdown }

public enum ActionKind { Fold, Check, Call, Raise, AllIn }

/// <summary>
/// プレイヤー。Stack はハンドに出していない手元の額（behind）。
/// 退席した人もリストに残す（次のボタンの位置を、前のボタンの人の並びの位置から決めるため）。BuyIn は今は画面に出さないが、精算に使えるよう記録だけしておく。
/// </summary>
public sealed record Player(int Id, string Name, int ColorIndex, long Stack, PlayerStatus Status, long BuyIn);

/// <summary>
/// 1 レベル分のブラインド。Third が 0 ならトリプルブラインドを使わない。
/// </summary>
public sealed record BlindLevel(long Sb, long Bb, long Third = 0);

/// <summary>
/// UNDO の対象になる設定。プリセットはここではなく PresetService が持つ。
/// </summary>
public sealed record GameSettings(
    ImmutableList<BlindLevel> Levels,
    int LevelMinutes,          // 0 ならブラインド固定
    bool TripleBlind,
    long StartingStack,
    long? ChipUnit,            // null なら今のレベルの SB
    bool RoundBets);

/// <summary>
/// ハンドに参加している 1 席分の状態。StreetBet はこのストリートで出した額、Contributed はハンド全体で出した額。
/// </summary>
public sealed record HandSeat(int PlayerId, long StreetBet, long Contributed, bool Folded, bool AllIn, bool Acted);

/// <summary>
/// 進行中のハンド。Seats はボタンの次の人から並べ、ボタンの人を最後に置く（チョップの端数もこの順で配る）。
/// WinnerTiers はショーダウンで選んだ勝者を強い順に積んだもの。
/// </summary>
public sealed record HandState(
    int Number,
    int ButtonId,
    BlindLevel Blinds,
    ImmutableList<int> BlindIds,  // SB, BB, 3 番目の順
    Street Street,
    ImmutableList<HandSeat> Seats,
    int? ToActId,
    long CurrentBet,
    long MinRaise,
    ImmutableList<ImmutableList<int>> WinnerTiers);

public sealed record PlayerResult(int PlayerId, long Won, long Contributed)
{
    [JsonIgnore] public long Net => Won - Contributed;
}

public sealed record HandResult(int HandNumber, ImmutableList<PlayerResult> Players);

/// <summary>
/// ゲーム全体の状態。不変オブジェクトで、操作のたびに GameEngine が新しい状態を作る。
/// Players の並び順が席順。ChosenButtonId はハンドの合間にユーザーが選んだ次のボタン（選んでいなければ null）。
/// </summary>
public sealed record GameState(
    ImmutableList<Player> Players,
    GameSettings Settings,
    HandState? Hand,
    int? LastButtonId,
    int NextPlayerId,
    int HandCount,
    HandResult? LastResult,
    int? ChosenButtonId = null)
{
    public const int MaxSeats = 10;

    public Player GetPlayer(int id) => Players.First(p => p.Id == id);

    [JsonIgnore] public IEnumerable<Player> Seated => Players.Where(p => p.Status != PlayerStatus.Left);
}

public sealed record Pot(long Amount, ImmutableList<int> EligibleIds);
