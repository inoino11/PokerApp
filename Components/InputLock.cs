namespace PokerApp.Components;

/// <summary>
/// 操作パネルの中身が切り替わった直後に、入力を受け付けない時間。
/// 切り替わる前のボタンを二度押ししたとき、同じ位置に出た別のボタン（次の人の Fold など）を押さないようにする。
/// 待たされる感覚が出ない程度に短くする。
/// </summary>
public static class InputLock
{
    public const int Milliseconds = 175;
}
