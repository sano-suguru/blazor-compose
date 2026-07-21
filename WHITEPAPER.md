# BlazorCompose: A Code-First Declarative UI Library for Blazor

**Whitepaper & Architectural Design**

対象プラットフォーム: .NET 10 (LTS) ベースライン / .NET 11 マルチターゲット

---

## 1. エグゼクティブサマリー (Executive Summary)

### 1.1 背景

現在、モバイルおよびデスクトップUI開発の主流は、SwiftUI、Jetpack Compose、Flutterに代表されるコードファーストの宣言的UI(Code-Driven Declarative UI)へとシフトしています。これらはHTML/XMLといった外部マークアップ言語を排除し、プログラミング言語自体の言語機能(型安全性、オートコンプリート、リファクタリング、ロジックのインライン記述)を最大限に活かしてUIを構築します。

一方、Microsoft Blazorは優れた宣言的フレームワークであるものの、基本的にはRazor構文(マークアップファースト)に依存しています。C#単体でUIを記述するAPI(`RenderTreeBuilder`)は低レイヤーかつフレームワーク内部向けに設計されており、人間が読み書きするには極めて冗長です。Microsoft自身も `RenderTreeBuilder` の手書きを推奨していません。これは主に、シーケンス番号の手動管理が差分検知(Diffing)の破綻を招きやすいためです。

### 1.2 本プロジェクトの提案

本ホワイトペーパーでは、Blazor上に直感的で型安全なコードファーストUI構築を導入するライブラリ「BlazorCompose」を提案します。

BlazorComposeの中核は、Razorコンパイラと同型のコンパイル戦略です。Razorコンパイラが `.razor` マークアップからC#のレンダリングメソッドを生成するのとまったく同じように、BlazorComposeのSource Generatorは、開発者がC#で記述した宣言的な `Body` 式からレンダリングメソッドを生成します。マークアップの代わりにC#の式を「ソース・オブ・トゥルース」とすることで、Razorが実証済みの静的シーケンス割当・差分検知性能をそのまま継承しながら、SwiftUIやJetpack Composeと同等の記述体験を実現します。

### 1.3 主要な技術的判断

UI定義(`Body`)は設計時のソース・オブ・トゥルースであり、実行時には評価されません。Source Generatorが `Body` の式ツリーを解析し、静的シーケンス番号を定数として埋め込んだレンダリングメソッドを部分クラスに生成します。これはRazorコンパイラが採る方式と同型であり、Blazorの差分検知が要求する「シーケンス番号のコンパイル時確定」を構造的に満たします(§5)。

この方式により、コードファーストUIで従来問題となる2つの型システム上の障害が同時に解消されます。第一に、C#には SwiftUI の `some View` に相当する不透明戻り値型が存在しないため、装飾チェーンの合成型(`Padded<VStack<...>>` 等)を統一的に返す手段がありませんが、実行時評価を行わない本方式では全APIが軽量なマーカー型 `View` を返すだけで済みます。第二に、実行時ツリー構築方式では避けられないヒープ割り当てが、本方式では原理的に発生しません(生成コードは `RenderTreeBuilder` へ直接命令を発行します)。

解析の適用範囲は明示的に仕様化します。静的解析可能な構文サブセット(SSC)の内側では完全な静的シーケンス割当を行い、外側(解析不能なヘルパー呼び出し等)は動的リージョンとして正確性を保ったまま縮退させます(§5.3)。

プラットフォーム戦略はLTS優先です。net10.0をベースラインとし、net11.0はオプトインのマルチターゲットとします(§3)。性能に関する数値はすべて予測値として明記し、第1フェーズのPoCベンチマークで実測・更新します(§7)。

---

## 2. コア・コンセプト (Core Concepts)

### 2.1 No-HTML / Pure C#

HTMLのタグ記述や生の文字列によるCSSクラス指定を廃止し、すべてをC#のメソッド、型安全な列挙型、構造体で表現します。IDEのインテリセンスが100%機能し、レイアウトやスタイルのエラーはビルド時に検知されます。

### 2.2 Seamless Blazor Integration

既存のBlazorエコシステムと完全に互換します。BlazorComposeで構築されたUIは標準の `RenderFragment` として公開できるため、既存の `.razor` コンポーネントの中からシームレスに呼び出すことができ、逆もまた可能です(§6)。

### 2.3 Razor-Equivalent Compilation

`Body` に記述された宣言的な式は、Source Generatorによってビルド時にレンダリングメソッドへコンパイルされます。生成コードはRazorコンパイラの出力と同じ形式(静的シーケンス番号付きの `RenderTreeBuilder` 命令列)であるため、Blazorエンジンから見ればBlazorComposeコンポーネントと通常のRazorコンポーネントは区別がつきません。開発者が書くコードと実行される命令列の間に、ランタイムの動的解釈や中間ツリーは存在しません。

> 設計上の要件: コンポーネントクラスは `partial` として宣言する必要があります(Source Generatorがレンダリングメソッドを同一クラスへ生成するため)。非partialクラスはビルドエラー(BC1001)となります。

---

## 3. 対応プラットフォーム戦略 (Target Platform Strategy)

| ターゲット | 位置付け           | 提供機能                                                                                                 |
| ---------- | ------------------ | -------------------------------------------------------------------------------------------------------- |
| net10.0    | ベースライン(必須) | コアエンジン全機能。LTS(3年サポート)であり企業ユーザーの採用障壁が低い                                   |
| net11.0    | オプトイン(推奨)   | C# 15のUnion型・`closed` 階層による閉世界 `ViewNode` 定義、Runtime Asyncによるイベントパイプライン軽量化 |

本ライブラリのコア技術(Source Generatorによる部分クラスへのメンバー生成)は成熟した標準機能であり、特定の最新言語機能に依存しません。net11.0(2026年11月GA予定、STS・24ヶ月サポート)では、C# 15のUnion型と `closed` 階層を用いてUIノードの集合を閉じた判別共用体として定義でき、ビジターの網羅性がコンパイル時に検証されます。該当APIは `#if NET11_0_OR_GREATER` で条件提供します。

> 注記: Union型は執筆時点(.NET 11 Preview 5)で一部機能が未実装のため、net11.0向けAPIの正式化はGA後とします(§9 ロードマップ参照)。

---

## 4. APIデザインと構文仕様 (API Design & Syntax)

### 4.1 基本コンポーネントの構造

開発者は `ComposeComponentBase` を継承した `partial` クラスで、`Body` プロパティをオーバーライドしてUI構造を定義します。SwiftUIの `var body: some View` に対応する記述体験です。

```csharp
using static BlazorCompose.UI;

public partial class CounterPage : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(spacing: 16,
            Text($"Count: {_count}")
                .FontSize(24)
                .Bold()
                .Foreground(Colors.Slate900),

            HStack(spacing: 8,
                Button("Increment", () => _count++)
                    .Style(ButtonStyle.Primary),
                Button("Reset", () => _count = 0)
                    .Style(ButtonStyle.Outline)
            )
        )
        .Padding(24)
        .Background(Colors.White)
        .CornerRadius(12);
}
```

- `View` はすべてのファクトリ・装飾メソッドが返す軽量なマーカー型(空の `readonly struct`)です。式は通常のC#として型検査されますが、実行時に評価されることはなく、Source Generatorが式ツリーを直接レンダリングコードへ変換します。
- `VStack(spacing: 16, ...)` の子要素は `params View[]` で受けるため、任意個の子を自然に記述できます。マーカー型方式のため、`params` 配列が実行時に確保されることもありません。
- 状態(`_count`)への参照や補間文字列、イベントラムダは、生成コードへ構文ごと移植されます(同一partialクラス内のため、privateメンバーへのアクセスも保たれます)。

レイアウトコンテナには `VStack` / `HStack` を採用します。Jetpack Compose流の `Column` / `Row` も検討しましたが、Blazorエコシステムでは `Column` がデータグリッド(QuickGrid、MudBlazor等)の列定義と強く結び付いており、混同を招くため、軸方向が一義に伝わるSwiftUI流を選択しています。

### 4.2 リストと条件分岐の表現

分岐とループは、専用コンビネータ `If` / `ForEach` で宣言的に記述します。

```csharp
public partial class TaskListPage : ComposeComponentBase
{
    private readonly List<TaskItem> _items = [];

    protected override View Body =>
        VStack(spacing: 12,
            Text("Tasks").FontSize(20).FontWeight(FontWeight.SemiBold),

            If(_items.Count == 0,
                then: () => Text("No tasks yet")
                                .Foreground(Colors.Gray500)
                                .Italic(),
                otherwise: () => ForEach(_items,
                    key: t => t.Id,
                    content: item =>
                        HStack(spacing: 8,
                            CheckBox(item.Done, v => item.Done = v),
                            Text(item.Title)
                                .StrikeThrough(item.Done)
                        )
                        .Padding(vertical: 4)
                )
            ),

            Button("Add Task", AddItem).Style(ButtonStyle.Primary)
        );

    private void AddItem() => _items.Add(new TaskItem("New task"));
}
```

- `If` はネイティブの `if` 文へ、`ForEach` は `foreach` + `SetKey` へと展開されます。分岐の各パスには互いに素な静的シーケンス空間が割り当てられ、状態の誤った引き継ぎを防ぎます(Yellow Paper §2.4)。
- `ForEach` の `key` セレクタは必須です。シーケンス番号が「テンプレート内の構文位置」を、キーが「データの同一性」をそれぞれ担うことで、並べ替え・挿入・削除時の状態保持が保証されます。
- `Body` 内でネイティブの制御構文(ブロック本体の `if` / `foreach` 等)を直接使うことも可能です。Source Generatorは該当構文を生成コードへそのまま移植し、動的リージョンで包みます(§5.3)。

### 4.3 コンポーネントの分割と再利用

UIの部分は `[Composable]` 属性を付与した静的メソッドに抽出できます。Jetpack Composeの `@Composable` に対応する概念で、Source Generatorはこれらを解析対象に含め、呼び出しサイトへ静的に展開します。

```csharp
protected override View Body =>
    VStack(
        Header("My Application"),   // [Composable] メソッド — 静的展開の対象
        BodyContent()
    );

[Composable]
private static View Header(string title) =>
    HStack(
        Icon(Icons.Menu),
        Text(title).FontSize(18)
    )
    .Padding(horizontal: 16, vertical: 12)
    .Background(Colors.Slate800)
    .Foreground(Colors.White);
```

`[Composable]` の付かないメソッドが `View` を返す場合、Source Generatorはその内部を解析できないため、当該メソッドは実行時に評価される動的コンテンツとして扱われます(戻り値の `View` に `RenderFragment` を内包させる形式。§5.3)。

---

## 5. アーキテクチャと内部実装 (Architecture & Technical Details)

### 5.1 コンパイルモデル: Bodyからレンダリングメソッドへ

Source Generatorは各コンポーネントの `Body`(および到達可能な `[Composable]` メソッド)の式ツリーを解析し、静的シーケンス番号を定数として埋め込んだレンダリングメソッドを同一partialクラスへ生成します。

§4.1の `CounterPage` から生成されるコードの概念形:

```csharp
// <auto-generated/> CounterPage.g.cs
public partial class CounterPage
{
    protected override void RenderBody(RenderTreeBuilder __b)
    {
        __b.OpenElement(0, "div");                                    // VStack + 装飾
        __b.AddAttribute(1, "class", Theme.Class(Cls.VStack, Cls.Gap16,
                                                 Cls.Pad24, Cls.BgWhite, Cls.Radius12));
        __b.OpenElement(2, "span");                                   // Text
        __b.AddAttribute(3, "class", Theme.Class(Cls.Fs24, Cls.Bold, Cls.FgSlate900));
        __b.AddContent(4, $"Count: {_count}");                        // 状態参照は構文ごと移植
        __b.CloseElement();
        __b.OpenElement(5, "div");                                    // HStack
        __b.AddAttribute(6, "class", Theme.Class(Cls.HStack, Cls.Gap8));
        __b.OpenElement(7, "button");
        __b.AddAttribute(8, "class", Theme.Class(Cls.BtnPrimary));
        __b.AddAttribute(9, "onclick",
            EventCallback.Factory.Create(this, () => _count++));      // ラムダも移植
        __b.AddContent(10, "Increment");
        __b.CloseElement();
        /* … Reset ボタン … */
        __b.CloseElement();
        __b.CloseElement();
    }
}
```

基底クラスとの接続は次の形をとります。

```csharp
public abstract class ComposeComponentBase : ComponentBase
{
    protected abstract View Body { get; }          // 設計時のソース・オブ・トゥルース
    protected abstract void RenderBody(RenderTreeBuilder builder);   // SGが実装を生成

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder)
        => RenderBody(builder);
}
```

`Body` は実行時に一度も呼び出されません。ファクトリ・装飾メソッドの実体はすべて `default(View)` を返す慣性(inert)実装であり、万一評価されても副作用はなく、AOTビルドではILトリマーにより除去されます。

### 5.2 シーケンス番号の静的確定

Blazorの差分検知は、シーケンス番号がコンパイル時に静的確定していることを前提とします。ランタイムでの動的インクリメントは、要素の挿入・削除時にDiffingアルゴリズムを誤認させ、サブツリーの不要な破棄・再生成とコンポーネント状態の消失を引き起こします。

本方式では、Source Generatorが式ツリーを深さ優先で走査し、各ノードに一意のシーケンス区間を割り当てて生成コードへ定数として埋め込むため、この前提は構造的に満たされます。Razorコンパイラがマークアップに対して行っていることを、C#式に対して行うだけです。割当アルゴリズムの形式定義はYellow Paper §2を参照してください。

### 5.3 静的解析可能サブセット(SSC)と動的リージョン

任意のC#コードに対して静的シーケンス割当は成立しないため、解析の適用範囲を明示的に定義します。

`Body` および `[Composable]` メソッド内のファクトリ/装飾/コンビネータの直接呼び出し(インラインラムダを含む)がSSCの内側であり、完全な静的割当の対象です。SSCの外側は次の2通りに扱われます。

移植可能な構文(ネイティブ `if` / `foreach` / `switch` 等)は、生成コードへそのまま移植された上で、境界に静的シーケンスを持つリージョン(`OpenRegion` / `CloseRegion`)で包まれます。リージョンはシーケンス空間を分離するため、内部の動的性が外部のDiffingへ波及することはありません。

解析不能な呼び出し(`[Composable]` の付かない `View` 返却メソッド等)は、実行時に評価され、戻り値の `View` に内包された `RenderFragment` がリージョン内で描画されます。この経路のみ通常のヒープ割り当てが発生します。

いずれの場合も正確性は保たれ、失われるのは該当領域の静的最適化のみです。アナライザーは情報診断BC2001で最適化機会の喪失を通知します。

アナライザーは `Body` / `[Composable]` 本体での状態変更(インスタンスフィールドへの代入、インクリメント等の直接書き込み)をエラー診断BC3001で検出します。`Body` は純粋な状態→UIの射影でなければならず、状態遷移はイベントハンドラに委ねる必要があります。なお、`Button` のonClickラムダ(遅延イベントハンドラ)はレンダリング後に実行されるため除外されます。メソッド呼び出し経由の副作用など任意の解析不能パスの完全な検出は保証しません。

### 5.4 Hot Reload戦略

本アーキテクチャは.NET Hot Reloadと構造的に相性が良い位置にあります。`Body` 式の編集はSource Generatorが再生成する `RenderBody` のメソッド本体の変更として現れますが、メソッド本体の差し替えは.NET Hot Reload(Edit and Continue)が最も安定してサポートする編集クラスです。`[Composable]` メソッドの追加も「既存型へのメンバー追加」であり、サポート範囲内です。さらにBlazorには既にRazor用の `MetadataUpdateHandler` によるコード更新後の再レンダリング経路が存在し、BlazorComposeのコンポーネントは通常の `ComponentBase` 派生+通常の生成メソッドであるため、この既存経路にそのまま乗ります。独自のリロード機構は必要ありません。

挙動は次のように仕様化します。要素を `Body` の途中へ挿入する編集では後続ノードのシーケンス番号が再割当されるため、リロード直後の初回レンダリングで当該コンポーネントのDOMサブツリーが再構築されます(コンポーネントのフィールド状態は保持され、入力中のフォーカス等のDOMローカル状態は失われえます)。これはRazorファイル編集時とまったく同じ意味論です。

残る不確実性は、編集セッション中にサードパーティSource Generatorが確実に再実行されるかというツーリング挙動で、Visual Studio / `dotnet watch` / Riderで差異があった歴史があります。このため第1フェーズの受け入れ条件に3環境でのHot Reload実測を含めます(§9)。万一特定環境で不安定な場合の開発時フォールバック(DEBUGビルド限定の解釈モード)はYellow Paper付録Cに代替案として記載しています。

---

## 6. 既存Blazorエコシステムとの「双方向」互換性

BlazorComposeは独自のシェルターを構築するのではなく、既存のRazorコンポーネント(`.razor`)やライブラリ(MudBlazor、QuickGrid等)をそのまま再利用できます。

### 6.1 Razorの中でBlazorComposeを使う

Source Generatorは各 `[Composable]` メソッドに対し、`RenderFragment` を返す兄弟メソッド(`〜AsFragment`)を併生成します。これにより既存の `.razor` ファイルへコードファーストUIを直接埋め込めます。

```razor
@* ExistingPage.razor *@
<div class="legacy-layout">
    @Widgets.StatusBadgeAsFragment(currentStatus)
</div>
```

```csharp
public static partial class Widgets
{
    [Composable]
    public static View StatusBadge(Status status) =>
        HStack(spacing: 4,
            Icon(status.IsHealthy ? Icons.Check : Icons.Alert),
            Text(status.Label).FontSize(12)
        )
        .Padding(horizontal: 8, vertical: 2)
        .CornerRadius(999);
}
```

### 6.2 BlazorComposeの中で既存のRazorコンポーネントを使う

`Component<T>()` ファクトリで、サードパーティ製を含む任意のBlazorコンポーネントをコードファーストツリーへ組み込めます。パラメータはSource Generatorが生成する静的セッターでバインドされるため(式木のランタイムコンパイルなし)、AOT環境でも安全です。

```csharp
protected override View Body =>
    VStack(
        Text("Data Grid").FontSize(20),
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)
    );
```

---

## 7. パフォーマンス特性と予測 (Performance Projections)

本章の数値はすべて設計に基づく予測値であり、第1フェーズのPoCベンチマークで実測・更新されます。

### 7.1 レンダリングコストとGCアロケーション

生成コードはRazorコンパイラの出力と同形式であるため、SSC内側のレンダリングコスト・アロケーション特性は等価なRazorコンポーネントと同等(予測値)です。実行時の中間ツリーやビルダーオブジェクトは存在しないため、コードファースト方式に一般的に伴う追加のGC負荷はゼロです。追加コストが生じるのは動的コンテンツ経路(§5.3)のみで、これは `RenderFragment` を手書きした場合と同等です。

### 7.2 差分検知性能

静的シーケンス割当により、Diffing計算量は理論上の最小値 O(|r_t| + |r_{t+1}|) を維持します(Yellow Paper §1.2)。第1フェーズでは、動的インクリメント方式との比較(要素挿入・削除・並べ替えシナリオでの状態保持とパッチサイズ)を公開します。

### 7.3 Wasmバイナリサイズ

パラメータバインディングを含む全機構がリフレクション・フリー(`System.Reflection` / `System.Linq.Expressions` へのランタイム依存ゼロ)であるため、ILトリマーが未使用コードをアグレッシブに削除できます。リフレクションベースのバインディングを持つ同等ライブラリ比で、AOTコンパイル後のWasmペイロードを約20〜30%削減(予測値)と見込みます。素のRazor構成との比較ではほぼ同等です。

---

## 8. 関連プロジェクトとの比較 (Related Work)

C#によるコードファーストUIの試みは本ライブラリが最初ではありません。ただし、対象プラットフォームが異なるため直接の競合ではなく、本章は設計アプローチの対比です。

|                  | BlazorCompose           | Comet                                           | Avalonia.Markup.Declarative               | CommunityToolkit.Maui.Markup     | 手書き RenderTreeBuilder   |
| ---------------- | ----------------------- | ----------------------------------------------- | ----------------------------------------- | -------------------------------- | -------------------------- |
| レンダリング先   | 実DOM(Blazor)           | ネイティブ(MAUIハンドラ)                        | ネイティブ+ブラウザ(Skiaによるcanvas描画) | ネイティブ(MAUI)。ブラウザ非対応 | 実DOM                      |
| プロジェクト状態 | 本提案                  | 2025年7月アーカイブ(概念実証・公式サポートなし) | 活発                                      | 活発                             | Blazor標準(手書きは非推奨) |
| UIモデル         | 宣言的(再評価+差分検知) | 宣言的(実行時評価+リフレクションバインド)       | retained-mode構築の糖衣                   | retained-mode構築の糖衣          | 宣言的(全手動)             |
| シーケンス番号   | コンパイル時確定        | 対象外(Blazor外)                                | 対象外                                    | 対象外                           | 手動管理(破綻しやすい)     |
| 状態の記述       | 素のC#フィールド        | `State<T>` ラッパー+反応ラムダの使い分け        | ViewModel / `StateHasChanged`             | バインディング式                 | 素のフィールド             |
| 実行時中間表現   | なし(SSC経路)           | UIツリー+リフレクション                         | コントロール実体を保持                    | コントロール実体を保持           | なし                       |
| AOT/トリミング   | 完全適合                | リフレクション依存                              | 適合                                      | 適合                             | 適合                       |

この対比から、本ライブラリの差別化点は次の3点に整理されます。

第一に、DOMネイティブであること。Avaloniaはブラウザ上でも動作しますが、それはSkiaによるcanvasへの描画であり、DOMを持ちません。SEO、アクセシビリティツリー、CSSエコシステム、SSR/プリレンダリングはcanvas描画には適用できません。BlazorComposeは実DOM/HTMLへ宣言的UIを射影するコードファーストC#であり、Webの資産がすべて生きます。

第二に、Blazor差分検知との構造的整合。retained-mode系(Avalonia.Markup.Declarative、MAUI.Markup)はコントロール実体を保持・変異させるため、シーケンス番号問題自体を持ちません。一方、Blazor上でコードファーストを試みる場合この問題は不可避であり、本ライブラリはこれをRazorコンパイラと同型の方式で解決した点が核心的貢献です。手書き `RenderTreeBuilder` や実行時ツリー方式では、この問題が正しさと性能の両面で破綻要因となります。

第三に、宣言的セマンティクスとゼロ中間表現の両立。毎レンダリングでUI全体を再評価する宣言的な書き味は、実行時ツリー構築方式(Cometがこの型)ではGCプレッシャーという恒常的コストを伴いました。本ライブラリは同じ書き味を、実行時の中間オブジェクトを一切生成せずに提供します。宣言的UIの生産性とretained-mode並みのランタイムコストの両取りが、コンパイル時生成方式の本質的な利点です。

---

## 9. ロードマップ (Roadmap)

### 第1フェーズ: コアAPIとPoC(2026年Q3)

基本レイアウト(`VStack`, `HStack`, `Grid`)と `Text`, `Button` を実装し、Source Generatorによる `Body` 解析→レンダリングメソッド生成パイプラインを実証します。検証ベンチマークとして、動的インクリメント方式とのDiffing挙動・状態保持比較、および素のRazorとのアロケーション比較を実測し、§7の予測値を実測値に置換します。受け入れ条件には、Visual Studio / `dotnet watch` / Riderの3環境におけるHot Reload動作の実測(§5.4)を含めます。

### 第2フェーズ: 解析範囲の拡張と .NET 11 対応(2026年Q4)

`[Composable]` 解析の拡張(ジェネリックヘルパー、ローカル関数対応)と動的リージョンの安定化を行います。.NET 11 GA(2026年11月)後、Union型・`closed` 階層ベースの `ViewNode` APIをnet11.0ターゲットで正式化します。

### 第3フェーズ: デザイナー協調エコシステム(2027年)

Figmaデザインから BlazorCompose C#コードへの自動コードジェネレーター、および各種CSSフレームワーク(Tailwind CSS, Bootstrap)向け型安全テーマアダプターを構築します。

---

## 10. 結論 (Conclusion)

BlazorComposeは、C#の洗練された言語仕様、強力なIDEサポート、コンパイル時静的検証という利点をBlazor UI開発に最大限もたらします。

その実現手段は奇をてらったものではありません。Razorコンパイラが10年にわたり実証してきた「マークアップからのコード生成」を、「C#式からのコード生成」に置き換えただけです。だからこそ、Blazorエンジンとの互換性・性能特性はRazorと同等であることが構造的に保証され、開発者はSwiftUIやJetpack Compose、Flutterで実証されたコードファースト開発の生産性を、既存のBlazorエコシステムを失うことなく手にできます。