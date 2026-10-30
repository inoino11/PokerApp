# CLAUDE.md

`PokerApp`（表示名「シマリス - ポーカーチップ管理 -」、英字では CHIPmunk）は、テキサスホールデムのチップのやり取りを 1 台の端末でデジタル化する Blazor WebAssembly 製のクライアント完結型 PWA。サーバーを持たず、データは端末内（localStorage / IndexedDB）に保存し、GitHub Pages から独自ドメインの `https://chipmunk.inoino11.com/` で静的配信する。

## 仕様

- 仕様は [docs/spec.md](docs/spec.md) にまとめてある。仕様を変えたら、そちらも直す。
- 名前は `Services/AppInfo.cs` の定数（上の帯とタブ）、`wwwroot/index.html`（`<title>`・起動画面・OGP）、`wwwroot/manifest.json` の `name` / `short_name`（ホーム画面に入れたときの名前「ポーカー シマリス」）にある。変えるときはそろえる。
- アイコンの元の絵は `docs/icon/`、共有用の画像は `wwwroot/ogp-image.jpg`。

## 技術スタック

| 用途 | 採用 |
|---|---|
| フレームワーク | .NET 10 / Blazor WebAssembly（`Microsoft.NET.Sdk.BlazorWebAssembly`） |
| UI | MudBlazor（テーマ・ダイアログ・Snackbar・Chart もこれで賄う） |
| 軽量な永続化 | Blazored.LocalStorage（設定値・入力途中の状態） |
| 大量データの永続化 | IndexedDB（`wwwroot/js/db.js` に JS で実装し、JS interop で呼ぶ） |
| 画像生成など | 必要なときだけ CDN の JS ライブラリ（例: html2canvas）を `index.html` で読み込む |
| 書体 | 自前で配る（`wwwroot/fonts/`。Google Fonts など外部の書体配信は使わない） |
| 外部API | 必要最小限。サーバー側処理が要るときは Cloudflare Workers を使う |
| テスト | xUnit（計算・集計のロジック）、Playwright .NET（公開物の画面テスト） |

- csproj では `Nullable` と `ImplicitUsings` と `TreatWarningsAsErrors` を有効にする。
- NuGet パッケージは安易に増やさない。MudBlazor に同じ機能があればそちらを使う。

## コマンド

```sh
dotnet watch run            # 開発サーバー（http://localhost:5xxx）
dotnet build                # ビルド確認。変更後は必ずここまで通す
dotnet test tests/PokerApp.Tests      # ロジックのテスト
dotnet publish PokerApp.csproj -c Release -o release   # 本番ビルド（CI と同じ。slnx があるので csproj を指定する）
E2E_WWWROOT="$PWD/release/wwwroot" E2E_BROWSER_CHANNEL=msedge dotnet test tests/PokerApp.E2E   # 公開物の画面テスト
```

- この PC（Windows）で Node が要るときは、VS Code 同梱のものを `ELECTRON_RUN_AS_NODE=1 "$LOCALAPPDATA/Programs/Microsoft VS Code/Code.exe"` で使う。
- `index.html` などを PowerShell の `Set-Content` で書き換えない（文字コードが壊れる）。書き換えは Edit か bash の sed / Python で行う。

## フォルダ構成

```
PokerApp/
├─ Program.cs          # DI 登録・MudBlazor 設定のみ
├─ App.razor           # Router
├─ _Imports.razor      # 全コンポーネント共通の @using
├─ StorageKeys.cs      # localStorage のキー定数（1か所に集約）
├─ Layout/             # MainLayout / NavMenu（+ .razor.css）
├─ Pages/              # @page を持つ画面コンポーネント
├─ Components/         # @page を持たない部品・ダイアログ
├─ Services/           # 状態管理・永続化・計算ロジック
├─ Models/             # POCO・enum・純粋なヘルパー
├─ wwwroot/
│  ├─ index.html       # ローディング画面・起動処理・PWA 登録
│  ├─ js/              # JS interop（機能ごとにファイルを分ける）
│  ├─ css/app.css      # 全体スタイル
│  ├─ fonts/           # 自前で配る書体（ライセンス文書も同梱）
│  ├─ manifest.json
│  ├─ service-worker.js / service-worker.published.js
│  └─ ogp-image.jpg, icon-*.png
├─ tests/              # PokerApp.Tests（xUnit）/ PokerApp.E2E（Playwright）
├─ cloudflare/         # Worker を使う場合のみ（Worker 本体・wrangler.toml・テスト）
└─ .github/workflows/  # deploy.yml（と deploy-worker.yml）
```

## 命名規則

- 名前空間はフォルダに合わせる（`PokerApp.Services`, `PokerApp.Models`）。file-scoped namespace（`namespace X;`）を使う。
- クラス・メソッド・プロパティは PascalCase。非同期メソッドは `〜Async` で終える。
- private フィールドは `_camelCase`、ローカル変数・引数は camelCase。
- コンポーネント名は役割で終える: 画面は名詞（`Dashboard`, `Settings`）、ダイアログは `〜Dialog`、部品は `〜Table` / `〜ButtonGroup` / `〜Keypad` など。
- 子コンポーネントのイベントは `EventCallback` で `On〜`（`OnDeleteRequested`, `OnKeyPress`）。双方向バインドするものは `Xxx` と `XxxChanged` の組にする。
- JS 側のグローバル関数は機能ごとに1オブジェクトにまとめる（`window.appDb.init`, `window.pwaInstall.prompt`）。グローバルに関数をばらまかない。
- コメント・UI 文言は日本語でよい。

## コメント
- 文体は常体（「〜する」）か体言止め。
- 「何をしているか」ではなく「なぜそうするか」「守るべき前提」を書く。コードを読めば分かる言い換えは書かない。
- 変更履歴（「【修正】」「変更点:」「以前の〜」）やコメントアウトしたコードは残さない（履歴は git に任せる）。
- 書き方:
  - Services / Models のクラスと、名前だけでは動作が分からない公開メソッド → `/// <summary>`（3行形式）
  - それ以外の C#（`@code` 内を含む）・JS → `//`、CSS → `/* */`、Razor のマークアップ → `@* *@`
  - 長いファイルの区切り → `// --- 見出し ---`（マークアップでは `@* --- 見出し --- *@`）
  - 短い補足は行末コメントでよい（単位・値の意味など）。

## コンポーネントの分け方

- **Page**（`Pages/`）: ルーティング・Service の呼び出し・画面全体の状態を持つ。
- **部品**（`Components/`）: `[Parameter]` で値を受け取り、`EventCallback` で親に返すだけ。Service を直接 inject しない（表示専用にする）。
- **ダイアログ**: MudBlazor の `IDialogService.ShowAsync<T>()` で開き、`[CascadingParameter] IMudDialogInstance MudDialog` で結果を返す。呼び出し側は `result.Canceled` と `result.Data is T` をチェックする。
- 1 ファイルが 500 行を超えたら、部品の切り出しかロジックの Service への移動を検討する。
- スタイルはコンポーネント専用なら `Xxx.razor.css`（CSS isolation）、全体共通なら `app.css`。MudBlazor の内部要素を上書きする場合のみ `::deep` を使う。
- `app.css` は MudBlazor の CSS より先に読み込まれるので、MudBlazor の指定を上書きするときは親のクラスを付けて一段強くする（例: `.mud-navmenu .mud-nav-link`）。
- 計算・集計ロジックは `.razor` に書かず Service に置く。

## DI と状態管理

- Service はすべて `Program.cs` で `AddScoped` 登録する（WASM では Scoped ≒ アプリ全体で1つ）。
- Service の役割を分ける:
  - **`〜StateService`**: 画面をまたいで保持する状態。プロパティは `{ get; private set; }` で公開し、変更は必ず `SaveXxxAsync` 等のメソッド経由にして、メソッド内でメモリ更新と localStorage 保存を同時に行う。起動時に `InitializeStateAsync()` でまとめて復元する。
  - **`DatabaseService`**: IndexedDB への読み書きの窓口。JS interop 呼び出しはここに閉じ込め、Page から `IJSRuntime` で DB 関数を直接呼ばない。初回呼び出し時に `EnsureInitializedAsync()` で DB を開く。
  - **`〜CacheService`**: 重い集計結果をメモリに保持する。`SemaphoreSlim` で二重初期化を防ぎ、`InitializeAsync(forceReload: true)` で再計算する。起動直後に `_ = Cache.InitializeAsync();` で裏で温めておく。
  - **`〜CalculatorService`**: 状態を持たない純粋な計算。
- アプリ全体の表示設定（単位・テーマ等）は `MainLayout` で読み込み、`<CascadingValue>` で配る。受け側は `[CascadingParameter]`。
- localStorage のキーは `StorageKeys` の定数を使い、文字列リテラルを直接書かない。リリース後はキーの値を変えない（利用者の端末に保存済みのデータが読めなくなる）。
- 保存データの型を変えるときは、古い形式を読めたら変換する救済処理を入れる（読めなければ初期値で継続し、例外で起動を止めない）。

## JS interop

- JS は `wwwroot/js/*.js` に置き、`index.html` で `<script src>` 読み込みする。`index.html` に直接書くのは起動・ローディング・PWA 登録まわりだけにする。
- C# → JS は `InvokeAsync<T>` / `InvokeVoidAsync`。JS から結果を返すときは `"SHARED"` / `"COPIED"` / `"ERROR: ..."` のような状態文字列を返し、C# 側で Snackbar 表示を分岐する。
- JS → C# のコールバックは `DotNetObjectReference` + `[JSInvokable]`。コンポーネントの `Dispose` で参照を解放する。
- IndexedDB のスキーマ変更は `indexedDB.open(name, version)` の version を上げ、`onupgradeneeded` 内で `objectStoreNames.contains` を確認してから作る。複数ストアを更新するときは1トランザクションで行う。

## PWA・起動

- `index.html` に全画面ローディングを置き、`MainLayout.OnAfterRenderAsync(firstRender)` から JS を呼んでフェードアウトする。
- `Blazor.start()` は `autostart="false"` で手動起動し、失敗時は「Service Worker 解除 + Cache Storage 全削除 + リロード」の復旧ボタンを出す。
- Service Worker は更新を検知したら `confirm` で確認し、`SKIP_WAITING` → `controllerchange` でリロードする。
- Service Worker で、アプリ以外の静的ページ（`tools/` など）へのナビゲーションを `index.html` に置き換えない。書体ファイルはプリキャッシュから外し、使われたときに実行時キャッシュへ入れる。
- バージョン表記は csproj の `InformationalVersion` にビルド日時（JST, `vyy.MM.dd.HHmm`）を埋め込む。
- 画面内の例外は `MainLayout` の `ErrorBoundary` で受け、ヘッダーとメニューは残して「トップに戻る」「再読み込み」を出す。

## 公開先とドメイン

- アプリは `https://chipmunk.inoino11.com/` のルートで配信する。`<base href="/" />` に固定し、ローカルと本番で切り替えない。CI で書き換えない。
- パスはすべて相対パスで書く（先頭 `/` を付けない）。
- OGP の `og:url` / `og:image` は `https://chipmunk.inoino11.com/` からの絶対 URL で書く。
- ブラウザはサイトの住所（オリジン）ごとにデータを分けて保存する。公開後に住所を変えると、利用者が保存したプリセットが引き継がれない（バックアップの仕組みはないので、移す手段もない）。住所は変えない。
- 共有するのが前提の URL では、鍵などの秘密は `#` 以降に置く（サーバーやログに送られない）。

## Cloudflare Workers（サーバー側の処理が要る場合のみ）

- Worker の住所は `chipmunk-api.inoino11.com` にする（`api.inoino11.com` は TaGAMΣ の Worker が使っている）。`wrangler.toml` に `routes = [{ pattern = "chipmunk-api.inoino11.com", custom_domain = true }]` を書けば、DNS と証明書は Cloudflare が用意する。
- 設定は `wrangler.toml` に集め、ダッシュボードで直接変えない（次のデプロイで上書きされる）。
- デプロイは `cloudflare/**` を変えた push で GitHub Actions（`cloudflare/wrangler-action`）が行う。Worker のテストが通ったときだけデプロイする。
- 無料枠（KV の書き込みは 1 日 1,000 回、UTC 0 時 = 日本時間 9 時に戻る）を意識し、書き込み回数を抑える作りにする（送信の間引き、最短間隔）。上限に達したら Worker は 429 を返し、アプリは「日本時間 9 時に回復します」と表示する。
- 利用者のデータは端末で暗号化してから送り、サーバーでは中身を読めないようにする。KV には有効期限（TTL）を付ける。

## 時刻

- 画面に出す時刻は端末の時計（日本の端末なら日本時間）に従う。
- サーバーや運用の仕組み（Worker の保存時刻、GitHub Actions、Cloudflare の集計）は UTC で動く。保存は UTC のまま（並び順や数え方が UTC 前提のため）にし、人に見せるときは日本時間に直して「（日本時間）」と添える。
- GitHub Actions の `cron` は UTC で書き、コメントに日本時間を併記する。

## パスキー

- パスキー（WebAuthn）を使うときは、`inoino11.com` 配下で開いていれば `rp.id` / `rpId` に `"inoino11.com"` を指定する。アプリを別のサブドメインに移してもパスキーがそのまま使える。
- localhost などでは指定しない（開いている住所に結び付く）。

## 問い合わせ・エラー報告（TaGAMΣ の仕組みを使い回す）

- 問い合わせフォームとエラー報告は、運営者の公開鍵で端末内で暗号化してから Worker に送る（ECDH P-256 → HKDF → AES-GCM）。秘密鍵は運営者の手元にだけ置き、リポジトリ・Cloudflare・GitHub には置かない。
- エラー報告は自動では送らない。送る内容をそのまま見せ、利用者が「送信する」を押したときだけ送る。送る文から `?` と `#` 以降は取り除く。
- 読むためのページ（`tools/feedback-reader.html`）は CSP で通信を禁じ、復号はブラウザの中だけで行う。

## 表示と文言

- 金銭や賭けに関わりうるツールでは、「金銭の賭けを推奨するものではありません」などの注記を `Services/Notices.cs` の定数にまとめ、関係する画面に出す。
- データの扱い・規約・問い合わせは 1 ページ（例: 「データと規約」）にまとめ、改定日を書く。外部サービスを足したら、そのページも更新する。
- 左メニューの項目名は 1 行に収まる長さ（メニュー幅 160px で 6 文字程度）にする。
- 短い表示（FAQ の質問、チェックボックスの文言など）が単語の途中で折り返すときは、`Phrases` 部品（`|` で折り返してよい切れ目を示し、`.phrase { display: inline-block }` で包む）を使う。説明文の改行までは気にしない。

## ビルド・デプロイ

- `main` への push で GitHub Actions（`.github/workflows/deploy.yml`）が GitHub Pages にデプロイする。手動デプロイはしない。
- ワークフローの流れ: `setup-dotnet` → ロジックのテスト → `dotnet publish PokerApp.csproj -c Release -o release` → 公開物の画面テスト（Playwright の Chromium）→ `.nojekyll` 作成と `index.html` を `404.html` にコピー（SPA の直リンク対策）→ `upload-pages-artifact` → `deploy-pages`。テストが落ちたら公開しない。
- 画面テストが失敗したら、操作の動画を成果物として上げる（`E2E_ARTIFACTS`）。
- 運用の見張り（KV の書き込み回数や問い合わせの新着）が要るなら、GitHub Actions の定期実行で調べ、知らせることがあるときだけ「運用通知」の Issue に書く（GitHub からメールが届く）。

## 作業の進め方

- push はユーザーの許可を得てから行う。Worker がデプロイされる push は、そのことを明記して許可を求める。
- コミットは話題ごとに分ける。
- 秘密の情報（秘密鍵、API トークン、復元キーなど）を Claude が扱わない。必要なときは、ユーザーが自分の端末や画面で入力・保存する手順を案内する。
- ユーザーが手を入れたファイルや、未コミットの変更には、許可なく触れない。
- 経過と結果の報告・質問・提案は、必ず日本語で書く（ユーザーは英語を読むのが得意ではない）。ツール呼び出しの合間の短い報告や、ツールの説明文も日本語にする。会話が長くなっても英語に切り替えない。
- 許可を求めるときは、コマンドを示して「実行してよいか」と訊くのではなく、「こういう仕様にしてよいか」「こういう変更をしてよいか」と、何が変わるかで訊く。

## コミット

- コミットメッセージは日本語で、何を変えたかを1行で書く（例: 「〜機能初期実装」「〜の問題を修正」「〜のレイアウト調整」）。必要なら空行のあとに理由を書く。
- `bin/` `obj/` `release/` や作業用ファイルはコミットしない（`.gitignore` に追加する）。

## 作業時の注意

- 変更後は `dotnet build` が警告なく通ることを確認する。
- UI の変更はスマホ幅（320px・390px）とダークモード／ライトモードの両方で崩れないか確認する。必要なら一時的な Playwright のテストでスクリーンショットを撮って確かめ、そのテストはコミットしない。
- バックアップ（エクスポート/インポート）は持たない方針。端末に保存するのは、一戦の途中のリロードに備えた進行中のゲームと、プリセットだけ。
- 保存形式（`SavedGame` と操作の判別子、プリセット）を変えるときは、古い形式を読めなくても起動を止めない（進行中のゲームは失われてよいが、プリセットはなるべく救う）。
