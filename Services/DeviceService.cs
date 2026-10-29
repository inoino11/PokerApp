using Microsoft.JSInterop;

namespace PokerApp.Services;

/// <summary>
/// 端末と画面の機能（画面を消さない・レベルアップの通知・手番へのスクロール・ファイルの保存）の窓口。JS の呼び出しはここに閉じ込める。
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

    public ValueTask ScrollToActiveAsync() => js.InvokeVoidAsync("appDevice.scrollToActive");

    public ValueTask DownloadAsync(string fileName, string text) => js.InvokeVoidAsync("appDevice.download", fileName, text);
}
