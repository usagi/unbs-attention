# USAGI.NETWORK BeatSaber Attention

Beat Saber 向けの「⚠️注意」を表示するプラグインです。

## できること

- 曲選択中に「注意カテゴリ + 理由」を表示
- Google スプレッドシートから注意データを購読更新
- 注意カテゴリごとに表示 ON/OFF
- 注意カテゴリごとのプリフィックス付き表示
- 表示位置の X/Y 微調整（設定画面の POSITION タブ）

## 使い方

1. メインメニュー左側 MODS の `UNBS Attention` を開く
2. `SpreadsheetSources` に 購読したい Google Spreadsheets の URL を追加（デフォルト設定あり†1）
3. `今すぐ更新` を押して取り込み
4. `GENERAL` / `PREFIX` / `POSITION` タブで好みに調整

†1: デフォルト入りのスプレッドシートは本プラグインの作者のうさぎ博士ちゃんことDr.USAGIと本プラグイン制作のきかっけとなった配信者コミュニティーのフレンズである風野しあ、うさねくれあ、あきちゃらによって管理されているものです。本プラグインでは任意のスプレッドシートを購読できるので、必要に応じて自分で管理するスプレッドシートや他の誰かが管理するスプレッドシートを購読したり、デフォルトのものは削除して使うとよいです。

- <https://docs.google.com/spreadsheets/d/1HwMqwaHzlyidgGnIu5wTXosWzxgSI8dW0EO1yrgvAOg/edit?usp=sharing>

## ビルド

### VS Code で通常ビルド

1. .NET Framework 4.7.2 Targeting Pack を入れる
1. .NET SDK を入れる
1. 実行:

```powershell
dotnet restore
dotnet build
```

### BSIPA 有効ビルド

既定では BSIPA 参照を無効にして軽く開発できます。
BSIPA で動かす場合は `EnableBsipa=true` を付けます。

1. `IPA.Loader.dll` を `Libs/` に置く（または `BsipaLibDir` を指定）
1. 実行:

```powershell
dotnet build -p:EnableBsipa=true
```

カスタム参照パス例:

```powershell
dotnet build -p:EnableBsipa=true -p:BsipaLibDir="C:/path/to/Beat Saber_Data/Managed"
```

出力 DLL は `bin/Debug/net472/UnbsAttention.dll` です。Beat Saber の `Plugins` へ配置して使います。

## スプレッドシートのカラムと設定値

- `category`
- `bsr`
- `info_includes`
- `info_regex`
- `desc_includes`
- `desc_regex`
- `reason`

補足:

- `bsr`, `info_includes`, `desc_includes` は `;` `,` `|` 区切り対応
- `info_includes`, `desc_includes` は大文字小文字を区別せず照合

各カラムの設定値、設定例は前述のデフォルトのスプレッドシートの内容を参照してください。

## 現在の設定 UI

左カラムは 3 タブ構成です。

- `GENERAL`: 有効化、カテゴリ ON/OFF、手動更新
- `PREFIX`: カテゴリ接頭辞の編集
- `POSITION`: 表示位置の X/Y 微調整

右カラムは購読ソース一覧です（追加 / 削除 / 開く）。

## 色設定

`UserData/unbs-attention.settings.json` の以下キーで色を変更できます。

- `AttentionDisplayColorHex`
- `PlayButtonAttentionColorHex`
- `PlayButtonAttentionTextColorHex`

`#RRGGBB` と `#RRGGBBAA` の両方に対応しています。
例: `#FFE033CC`（末尾 `CC` が alpha）

## 主要フォルダ

- `src/Plugin.cs`: ランタイム本体
- `src/Config/PluginConfig.cs`: 設定モデル
- `src/Models/*`: データモデル
- `src/Services/*`: 同期 / 解析 / 外部連携
- `src/Presentation/*`: 表示・設定 UI
- `assets/template.csv`: シート用テンプレート

## License

- [MIT License](https://opensource.org/licenses/MIT)

## Author

- [USAGI.NETWORK](https://usagi.network)
