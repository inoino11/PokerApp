using Microsoft.JSInterop;

namespace PokerApp.Services;

/// <summary>
/// 端末と画面の機能（画面を消さない・レベルアップの通知・手番へのスクロール・席替えのドラッグ）の窓口。JS の呼び出しはここに閉じ込める。
/// </summary>
public sealed class DeviceService(IJSRuntime js)
{
    public async Task<string> KeepAwakeAsync()
    {
        try
        {
            return await js.InvokeAsync<string>("appDevice.keepAwake");
        }
        catch (JSException e)
        {
            return "ERROR: " + e.Message;
        }
    }

    public ValueTask NotifyLevelUpAsync() => js.InvokeVoidAsync("appDevice.levelUp");

    public ValueTask NotifyOneMinuteAsync() => js.InvokeVoidAsync("appDevice.oneMinute");

    public ValueTask ScrollToActiveAsync() => js.InvokeVoidAsync("appDevice.scrollToActive");

    /// <summary>
    /// 席替えのドラッグを受け付ける。離したときに target の OnSeatDropped(from, to) が呼ばれる。
    /// </summary>
    public ValueTask RegisterSeatDragAsync<T>(DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("seatDrag.register", target);

    public ValueTask UnregisterSeatDragAsync() => js.InvokeVoidAsync("seatDrag.unregister");
}
