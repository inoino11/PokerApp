using Microsoft.Playwright;

namespace PokerApp.E2E;

[Collection(nameof(AppCollection))]
public class TableTests(AppFixture app)
{
    private static Task AddPlayers(IPage page, int count) =>
        Enumerable.Range(0, count).Aggregate(Task.CompletedTask, async (t, _) =>
        {
            await t;
            await page.Locator(".player-add").ClickAsync();
        });

    [Fact]
    public async Task 全員が降りるとBBが取り_結果が出る()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            await page.GetByText("最初のハンドを始める").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("ハンド #1 の結果")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("P3 +50")).ToBeVisibleAsync();
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ポットの割合で額を決めてレイズし_元に戻せる()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            await page.GetByText("最初のハンドを始める").ClickAsync();
            // ポット 150 の 100% → 150 を新しく出す
            await page.GetByRole(AriaRole.Button, new() { Name = "100%" }).ClickAsync();
            await Assertions.Expect(page.Locator(".sizer-value")).ToHaveTextAsync("150");
            await page.GetByRole(AriaRole.Button, new() { Name = "Raise", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".pot-total")).ToHaveTextAsync("300");

            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(page.Locator(".pot-total")).ToHaveTextAsync("150");

            // 開き直しても状態が残る
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator(".pot-total")).ToHaveTextAsync("150", new() { Timeout = 60_000 });
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task サイドポットの勝者を順に選ぶ()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            // P1 の手元を 1,000 にする
            await page.Locator(".player-row").First.ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "額を変える" }).ClickAsync();
            foreach (var key in new[] { "C", "1", "000" })
                await page.Locator(".keypad-key", new() { HasTextString = key }).First.ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "決定" }).ClickAsync();
            await Assertions.Expect(page.Locator(".player-stack").First).ToHaveTextAsync("1,000");

            await page.GetByText("最初のハンドを始める").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "All-in", Exact = true }).ClickAsync(); // P1
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P2
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P3
            await page.GetByRole(AriaRole.Button, new() { Name = "Bet", Exact = true }).ClickAsync();     // フロップ P2
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P3
            for (var i = 0; i < 4; i++) await page.GetByRole(AriaRole.Button, new() { Name = "Check", Exact = true }).ClickAsync();

            await Assertions.Expect(page.GetByText("メインポット")).ToBeVisibleAsync();
            await page.Locator(".winner-btn", new() { HasTextString = "P1" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "勝者に決定" }).ClickAsync();
            await Assertions.Expect(page.GetByText("サイドポット 1")).ToBeVisibleAsync();
            await page.Locator(".winner-btn", new() { HasTextString = "P2" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "勝者に決定" }).ClickAsync();
            await Assertions.Expect(page.GetByText("ハンド #1 の結果")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".player-stack").First).ToHaveTextAsync("3,000");
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
