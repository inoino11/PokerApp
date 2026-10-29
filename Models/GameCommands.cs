using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PokerApp.Models;

/// <summary>
/// UNDO の 1 手になる操作。端末に保存して再生するので、時刻など外から決まる値は呼び出し側で決めて持たせる。
/// 判別子の文字列はリリース後に変えない（保存済みの履歴が読めなくなる）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(AddPlayer), "add")]
[JsonDerivedType(typeof(RenamePlayer), "rename")]
[JsonDerivedType(typeof(SetStack), "stack")]
[JsonDerivedType(typeof(SetStatus), "status")]
[JsonDerivedType(typeof(MovePlayer), "move")]
[JsonDerivedType(typeof(StartHand), "start")]
[JsonDerivedType(typeof(Act), "act")]
[JsonDerivedType(typeof(ChooseWinners), "winners")]
[JsonDerivedType(typeof(UpdateSettings), "settings")]
public abstract record GameCommand;

public sealed record AddPlayer(long Stack) : GameCommand;

public sealed record RenamePlayer(int PlayerId, string Name) : GameCommand;

/// <summary>
/// 手元の額を変える。CountAsBuyIn なら増えた分をリバイとして精算に計上する。
/// </summary>
public sealed record SetStack(int PlayerId, long Stack, bool CountAsBuyIn) : GameCommand;

public sealed record SetStatus(int PlayerId, PlayerStatus Status) : GameCommand;

/// <summary>
/// 席を隣の着席者と入れ替える。Direction は -1（上へ）か +1（下へ）。
/// </summary>
public sealed record MovePlayer(int PlayerId, int Direction) : GameCommand;

/// <summary>
/// ハンドを始める。レベルは時計から呼び出し側で決める（再生しても同じ結果にするため）。
/// </summary>
public sealed record StartHand(int LevelIndex) : GameCommand;

/// <summary>
/// To は Raise のときの「このストリートで出す合計」。ほかのアクションでは使わない。
/// </summary>
public sealed record Act(int PlayerId, ActionKind Kind, long To = 0) : GameCommand;

public sealed record ChooseWinners(ImmutableList<int> PlayerIds) : GameCommand;

public sealed record UpdateSettings(GameSettings Settings) : GameCommand;
