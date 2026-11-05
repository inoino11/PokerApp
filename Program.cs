using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using PokerApp;
using PokerApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.TopCenter;
    config.SnackbarConfiguration.VisibleStateDuration = 2500;
    config.SnackbarConfiguration.PreventDuplicates = false;
    // 初期値（2 秒）だと ✖ を押しても消えないように見え、見えない通知が上の帯のボタンを 2 秒ふさぐ
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
});
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<GameStateService>();
builder.Services.AddScoped<ClockService>();
builder.Services.AddScoped<PresetService>();
builder.Services.AddScoped<DeviceService>();

await builder.Build().RunAsync();
