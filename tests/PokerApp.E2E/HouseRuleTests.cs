using Microsoft.Playwright;

namespace PokerApp.E2E;

[Collection(nameof(AppCollection))]
public class HouseRuleTests(AppFixture app)
{
    // 設定の「ハウスルール」を変えて確定する
    internal static async Task SetHouseRulesAsync(IPage page, string? runIt = null, string? sevenDeuceBb = null)
    {
        await page.GetByLabel("設定").ClickAsync();
        if (runIt is not null)
        {
            await page.Locator(".house-rules .mud-select").First.ClickAsync();
            await page.GetByRole(AriaRole.Option, new() { Name = runIt }).ClickAsync();
        }
        if (sevenDeuceBb is not null)
        {
            var field = page.GetByLabel("72o（BB）");
            await field.FillAsync(sevenDeuceBb);
            await field.PressAsync("Tab");
        }
        await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
    }

    internal static async Task AddPlayersAsync(IPage page, int count)
    {
        for (var i = 0; i < count; i++) await page.Locator(".player-add").ClickAsync();
    }

    [Fact]
    public async Task オールインのあとTwiceを選ぶと回ごとに勝者を選べる()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await SetHouseRulesAsync(page, runIt: "Twice まで");
            await AddPlayersAsync(page, 2);
            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "All-in", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "Twice", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".showdown-title")).ToContainTextAsync("1 回目 Main pot");
            await Assertions.Expect(page.Locator(".showdown-later")).ToContainTextAsync("2 回目 Main pot");
            await page.Locator(".winner-btn", new() { HasTextString = "P1" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".showdown-title")).ToContainTextAsync("2 回目 Main pot");
            await page.Locator(".winner-btn", new() { HasTextString = "P2" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();

            // 1 回ずつ取ったので、どちらも元の額に戻る
            await Assertions.Expect(page.GetByText("前回の結果")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".player-stack").Nth(0)).ToHaveTextAsync("10,000");
            await Assertions.Expect(page.Locator(".player-stack").Nth(1)).ToHaveTextAsync("10,000");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task セブンデュースを入れると配られた全員から受け取る()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await SetHouseRulesAsync(page, sevenDeuceBb: "2");
            await AddPlayersAsync(page, 3);
            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();

            // P3（BB）が SB の 50 を取ったあと、72o で P1・P2 から 200 ずつ
            await page.GetByRole(AriaRole.Button, new() { Name = "72o", Exact = true }).ClickAsync();
            await page.Locator(".sd-btn", new() { HasTextString = "P3" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".result-bonus")).ToHaveTextAsync("72o P3 +400");
            await Assertions.Expect(page.GetByText("P3 +450")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "72o", Exact = true })).ToHaveCountAsync(0);

            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "72o", Exact = true })).ToBeVisibleAsync();
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
