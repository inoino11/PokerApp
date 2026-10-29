using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Playwright;

namespace PokerApp.E2E;

/// <summary>
/// 公開物（dotnet publish の wwwroot）をその場で配信し、ブラウザを開く。
/// E2E_WWWROOT で公開物の場所、E2E_BROWSER_CHANNEL でブラウザ（msedge など）、E2E_ARTIFACTS で動画の保存先を変えられる。
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = default!;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("E2E_WWWROOT") ?? Path.Combine(RepoRoot(), "release", "wwwroot"));
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException($"公開物がありません: {root}（先に dotnet publish -c Release -o release を実行する）");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        var files = new PhysicalFileProvider(root);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".blat"] = "application/octet-stream";
        types.Mappings[".webmanifest"] = "application/manifest+json";
        _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        _app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        _app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/";

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL"),
        });
    }

    /// <summary>
    /// スマホ幅の新しいページを開き、アプリの起動を待つ。
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width = 390, int height = 844)
    {
        var artifacts = Environment.GetEnvironmentVariable("E2E_ARTIFACTS");
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "ja-JP",
            RecordVideoDir = string.IsNullOrEmpty(artifacts) ? null : artifacts,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl);
        await page.Locator(".player-add").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        return (context, page);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PokerApp.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つかりません。");
    }
}

[CollectionDefinition(nameof(AppCollection))]
public sealed class AppCollection : ICollectionFixture<AppFixture>;
