using System.Text.RegularExpressions;
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
            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("前回の結果")).ToBeVisibleAsync();
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
            await page.GetByText("ディール開始").ClickAsync();
            // 額を選ぶ画面では Fold・Call・All-in を出さない
            await page.GetByRole(AriaRole.Button, new() { Name = "Raise…" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "All-in", Exact = true })).ToHaveCountAsync(0);
            // ポット 150 の 100% → 150 を新しく出す
            await page.GetByRole(AriaRole.Button, new() { Name = "100%" }).ClickAsync();
            await Assertions.Expect(page.Locator(".sizer-value")).ToHaveTextAsync("150");
            await page.GetByRole(AriaRole.Button, new() { Name = "Raise to 150", Exact = true }).ClickAsync();
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
            var stack = page.Locator(".dlg-stack-input input");
            // 数値として読めなければ直前の値に戻る
            await stack.FillAsync("abc");
            await stack.PressAsync("Tab");
            await Assertions.Expect(stack).ToHaveValueAsync("10,000");
            await stack.FillAsync("１，０００");
            await stack.PressAsync("Tab");
            await Assertions.Expect(stack).ToHaveValueAsync("1,000");
            await page.GetByRole(AriaRole.Button, new() { Name = "確定" }).ClickAsync();
            await Assertions.Expect(page.Locator(".player-stack").First).ToHaveTextAsync("1,000");

            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "All-in", Exact = true }).ClickAsync(); // P1
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P2
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P3
            await page.GetByRole(AriaRole.Button, new() { Name = "Bet…" }).ClickAsync();                 // フロップ P2
            await page.GetByRole(AriaRole.Button, new() { Name = "Bet 100", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();  // P3
            for (var i = 0; i < 4; i++) await page.GetByRole(AriaRole.Button, new() { Name = "Check", Exact = true }).ClickAsync();

            await Assertions.Expect(page.GetByText("Main pot")).ToBeVisibleAsync();
            // 勝者を選ぶ間はポットの帯を隠し、このあと決めるポットを出す
            await Assertions.Expect(page.Locator(".pot-bar")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator(".showdown-later")).ToBeVisibleAsync();
            await page.Locator(".winner-btn", new() { HasTextString = "P1" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".showdown-title")).ToContainTextAsync("Side pot");
            await Assertions.Expect(page.Locator(".showdown-later")).ToHaveCountAsync(0);
            await page.Locator(".winner-btn", new() { HasTextString = "P2" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("前回の結果")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".player-stack").First).ToHaveTextAsync("3,000");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task 席替えモードでドラッグして並べ替え_元に戻せる()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 4);
            await page.GetByRole(AriaRole.Button, new() { Name = "席替え" }).ClickAsync();

            // P4 のつまみを P1 の行の上の方まで引っぱる
            var handle = await page.Locator(".seat-handle").Nth(3).BoundingBoxAsync();
            var first = await page.Locator(".player-row").First.BoundingBoxAsync();
            await page.Mouse.MoveAsync(handle!.X + handle.Width / 2, handle.Y + handle.Height / 2);
            await page.Mouse.DownAsync();
            for (var i = 1; i <= 10; i++)
                await page.Mouse.MoveAsync(handle.X + handle.Width / 2, handle.Y + handle.Height / 2 + (first!.Y + 5 - handle.Y - handle.Height / 2) * i / 10);
            await page.Mouse.UpAsync();

            var names = page.Locator(".player-name");
            await Assertions.Expect(names.First).ToHaveTextAsync(new Regex(@"^P4(?!\d)"));
            await Assertions.Expect(names.Nth(1)).ToHaveTextAsync(new Regex(@"^P1(?!\d)"));

            await page.GetByRole(AriaRole.Button, new() { Name = "完了" }).ClickAsync();
            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(names.First).ToHaveTextAsync(new Regex(@"^P1(?!\d)"));
            await Assertions.Expect(names.Nth(3)).ToHaveTextAsync(new Regex(@"^P4(?!\d)"));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ハンドの途中は席替えに入れず_合間なら次のボタンが見える()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            await Assertions.Expect(page.Locator(".badge-next")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".player-row").First.Locator(".badge-next")).ToBeVisibleAsync();

            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "席替え" }).ClickAsync();
            await Assertions.Expect(page.GetByText("ハンドの合間のみ席替え可能")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".seat-handle")).ToHaveCountAsync(0);

            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Fold", Exact = true }).ClickAsync();
            // 次のボタンは P2
            await Assertions.Expect(page.Locator(".player-row").Nth(1).Locator(".badge-next")).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "席替え" }).ClickAsync();
            await Assertions.Expect(page.Locator(".seat-handle")).ToHaveCountAsync(3);

            // 席替え中に UNDO でハンドの途中へ戻ったら、席替えモードを抜ける
            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(page.Locator(".seat-handle")).ToHaveCountAsync(0);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task 二度押ししても次の人のアクションにはならない()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            await page.GetByText("ディール開始").ClickAsync();
            await Assertions.Expect(page.Locator(".input-lock")).ToHaveCountAsync(0);
            // P1 の Call を素早く 2 回押す。2 回目は P2 の同じ位置のボタンに当たるが、受け付けない
            var call = await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).BoundingBoxAsync();
            await page.Mouse.ClickAsync(call!.X + call.Width / 2, call.Y + call.Height / 2, new() { ClickCount = 2 });
            await Assertions.Expect(page.Locator(".pot-total")).ToHaveTextAsync("250");
            await Assertions.Expect(page.Locator(".action-name")).ToHaveTextAsync("P2");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task 次のハンドのDを選び_元に戻せる()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 3);
            var rows = page.Locator(".player-row");
            await Assertions.Expect(rows.First.Locator(".badge-next")).ToBeVisibleAsync();

            // 次D の人の画面にはボタンを出さない
            await rows.First.ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "次のハンドの D にする" })).ToHaveCountAsync(0);
            await page.GetByRole(AriaRole.Button, new() { Name = "キャンセル" }).ClickAsync();

            await rows.Nth(2).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "次のハンドの D にする" }).ClickAsync();
            await Assertions.Expect(rows.Nth(2).Locator(".badge-next")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".badge-next")).ToHaveCountAsync(1);

            await page.GetByText("ディール開始").ClickAsync();
            await Assertions.Expect(rows.Nth(2).Locator(".badge-d")).ToBeVisibleAsync();

            // ハンドの開始と D の選択を戻すと、次D は先頭の人に戻る
            await page.GetByLabel("元に戻す").ClickAsync();
            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(rows.First.Locator(".badge-next")).ToBeVisibleAsync();
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task 同じメンバーで始め直し_元に戻せる()
    {
        var (context, page) = await app.OpenAsync();
        try
        {
            await AddPlayers(page, 2);
            await page.GetByText("ディール開始").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "All-in", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Call", Exact = false }).ClickAsync();
            await page.Locator(".winner-btn", new() { HasTextString = "P1" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "確定", Exact = true }).ClickAsync();
            var stacks = page.Locator(".player-stack");
            await Assertions.Expect(stacks.Nth(1)).ToHaveTextAsync("0");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "参加者待ち（あと 1 人）" })).ToBeDisabledAsync();

            await page.GetByLabel("始め直す").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "同じメンバーで始め直す" }).ClickAsync();
            await Assertions.Expect(stacks.First).ToHaveTextAsync("10,000");
            await Assertions.Expect(stacks.Nth(1)).ToHaveTextAsync("10,000");
            await Assertions.Expect(page.GetByText("ディール開始")).ToBeVisibleAsync();

            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(stacks.Nth(1)).ToHaveTextAsync("0");

            await page.GetByLabel("始め直す").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "全部消して始める" }).ClickAsync();
            await Assertions.Expect(page.Locator(".player-row")).ToHaveCountAsync(0);
            await page.GetByLabel("元に戻す").ClickAsync();
            await Assertions.Expect(page.Locator(".player-row")).ToHaveCountAsync(2);
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
