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
/// 後ろの 2 つはハウスルールで、古い保存データにはないので既定値で読む。
/// </summary>
public sealed record GameSettings(
    ImmutableList<BlindLevel> Levels,
    int LevelMinutes,          // 0 ならブラインド固定
    bool TripleBlind,
    long StartingStack,
    long? ChipUnit,            // null なら今のレベルの SB
    bool RoundBets,
    int SevenDeuceBb = 0,      // 72o のボーナス（1 人あたり何 BB か）。0 なら使わない
    int MaxRuns = 1);          // Run it の上限（1 なら使わない、2 なら Twice まで、3 なら Thrice まで）

/// <summary>
/// ハンドに参加している 1 席分の状態。StreetBet はこのストリートで出した額、Contributed はハンド全体で出した額。
/// </summary>
public sealed record HandSeat(int PlayerId, long StreetBet, long Contributed, bool Folded, bool AllIn, bool Acted);

/// <summary>
/// 進行中のハンド。Seats はボタンの次の人から並べ、ボタンの人を最後に置く（チョップの端数もこの順で配る）。
/// WinnerTiers はショーダウンで選んだ勝者を強い順に積んだもの。
/// Runs は Run it の回数で、null は 1 回、0 は回数を選ぶ前。2 回以上のときは、勝者を WinnerTiers ではなく RunTiers に回ごとに積む。
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
    ImmutableList<ImmutableList<int>> WinnerTiers,
    int? Runs = null,
    ImmutableList<ImmutableList<ImmutableList<int>>>? RunTiers = null);

/// <summary>
/// 1 人分のハンドの結果。RunsWon はポットを（一部でも）取った回の数、Bonus は 72o で受け取った額（払った人は負）。
/// </summary>
public sealed record PlayerResult(int PlayerId, long Won, long Contributed, int RunsWon = 0, long Bonus = 0)
{
    [JsonIgnore] public long Net => Won - Contributed + Bonus;
}

/// <summary>
/// 前のハンドの結果。Bb と Runs は 72o の額を決めるのに使う（古い保存データでは Bb が 0 なので 72o を入れられない）。
/// SevenDeuceId は 72o を受け取った人（入れていなければ null）。
/// </summary>
public sealed record HandResult(int HandNumber, ImmutableList<PlayerResult> Players, long Bb = 0, int Runs = 1, int? SevenDeuceId = null);

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
