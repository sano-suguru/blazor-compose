# BlazorCompose Technical Yellow Paper

**Formal Specification, Compiling Algorithms, and Memory Layout Analysis**

前提環境: .NET 10(ベースライン)、.NET 11(条件付き機能)

---

## 0. 表記と前提 (Notation & Prerequisites)

集合・写像は素朴集合論の記法を用いる。`φ ∘ ψ` は合成、`≺` は狭義半順序。

本仕様が依存する言語・ランタイム機能:

| 機能                                             | 要件                               | 用途                             |
| ------------------------------------------------ | ---------------------------------- | -------------------------------- |
| Source Generatorによる部分クラスへのメンバー生成 | 全対応バージョン(成熟した標準機能) | `RenderBody` の生成(§2)          |
| ILトリミング / Native AOT                        | .NET 10                            | 慣性API・未使用コードの除去(§5)  |
| Union型 / `closed` 階層                          | C# 15 / .NET 11(条件付き)          | `ViewNode` の閉世界定義(§6)      |
| Runtime Async                                    | .NET 11(条件付き)                  | イベントパイプライン軽量化(§4.3) |

コア機構が特定の最新言語機能に依存しない点は本設計の意図的な性質である。検討の末に不採用とした代替アーキテクチャ(Interceptor方式、ランタイムref structツリー方式)とその理由は付録Bに記す。

---

## 1. 抽象数理モデルと形式定義 (Mathematical Model)

### 1.1 状態と射影 (State and Projection)

コンポーネントの状態空間を `S`、離散時刻 `t ∈ ℕ` における状態を `s_t ∈ S` とする。宣言的UI式の集合(設計時の構文的実体)を `E`、Blazor内部のレンダリングツリー(フレーム列)の集合を `R` と定義する。

BlazorComposeのコンパイルと実行は次の2つの写像で特徴付けられる:

```
γ : E → (S → R)     — Source Generatorによるコンパイル(ビルド時)
r_t = γ(e)(s_t)      — 生成されたレンダリングメソッドの実行(実行時)
```

Razorとの対比で言えば、Razorコンパイラは `E` をマークアップとして受け取り、BlazorComposeは `E` をC#式として受け取る。`γ` の像である `S → R` が実行時の全てであり、実行時に `E` が評価されることはない。

生成された写像 `γ(e)` は純粋関数であることを規約とする(単一方向データフロー、§4.1)。`Body` 内の状態変更は診断BC3001の対象となる。BC3001の初期検出範囲はコンポーネントのインスタンスメンバーへの静的識別可能な直接書き込み(フィールド代入、プロパティ代入、インクリメント/デクリメント演算子)に限る。`Button` のonClickラムダ(`DeferredEventHandler`として分類)内の変更はレンダリング後に実行されるため除外される。任意のメソッド呼び出し経由の副作用(非同期連鎖等)の完全な検出は初期スライスでは保証しない。

### 1.2 レンダリングツリーの等価性と差分検知 (Reconciliation Equivalence)

`R` の各フレーム `n ∈ r_t` はシーケンス番号 `seq(n) ∈ ℕ` を持つ。Blazorの差分演算子を

```
Δ : R × R → Patch
```

とし、`Δ(r_t, r_{t+1})` がDOMへ適用される。Blazorの差分アルゴリズムは、両ツリーを先頭から同時走査し、シーケンス番号の一致・大小比較のみでフレームの同一性(保持/挿入/削除)を判定する。

**定理1(シーケンス安定性条件)**
`Δ` が最小コスト O(|r_t| + |r_{t+1}|) で、かつ意味的に同一のノードの状態を保存するためには、任意の意味的同一ノード対 `(n, n′)`(`n ∈ r_t`, `n′ ∈ r_{t+1}`)について次が成立しなければならない:

```
seq(n) = seq(n′)                                   … (1)
```

**系1**: 条件(1)を満たす十分条件は、`seq` が実行時の生成順序ではなくソースコード上の構文位置の関数であることである。フレームを生成した式ノードの構文位置を `π(n)` としたとき、ある単射 `σ` が存在して:

```
seq(n) = σ(π(n)),   σ : Π → ℕ は単射             … (2)
```

本方式では `σ` はビルド時にSource Generatorが構成し、生成コードへリテラル定数として埋め込まれるため、条件(2)は構造的に満たされる。対照的に、ランタイムインクリメント方式(`seq(n) = 生成順序`)は、条件付きレンダリングや要素挿入により `π` と生成順序の対応が崩れた時点で条件(1)に違反し、一致すべきフレーム以降のサブツリー全体が「削除+新規挿入」と誤判定される(計算量が最悪 O(n) のサブツリー再構築へ劣化)とともに、再構築されたコンポーネントの内部状態(入力中のテキスト等)が消失する。

---

## 2. コンパイルアルゴリズム (Compilation Algorithm)

### 2.1 全体パイプライン

```
[ユーザーコード]                     [Source Generator]
partial class C :                    ① partial検証・Body発見
ComposeComponentBase                 ② SSC分類(§2.3)
  View Body => …        ──AST──▶    ③ DFS順シーケンス割当(§2.2)
  [Composable] View F() => …         ④ RenderBody(RenderTreeBuilder) の生成
                                        — 静的seq定数の埋め込み
                                        — 動的式・ラムダの構文移植
                                        — [Composable] のインライン展開
```

生成物は同一partialクラス内の `RenderBody` オーバーライドであり、基底クラス `ComposeComponentBase` の `BuildRenderTree` から呼び出される。`Body` プロパティおよび全ファクトリAPIは実行時に到達不能であり、AOTビルドではILトリマーが除去する。

### 2.2 シーケンス割当

`Body` の式ツリー `e` を深さ優先(preorder)で走査し、各UIノードに互いに素なシーケンス区間を予約する。`counter` はソースコード上の絶対オフセットではなく、構文ツリーの論理的な preorder 走査順で割り振られる整数(preorder 序数)である。これにより、コメントや空白の変更がシーケンス番号の安定性に影響しないことが保証される。

```
procedure Compile(e: ExpressionTree, model: SemanticModel) → RenderBody:
    counter ← 0
    code ← ∅
    for each node v in DFS-Preorder(e):
        match Classify(v, model):
            case Factory(kind) | Decorator(kind):
                w ← FrameWidth(kind)                // 当該ノードが発行するフレーム数(静的既知)
                code += EmitFrames(kind, v.Args, seqBase: counter)
                counter ← counter + w
            case Combinator(If | ForEach):
                code += ExpandCombinator(v, ref counter)   // §2.4
            case ComposableCall(m):
                code += Compile(Body(m), model)            // インライン展開(再帰)
            case Transplantable(stmt):                     // ネイティブ if/foreach 等
                code += WrapInRegion(Transplant(stmt), seq: counter); counter += 1
            case Opaque(expr):                             // 非[Composable]のView返却呼び出し等
                code += WrapInRegion(EmitFragmentOf(expr), seq: counter); counter += 1
                report BC2001(v)
    return code
```

`FrameWidth` はシーケンス引数を消費する `RenderTreeBuilder` 呼び出し数のみをカウントし、`CloseElement`・`CloseRegion` のようにシーケンス引数を持たない呼び出しは含まない。ノード種別ごとに静的に定まる(例: `Text` = 2 [`OpenElement` + `AddContent`]、onclick属性1個付き `Button` = 3 [`OpenElement` + `AddAttribute` + `AddContent`])。装飾チェーンは親要素のクラス属性へ静的に合成されるため、装飾の追加はフレーム数を増やさない(`.Padding(24).Bold()` は単一の `AddAttribute` に畳み込まれる)。動的引数(補間文字列、状態参照、イベントラムダ)は評価されず構文として `EmitFrames` の出力へ移植される。同一partialクラス内に生成されるため、`this` 経由のprivateアクセスは保存される。

### 2.3 静的シーケンス可能サブセット SSC (Statically Sequenceable Constructs)

任意のC#コードに対して条件(2)の `σ` を構成することは不可能である(呼び出しグラフが実行時にのみ確定するため)。解析の適用範囲を次の3階層に分類する:

**SSC(完全静的)** — 静的シーケンス割当の対象:
- SSC-1: `Body` 本体、および `[Composable]` メソッド本体における、ファクトリ/装飾メソッドの直接呼び出し
- SSC-2: `If(cond, then, otherwise)` コンビネータ(両分岐がインラインラムダであること)
- SSC-3: `ForEach(source, key, content)` コンビネータ(`content` がインラインラムダ、`key` は必須)
- SSC-4: SSC-1〜3の任意のネスト、および `[Composable]` 呼び出しの静的インライン展開

**Transplantable(構文移植)** — ネイティブ `if` / `foreach` / `switch` 等の制御構文。生成コードへ構文ごと移植され、境界リージョンで包まれる(§2.5)。

**Opaque(実行時評価)** — `[Composable]` の付かない `View` 返却メソッド呼び出し、デリゲート経由の間接呼び出し等。SGは内部を解析できないため、呼び出し式を生成コードへ移植し、実行時に返された `View` に内包される `RenderFragment` をリージョン内で描画する。診断BC2001(Info)で通知される。

いずれの階層でも正確性は保たれる。失われるのはTransplantable/Opaque領域内部の静的差分最適化のみである。

### 2.4 条件分岐における静的シーケンス空間の分離

SSC-2の `If` について、両分岐に互いに素な静的シーケンス区間を予約する:

```
If(condition, then: T₁, otherwise: T₂)

割当:  seq(境界リージョン)  = k
       seq空間(T₁)          = [k+1,  k+1+W(T₁))
       seq空間(T₂)          = [k+1+W(T₁), k+1+W(T₁)+W(T₂))
```

生成コードの概念形:

```csharp
__b.OpenRegion(k);
if (condition)
{
    /* T₁ のフレーム列: seq ∈ [k+1, k+1+W(T₁)) */
}
else
{
    /* T₂ のフレーム列: seq ∈ [k+1+W(T₁), …) — T₁と重複しない */
}
__b.CloseRegion();
```

`condition` が `true → false` に遷移した際、`T₁` と `T₂` のシーケンスが交差しないため、Blazorエンジンは「同一スロットの書き換え(誤った状態引き継ぎ)」ではなく「セグメント全体の排他的破棄と新規生成」として正しく検知する。これは定理1の条件(1)を、分岐セマンティクス(異なる分岐のノードは意味的に非同一)と整合する形で満たす。

`ForEach`(SSC-3)は `foreach` へ展開され、テンプレート `content` に単一の静的シーケンス空間を割り当てた上で、反復インスタンス間の同一性を `SetKey(key(item))` で識別する。シーケンスが「テンプレート内の構文位置」を、キーが「データ同一性」を担い、責務が直交する。

### 2.5 リージョンによるシーケンス空間の分離

Transplantable / Opaque領域 `D` は、境界に単一の静的シーケンスを持つリージョンで包まれる:

```csharp
__b.OpenRegion(seq_D);           // seq_D は静的に割当済み
__b.SetKey(runtimeKey);          // Opaqueの場合、必要に応じてランタイムキー
/* D の内容 */
__b.CloseRegion();
```

Blazorのリージョンはシーケンス空間を分離するため、`D` 内部の動的性が外部のDiffingへ波及することはない。

### 2.6 Hot Reload適合性 (Edit and Continue Conformance)

開発時の編集を、.NET Hot Reload(EnC)の編集クラスに対応付けて分類する。

`Body` 式または `[Composable]` 本体の変更は、再生成された `RenderBody` のメソッド本体差し替えとして現れる。メソッド本体の更新はEnCが最も安定してサポートする編集クラスである。`[Composable]` メソッドの新規追加は既存型へのメンバー追加であり、同じくサポート範囲内である。コンポーネントクラスのシグネチャ変更等のrude editは、Razorコンポーネントと同様にアプリケーション再起動を要する。

リロード後の初回レンダリングの意味論は §1.2 から直接導かれる。編集により構文位置写像 `π` が変化した場合、新旧の `σ(π(n))` は一般に一致しないため(条件(1)の不成立)、当該コンポーネントのフレーム列は差分検知上「排他的破棄と新規生成」として扱われる。コンポーネントインスタンス自体は保持されるためC#フィールドの状態は残り、DOMローカル状態(フォーカス、スクロール位置等)は失われる。これはRazorファイル編集時と同一の意味論であり、追加の仕様を要しない。

適用経路もBlazor標準に乗る。生成コードは通常の `ComponentBase` 派生型のメソッドであるため、Blazorが備える `MetadataUpdateHandler` による更新後再レンダリング機構がそのまま機能する。本設計固有の検証項目は「編集セッション中にSource Generatorが再実行され、生成コードの更新がEnCへ適用されること」のツーリング挙動のみであり、Visual Studio / `dotnet watch` / Riderの3環境での実測を第1フェーズの受け入れ条件とする。特定環境で不安定と判明した場合の開発時フォールバックは付録Cに示す。

---

## 3. メモリレイアウト (Runtime Memory Representation)

### 3.1 SSC経路: 中間表現ゼロ

SSC(および Transplantable)経路の実行時像は、静的シーケンス定数を伴う `RenderTreeBuilder` 命令の直列実行である。これはRazorコンパイラの生成物と同形式であり、UI記述に由来する中間オブジェクト(要素ツリー、ビルダー、`params` 配列)は一切ヒープに生成されない。マーカー型 `View` は空の `readonly struct` であり、実行時に到達不能である。

したがって、SSC経路のアロケーション特性は等価なRazorコンポーネントと同等である(予測値、PoCで実測)。残存するアロケーション源はBlazor自体に由来するものに限られる: イベントハンドラのデリゲート/クロージャ、`RenderTreeBuilder` 内部のフレーム配列(再利用される)、補間による一時文字列(`ISpanFormattable` 経路で部分的に緩和)。

### 3.2 Opaque経路: フラグメント内包 `View`

Opaque経路でのみ、`View` は実体を持つ。この場合の `View` は `RenderFragment` への参照を内包する軽量ハンドルであり、ヒープ割り当ては内包フラグメントの構築分に限られる。これは `RenderFragment` を手書きで合成した場合と同等のコストである。

```csharp
public readonly struct View
{
    internal readonly RenderFragment? Fragment;   // SSC経路では常に null(到達不能)
    internal View(RenderFragment fragment) => Fragment = fragment;
}
```

### 3.3 静的サブツリーの定数化

状態に依存しないサブツリー(固定ヘッダー、利用規約等)について、Source Generatorは依存解析により状態参照を持たない領域を検出し、生成コード上で属性文字列・コンテンツを定数化する。フレーム発行自体はBlazorの差分検知が要求するため毎回行われるが、値の再計算・再フォーマットは発生しない。

---

## 4. イベント・プロパゲーションと並行モデル (Concurrency & Event Pipeline)

### 4.1 実行順序の半順序定義

ユーザーアクションからDOM更新までの各事象を以下と定義する:

```
e : イベント発火(ブラウザ)
d : Blazor SynchronizationContext へのディスパッチ完了
σ : 状態遷移  s_t ↦ s_{t+1}
ρ : フレーム列生成  r_{t+1} = γ(e)(s_{t+1})
δ : 差分適用  Δ(r_t, r_{t+1}) のDOM同期
```

これらは狭義半順序 `≺` に従う:

```
e ≺ d ≺ σ ≺ ρ ≺ δ                                … (3)
```

`σ ≺ ρ` は単一方向データフローの強制を意味する: `RenderBody` の実行中に `σ` を発生させてはならない。ソースレベルでは「`Body` / `[Composable]` 内での状態変更禁止」に対応し、違反は診断BC3001となる。`Button` のonClickラムダ(`DeferredEventHandler`コンテキスト)はレンダリングではなくイベント後に実行されるため除外される。任意のメソッド呼び出し経由の副作用の完全な検出は保証しない(§1.1 BC3001注記参照)。

### 4.2 Blazor標準ディスパッチとの役割分担

Blazorは既に `SynchronizationContext`(および `ComponentBase.InvokeAsync`)により、レンダリングスレッドへの直列化ディスパッチを提供している。BlazorComposeはこれを置換しない。本ライブラリが並行モデルに追加するのは次の2点に限定される。

第一に、順序(3)のうち `σ ≺ ρ` のアナライザーによる静的検証(Blazor標準は規約のみで強制機構を持たない)。第二に、外部スレッドからの複数の状態変更通知を単一の再レンダリングへ合流させる、`Interlocked` ベースのロックフリー通知合流:

```csharp
private int _renderPending; // 0 or 1

public void NotifyStateChanged()
{
    if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
    {
        _dispatcher.InvokeAsync(() =>
        {
            Volatile.Write(ref _renderPending, 0);
            StateHasChanged();
        });
    }
}
```

Wasm環境(現状実質シングルスレッド)ではCASが常に無競合で成功するため、オーバーヘッドは分岐1回に縮退する。

### 4.3 Runtime Async(net11.0 条件付き)

net11.0ターゲットでは、Runtime Async(ランタイムネイティブ非同期)により非同期イベントハンドラのステートマシンオーバーヘッドが低減され、スタックトレースが平坦化される。BlazorCompose側のコード変更は不要であり、TFM切替のみで恩恵を受ける。

---

## 5. WebAssemblyとAOTコンパイル適合性 (AOT & Wasm Optimization)

BlazorComposeは実行時メタデータ分析・動的ディスパッチを排除する。全パラメータバインディング(`Component<T>().Param(...)` を含む)は、Source Generatorが生成する静的セッター経由で行われる。`Param` の式引数はSGが構文解析してセッター生成にのみ利用し、式木(`System.Linq.Expressions`)のランタイムコンパイルは行わない。`System.Reflection` / `System.Linq.Expressions` へのランタイム依存は0である。

さらに、`Body` プロパティと全ファクトリAPIは実行時に到達不能であるため、ILトリマーはこれらを丸ごと除去できる。UI記述のソースコードがバイナリサイズに寄与しない点は、実行時評価を行うコードファースト方式に対する本方式固有の利点である。

リフレクションベースのバインディングを持つ同等構成との比較で、AOTコンパイル後のWasmペイロードサイズを約20〜30%削減(予測値)と見込む。本数値は第1フェーズPoCにおいて、(a) BlazorCompose構成、(b) リフレクションバインディング構成、(c) 素のRazor構成の3系統で実測し、確定値に置換する。素のRazor構成との比較ではほぼ同等となる見込みである。

---

## 6. .NET 11 条件付き形式定義: 閉世界 `ViewNode`(参考仕様)

net11.0ターゲットでは、C# 15のUnion型と `closed` 修飾子を用いて、Source Generatorの内部表現であるUIノード集合を閉じた判別共用体として定義する:

```csharp
#if NET11_0_OR_GREATER
public closed union ViewNode
{
    TextNode(string Content, StyleSet Style);
    StackNode(Axis Axis, int Spacing, ViewNode[] Children);
    ButtonNode(string Label, ActionRef Handler, ButtonStyle Style);
    RegionNode(int Seq, KeyRef? Key, ViewNode Body);
    ComponentNode(TypeRef ComponentType, ParameterBag Parameters);
}
#endif
```

閉世界化により、コンパイラ内部のビジター(フレーム発行、依存解析、診断)の網羅性がコンパイル時に検証され(ケース漏れはコンパイルエラー)、`FrameWidth`(§2.2)の全域性が型システムで保証される。

> 注記: Union型は.NET 11プレビュー時点で一部機能(member provider等)が未実装であり、本章はGA後に正式化される参考仕様である。net10.0ターゲットでは同等の構造を `sealed` クラス階層+網羅性アナライザーで近似する。

---

## 7. 技術適合仕様サマリー (Specification Matrix)

| 評価項目                   | Blazor(通常Razor)                 | BlazorCompose(本システム)                                    | 備考                                      |
| -------------------------- | --------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| 記述パラダイム             | マークアップファースト(HTML + C#) | コードファースト(純粋C#)                                     | SwiftUI/Compose と同一の記述体験          |
| 型安全性(Style/Layout)     | 低(文字列CSS/クラス名依存)        | 完全型安全(コンパイル時検証)                                 | IDEインテリセンスが100%駆動               |
| コンパイル方式             | Razorコンパイラ(マークアップ→C#)  | Source Generator(C#式→C#)                                    | 生成物は同形式                            |
| シーケンス番号管理         | コンパイラによる静的割当          | SGによる静的割当(SSC)+ リージョン分離(Transplantable/Opaque) | 開発者はシーケンス制御を意識不要          |
| 実行時の中間表現           | なし                              | なし(SSC経路)/ フラグメント内包 `View`(Opaque経路のみ)       | UI記述由来のヒープ割当ゼロ                |
| メモリ・レンダリングコスト | 基準                              | 同等(予測値)                                                 | 生成コードが同形式のため                  |
| AOT / Wasm互換性           | 適合                              | 完全適合(リフレクション依存0、UI記述コードはトリム除去)      | 対リフレクション構成で20〜30%削減(予測値) |
| Hot Reload                 | ツーリングに一級統合              | EnC標準経路(メソッド本体差替+`MetadataUpdateHandler`)        | 編集後の意味論はRazorと同一(§2.6)         |
| 対応TFM                    | —                                 | net10.0(ベースライン)/ net11.0(Union型内部表現等)            | LTS優先のマルチターゲット                 |

---

## 付録A: アナライザー診断一覧

| ID     | 種別    | 内容                                                                                  |
| ------ | ------- | ------------------------------------------------------------------------------------- |
| BC1001 | Error   | コンポーネントクラスが `partial` として宣言されていない(`RenderBody` を生成できない)  |
| BC2001 | Info    | Opaque構文を検出。動的リージョンへ縮退し、当該領域の静的差分最適化が失われる          |
| BC3001 | Error   | `Body` / `[Composable]` 本体内での状態変更(単一方向データフロー違反)。初期検出範囲: コンポーネントインスタンスメンバーへの直接書き込み。`Button` onClickラムダ(遅延イベントハンドラ)は除外。任意の副作用の完全検出は保証しない |
| BC3002 | Warning | `ForEach` の `key` セレクタが要素の恒等性を保証しない可能性(インデックスベースキー等) |

## 付録B: 検討した代替アーキテクチャと不採用理由

**B.1 Interceptor方式(C# 14)** — `Body` を実行時に評価し、各ファクトリ呼び出しサイトをInterceptorで静的シーケンス付き実装へ置換する方式。呼び出しサイト置換自体は成立するが、(a) 実行時評価を前提とするため装飾チェーンの合成型に対する統一戻り値型が構成できない(C#に不透明戻り値型が存在せず、`ref struct` はインターフェースへ変換できない)、(b) `[InterceptsLocation]` の位置指定子がソース変更のたびに再計算され、ビルドパイプラインが位置データに敏感になる、(c) 本方式(全体生成)が採用可能である以上、部分置換に固有の利点がない、の3点により不採用。

**B.2 ランタイム `ref struct` ツリー方式** — 要素を `readonly ref struct` としてスタック上に構築し、実行時に `Render` を再帰呼び出しする方式。GC回避には有効だが、(a) 可変個の子要素を受け取る手段がない(`ref struct` は配列・`params` に格納不可、ジェネリックオーバーロードはアリティ上限を持つ)、(b) B.1と同じ戻り値型問題、(c) 静的サブツリーのキャッシュと両立しない(`ref struct` はフィールド格納不可)、により不採用。本方式(生成コードによる直接発行)は、同じゼロアロケーション特性を型システム上の無理なく達成する。

## 付録C: 開発時フォールバック案 — 解釈モード(コンチネンシー)

§2.6のツーリング検証で、特定環境においてSource Generatorの再実行がEnCに反映されないと判明した場合に限り、次のDEBUGビルド限定フォールバックを導入する余地を残す。

DEBUG構成では、ファクトリ・装飾API群を慣性実装から実働実装(`View` に `RenderFragment` を構築して内包する)へ条件コンパイルで切り替え、`RenderBody` の代わりに `Body` を実行時評価する。全体は単一のリージョン内で動的シーケンスを用いて描画される。Hot Reloadは `Body` プロパティ本体の差し替え(EnC標準サポート)として自然に機能し、SGの再実行に依存しない。RELEASE構成では本仕様の生成コード経路のみが用いられるため、出荷物の性能・サイズ特性に影響しない。

本案は開発時と実行時で描画経路が二重化する複雑性を伴うため、ツーリング実測で必要性が確認されるまで導入しない。