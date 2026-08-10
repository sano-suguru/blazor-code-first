# BlazorCodeFirst Architecture

**内部アーキテクチャ: コンパイルアルゴリズム、シーケンス割当、メモリレイアウト**

前提環境: .NET 10(ベースライン)、.NET 11(条件付き機能)

> 背景・目的・使い方の概要は `DESIGN.md` を参照。

---

## 0. 表記と前提

記号を用いるのは、シーケンス番号の安定条件(§1.2)という本設計の中核を厳密に述べる箇所に限ります。そこでは集合・写像の基本的な記法(`f : A → B` は写像、`|X|` は要素数)を用います。それ以外の箇所は通常の文章で記述します。

本仕様が依存する言語・ランタイム機能:

| 機能                                             | 要件                               | 用途                             |
| ------------------------------------------------ | ---------------------------------- | -------------------------------- |
| Source Generatorによる部分クラスへのメンバー生成 | 全対応バージョン(成熟した標準機能) | `RenderView` の生成(§2)          |
| ILトリミング / Native AOT                        | .NET 10                            | 慣性API・未使用コードの除去(§5)  |
| Union型 / `closed` 階層                          | C# 15 / .NET 11(条件付き)          | `ViewNode` の閉世界定義(§6)      |
| Runtime Async                                    | .NET 11(条件付き)                  | イベントパイプライン軽量化(§4.3) |

コア機構が特定の最新言語機能に依存しない点は、本設計の意図的な性質です。検討の末に不採用とした代替アーキテクチャ(Interceptor方式、ランタイムref structツリー方式)とその理由は付録Bに記します。

---

## 1. 抽象数理モデルと形式定義

### 1.1 状態と射影

コンポーネントの状態空間を `S`、時刻 `t` における状態を `s_t`(`s_t ∈ S`)とします。Blazor内部のレンダリングツリー(フレーム列)の集合を `R` とし、時刻 `t` に生成されるフレーム列を `r_t`(`r_t ∈ R`)とします。`R` と `r_t` は差分検知の安定条件(§1.2)で用います。

Source Generatorはビルド時に、設計時のUI式を「状態を受け取ってフレーム列を返す関数」(型でいえば `S → R`)へコンパイルします。実行時に動くのはこの生成関数だけであり、`r_t` はそれを状態 `s_t` に適用した結果です。UI式そのもの(設計時の構文的実体)は実行時には評価されません。Razorとの対比で言えば、Razorコンパイラはこの入力をマークアップとして受け取り、BlazorCodeFirstはC#式として受け取る、という違いです。

生成された関数は純粋(状態のみに依存し副作用を持たない)であることを規約とします(単一方向データフロー、§4.1)。設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)内の状態変更は診断BCF3001の対象となります。BCF3001の初期検出範囲はコンポーネントのインスタンスメンバーへの静的識別可能な直接書き込み(フィールド代入、プロパティ代入、複合代入、インクリメント/デクリメント演算子)に限ります。`Button` のonClickラムダ(`DeferredEventHandler`として分類)内の変更はレンダリング後に実行されるため除外されます。任意のメソッド呼び出し経由の副作用(非同期連鎖等)の完全な検出は初期スライスでは保証しません。

### 1.2 レンダリングツリーの等価性と差分検知

`R` の各フレーム `n ∈ r_t` はシーケンス番号 `seq(n) ∈ ℕ` を持つ。Blazorの差分演算子を

```
Δ : R × R → Patch
```

とし、`Δ(r_t, r_{t+1})` がDOMへ適用されます。Blazorの差分アルゴリズムは、両ツリーを先頭から同時走査し、シーケンス番号の一致・大小比較のみでフレームの同一性(保持/挿入/削除)を判定します。

**定理1(シーケンス安定性条件)**
`Δ` が最小コスト O(|r_t| + |r_{t+1}|) で、かつ意味的に同一のノードの状態を保存するためには、任意の意味的同一ノード対 `(n, n′)`(`n ∈ r_t`, `n′ ∈ r_{t+1}`)について次が成立しなければなりません:

```
seq(n) = seq(n′)                                   … (1)
```

**系1**: 条件(1)を満たす十分条件は、`seq` が実行時の生成順序ではなくソースコード上の構文位置の関数であることです。フレームを生成した式ノードの構文位置を `π(n)` としたとき、ある単射 `σ` が存在して:

```
seq(n) = σ(π(n)),   σ : Π → ℕ は単射             … (2)
```

本方式では `σ` はビルド時にSource Generatorが構成し、生成コードへリテラル定数として埋め込まれるため、条件(2)は構造的に満たされます。対照的に、ランタイムインクリメント方式(`seq(n) = 生成順序`)は、条件付きレンダリングや要素挿入により `π` と生成順序の対応が崩れた時点で条件(1)に違反し、計算量が O(n) の走査へ劣化します。

条件(1)の違反から先に何が起きるかは、キーの有無で分かれます(実測値は `DESIGN.md` §7.2)。キーを持たない場合、一致すべきフレーム以降が「削除+新規挿入」と誤判定され、再構築されたコンポーネントの内部状態(入力中のテキスト等)が消失します。ただしその範囲は構造的条件に依存し、ずれ幅がノードのフレーム幅の倍数であるときは後続ノードが1つずれた位置で一致するため、破棄は末尾に限られ残りはテキスト書き換えになります。一方、キーを持つ場合は兄弟グループ内のキー照合が成立するため、シーケンスがずれても状態は保持されます。つまり状態消失は、条件(1)違反とキーの不在が重なって初めて生じます。条件(1)違反だけからは導かれません。

シーケンス番号が構文位置に固定されていることが状態保持に効いてくるのは、リージョン(`OpenRegion`、§5.3)が介在する場合です。リージョン自身のシーケンスがずれるとリージョンごと破棄され、キー照合は兄弟グループの内側でしか働かないため状態を救えません。`If` / `ForEach` がリージョンを発行する本方式において、条件(2)はこの意味で本質的です。

---

## 2. コンパイルアルゴリズム

### 2.1 全体パイプライン

```
[ユーザーコード]                     [Source Generator]
partial class C :                    ① partial検証・Body発見
BodyComponentBase                 ② SSC分類(§2.3)
  View Body => …        ──AST──▶    ③ DFS順シーケンス割当(§2.2)
  [Composable] View F() => …         ④ RenderView(RenderTreeBuilder) の生成
                                        — 静的seq定数の埋め込み
                                        — 動的式・ラムダの構文移植
                                        — [Composable] のインライン展開
```

生成物は同一partialクラス内の `RenderView` オーバーライドであり、基底クラス(`BodyComponentBase` またはレイアウトの `ChromeLayoutBase`)の `BuildRenderTree` から呼び出されます。設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)および設計時API、すなわち `Html`・`Decorations` の全メンバーと設計時慣性型 `View` / `ComponentView<T>` / `ElementBuilder`(付録A、BCF3014)の全メンバーは、いずれも実行時に到達不能であり、AOTビルドではILトリマーが除去します。除去は `System.Reflection.Metadata` によるMethodDef不在検査をもって確認できる設計であり、その確認手段はトリムテストが担います。

設計時表現のゲッターは**単一の式に還元できなければなりません**。`=> expr` / `get => expr` /
`get { return expr; }` の 3 つの綴りは同一であり、いずれも同じ `RenderView` を生成します。文を含む
ゲッター(例: return の前のローカル変数宣言)は Transplantable 経路の領域であり未実装のため、BCF1004 と
して報告されます。自動プロパティは翻訳対象となるゲッター本体を宣言しないため、これも BCF1004 となります
(再abstract化 `abstract override` および実装部を持たない partial プロパティは対象外、後者は CS9248 が
原因を名指します)。設計時表現は実行時に評価されない不活性な構文であり、この制約は「式を静的に翻訳する」
という前提そのものです。

設計時表現の代わりに `RenderView` を手書きでオーバーライドすることは合法であり、SSC部分集合で表現できない
ボディのためのエスケープハッチです。この場合ジェネレータは何も生成しません(生成すると同名メンバーの重複で
CS0111 になり、著者は自分のコードを消すしか手がなくなります)。設計時表現は未使用となり、BCF1004 も報告され
ません。

BlazorCodeFirstコンポーネントとして認識される宣言形状は、トップレベルの `partial class` です。ジェネリック
(`partial class Foo<T>`)はサポートされ、生成部は同じ型パラメータ名を再掲します(制約句は再掲しません。
制約は型パラメータに属するため一方の宣言にあれば十分です)。ネストした型は BCF1005 で拒否されます。
`record` は `object` または別の `record` しか継承できないため(CS8864)、BlazorCodeFirstコンポーネントにはできません。

### 2.2 シーケンス割当

`Body` の式ツリー `e` を深さ優先(preorder)で走査し、各UIノードに互いに素なシーケンス区間を予約します。`counter` はソースコード上の絶対オフセットではなく、構文ツリーの論理的な preorder 走査順で割り振られる整数(preorder 序数)です。これにより、コメントや空白の変更がシーケンス番号の安定性に影響しないことが保証されます。

```
procedure Compile(e: ExpressionTree, model: SemanticModel) → RenderView:
    counter ← 0
    code ← ∅
    for each node v in DFS-Preorder(e):
        match Classify(v, model):
            case Factory(kind) | Decorator(kind):
                w ← EmittedWidth(v)                 // 当該ノードが発行したフレーム数(発行が権威、§2.7(D))
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
                report BCF2001(v)
    return code
```

上の擬似コードはノード単位のループとして書いていますが、畳み込みの単位は**連続する兄弟の run** であり(§2.7(D))、run 全体が1つの `AddMarkupContent` として発行されます。したがって幅を定めるのは発行そのものであって、ノード種別ではありません。#69 で `SequenceAllocator.Width` を削除して発行側を単一の権威にしたのはこのためで、独立に幅を計算する実装はもう存在しません(§2.7(B)末尾)。

`FrameWidth` はシーケンス引数を消費する `RenderTreeBuilder` 呼び出し数のみをカウントし、`CloseElement`・`CloseRegion` のようにシーケンス引数を持たない呼び出しは含みません。ノード種別と、そのノードが畳み込み可能かどうかから定まります(例: 子を持たない `Span` = 1 [`OpenElement`]、**動的な**文字列子を1つ持つ `Span`(`Span[$"...{x}"]`)= 2 [`OpenElement` + `AddContent`]、onclick属性1個付き `Button` = 3 [`OpenElement` + `AddAttribute` + `AddContent`]。イベントは畳み込みを阻むため、`Button` の子が定数でもこの幅です)。対して**定数**の文字列子を1つ持つ `Span`(`Span["..."]`)はそれ自体が畳み込み可能なので幅 1 です(`AddMarkupContent` 1回)。装飾チェーンのうち `class` は親要素の `class` 属性へ静的に合成されるため、`.Class` の追加はフレーム数を増やしません(`.Class("a").Class("b")` は単一の `AddAttribute` に畳み込まれます)。`class` 以外の属性・イベント装飾(`.Href` / `.Attr` / `.OnClick` / `.On` 等)はそれぞれ1装飾につき1フレームが追加されます(詳細は§2.7(A))。例外は `.Bind` で、1つにつき属性フレームとイベントフレームの2つを追加します。同一要素に何個でも置けるため、この2フレームがその個数ぶん積まれます(§2.7(A))。動的引数(補間文字列、状態参照、イベントラムダ)は評価されず、構文として `EmitFrames` の出力へ移植されます。同一partialクラス内に生成されるため、`this` 経由のprivateアクセスは保存されます。

値式を生成コードへ移植するとき、解決済みの型名は `global::` から始まる完全修飾名へ正規化します。未解決の型名は、元ファイルの `using` や名前空間に依存する表記のままでは安全に移植できないためBCF3015とします。ただし、作者が `global::` から記述した型参照は字句コンテキストに依存しないので通常のC#の名前解決に委ねます。ジェネリック型の外側と各型引数は独立に判定します。

`Html.Fragment`(ラッパーレスなグルーピング)は自身のフレームを開かないため、その `FrameWidth` は子ノードの `FrameWidth` の総和です(ローカル変数を持たない `[Composable]` 展開ノードと同型)。ただし子がすべて畳み込み可能な場合、fragment 全体が1つの run となり幅は 1 になります(§2.7(D))。`Html.Raw`(信頼済み生HTML注入)は `AddMarkupContent` を1回発行するだけの単一フレームで、`FrameWidth` = 1 です(子を持たない文字列コンテンツノードの `AddContent` と同型)。いずれも要素/コンポーネントのフレームを開かないため、`ForEach` の `content` の根には使えず(BCF3003)、装飾もできません(BCF3008、詳細は§2.7(A)と付録A)。

装飾不可は型システムでも表現されています。装飾は `ElementBuilder` の拡張であり、`Fragment` / `Raw` は `View` なのでCS1929です。それでもBCF3008を報告するのは、このCS1929が作者へ届かないためです。設計時表現が翻訳できないコンポーネントには `RenderView` が生成されず、クラスは必ず宣言段階エラーのCS0534を負うため、`csc` はメソッド本体の束縛へ進みません。`RejectedDecorationScanner` が存在しなかった時点の実MSBuild測定では、フィクスチャ `Bcf3008Host` が報告したのはCS0534とBCF1003だけで、CS1929は現れませんでした(BCF3008を報告するようになった現在は、同じフィクスチャがそれも報告します)。同じビルドでBCF1003は届いています。この打ち切りを越えられるのは生成器の診断だけです。

### 2.3 静的シーケンス可能サブセット(SSC)

任意のC#コードに対して条件(2)の `σ` は構成できません(呼び出しグラフが実行時にのみ確定するため)。解析の適用範囲を次の3階層に分類します:

**SSC(完全静的)**: 静的シーケンス割当の対象。
- SSC-1: `Body` 本体、および `[Composable]` メソッド本体における、要素ヘルパー/装飾の直接記述、および `Component<T>()`・`Fragment`・`Raw` の直接呼び出し
- SSC-2: `If(cond, then, otherwise)` コンビネータ(両分岐がインラインラムダであること)
- SSC-3: `ForEach(source, key, content)` コンビネータ(`content` がインラインラムダ、`key` は必須)
- SSC-4: SSC-1〜3の任意のネスト、および `[Composable]` 呼び出しの静的インライン展開

**Transplantable(構文移植)**: ネイティブ `if` / `foreach` / `switch` 等の制御構文。生成コードへ構文ごと移植され、境界リージョンで包まれます(§2.5)。

**Opaque(実行時評価)**: `[Composable]` の付かない `View` 返却メソッド呼び出し、デリゲート経由の間接呼び出し等。SGは内部を解析できないため、呼び出し式を生成コードへ移植し、実行時に返された `View` に内包される `RenderFragment` をリージョン内で描画します。診断BCF2001(Info)で通知されます。

いずれの階層でも正確性は保たれます。失われるのはTransplantable/Opaque領域内部の静的差分最適化のみです。

### 2.4 条件分岐における静的シーケンス空間の分離

SSC-2の `If` について、両分岐に互いに素な静的シーケンス区間を予約します:

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

`condition` が `true → false` に遷移した際、`T₁` と `T₂` のシーケンスが交差しないため、Blazorエンジンは「同一スロットの書き換え(誤った状態引き継ぎ)」ではなく「セグメント全体の排他的破棄と新規生成」として正しく検知します。これは定理1の条件(1)を、分岐セマンティクス(異なる分岐のノードは意味的に非同一)と整合する形で満たします。

`ForEach`(SSC-3)は `foreach` へ展開され、テンプレート `content` に単一の静的シーケンス空間を割り当てた上で、反復インスタンス間の同一性を `SetKey(key(item))` で識別します。シーケンスが「テンプレート内の構文位置」を、キーが「データ同一性」を担う責務分担と、リスト変異時の最小パッチは §2.7(B) に入出力例として示します。

### 2.5 リージョンによるシーケンス空間の分離

Transplantable / Opaque領域 `D` は、境界に単一の静的シーケンスを持つリージョンで包まれます:

```csharp
__b.OpenRegion(seq_D);           // seq_D は静的に割当済み
__b.SetKey(runtimeKey);          // Opaqueの場合、必要に応じてランタイムキー
/* D の内容 */
__b.CloseRegion();
```

Blazorのリージョンはシーケンス空間を分離するため、`D` 内部の動的性が外部のDiffingへ波及することはありません。

### 2.6 Hot Reload適合性

開発時の編集を、.NET Hot Reload(EnC)の編集クラスに対応付けて分類します。

`Body` 式または `[Composable]` 本体の変更は、再生成された `RenderView` のメソッド本体差し替えとして現れます。メソッド本体の更新はEnCが安定してサポートする編集クラスです。`[Composable]` メソッドの新規追加は既存型へのメンバー追加であり、同じくサポート範囲内です。コンポーネントクラスのシグネチャ変更等のrude editは、Razorコンポーネントと同様にアプリケーション再起動を要します。

リロード後の初回レンダリングの意味論は §1.2 から直接導かれます。編集により構文位置写像 `π` が変化した場合、新旧の `σ(π(n))` は一般に一致しないため(条件(1)の不成立)、当該コンポーネントのフレーム列は差分検知上「排他的破棄と新規生成」として扱われます。コンポーネントインスタンス自体は保持されるためC#フィールドの状態は残り、DOMローカル状態(フォーカス、スクロール位置等)は失われます。これはRazorファイル編集時と同一の意味論であり、追加の仕様を要しません。

適用経路もBlazor標準に乗ります。生成コードは通常の `ComponentBase` 派生型のメソッドであるため、Blazorが備える `MetadataUpdateHandler` による更新後再レンダリング機構がそのまま機能します。本設計固有のツーリング依存は「編集セッション中にSource Generatorが再実行され、生成コードの更新がEnCへ適用されること」の一点のみです。Visual Studio / `dotnet watch` / Riderで挙動差が生じうるため、環境ごとの確認を要します。特定環境で再実行がEnCへ反映されないと判明した場合の開発時フォールバックは付録Cに示します。

### 2.7 主要な変換の入出力仕様: 装飾の畳み込み・リスト・部品再利用・静的畳み込み

本方式で要となるのは、装飾チェーン・リスト・`[Composable]`・静的サブツリーの4つの変換です(単純な要素発行はここに含みません)。§2.4の `If` と同じ密度で、それぞれ「どの入力を、どの生成コードに変えるか」を定めます。

**(A) 装飾チェーンの畳み込み。入力: 装飾の連鎖 / 出力: `class` は畳み込み、他の属性・イベントは1:1のフレーム**

装飾メソッドは所有要素の属性・イベントへ静的に合成され、ラッパーノードを増やしません。`class` は特別で、`.Class`(または `.Attr("class", …)`)を何個連ねても単一の `class` 属性へ畳み込まれ、追加の属性フレームは生まれません。`class` 以外の属性・イベント(`.Href` / `.Attr` / `.OnClick` / `.On` 等)はそれぞれ独立した属性/イベントフレームとして1:1で発行され、同一属性・イベントの重複バインディングはBCF3010で診断されます。`class` に届く綴りは3つあり、畳み込むのはそのうち2つです。`.Bind("class", …)` はチャネルへ加わらず自分の属性フレームを出すため、装飾と共存させると `class` 属性が2つ発行されます。これはBCF3024で診断されます。

```csharp
// 入力(設計時のC#式)
Button
    .Class("btn")
    .Class("btn-primary")
    .OnClick(() => Save())["Save"]
```

```csharp
// 出力(生成コード): 2つの .Class は1つの class 属性へ畳み込まれ、.OnClick は独立したフレーム
__b.OpenElement(k,   "button");
__b.AddAttribute(k+1, "class", "btn btn-primary");
__b.AddAttribute(k+2, "onclick", /* () => Save() */);
__b.AddContent(k+3, "Save");
__b.CloseElement();
```

この `Button` の `FrameWidth` は4(`OpenElement` + `class` 属性 + `onclick` イベント + `AddContent`)です。`.Class` を何回連ねてもフレーム幅は増えませんが、`class` 以外の装飾を1つ追加するとフレーム幅も1つ増えます。ラッパーノード方式(装飾ごとに専用のラッパー要素を生成する方式)であれば装飾はDOMノードそのものを増やしますが、本方式はいずれの装飾も所有要素の属性・イベントとして合成するためDOM深さは増えません。要点は「`class` は装飾の個数によらずフレーム幅が一定に畳み込まれる一方、それ以外の属性・イベントは1装飾につき1フレームの1:1対応である」という非対称性で、この不変性が装飾を重ねても差分検知のシーケンス割当が安定する根拠です。

1:1の唯一の例外が双方向束縛です。`.Bind` は属性フレーム1つとイベントフレーム1つを発行するため、この装飾の `FrameWidth` は2です。束縛先の属性が `value` または `checked` のときは、加えて `SetUpdatesAttributeName` を1回呼びますが、これはシーケンス引数を取らないためフレームを増やしません(直前の属性フレームに、再同期対象の属性名を記録するだけです)。この2つの属性名に限るのは、クライアントが返すのが `EventFieldInfo` の組み立てるその要素自身の `value`(チェックボックスなら `checked`)だけであり、`RenderTreeUpdater` はその値をこの呼び出しが指名したフレームへ書くためです。それ以外の属性名を指名すると、フォーム要素では無関係のフレームを上書きして本来の属性を取り残し、フォーム要素以外では `EventFieldInfo.fromEvent` が `null` を返すため呼び出し自体が空振りになります。記録先が要素ではなく直前の属性フレームであるため、同一要素に2つの束縛を置いても各々が自分の名前を保ち、上書きも再同期の喪失も起きません(実測)。同一要素に束縛を何個置いても構いません。モデル側も要素あたりの束縛をコレクションとして持ちます。名前が衝突した場合はBCF3010が報告し、束縛先が `class` で同じ要素がクラスチャネルへの装飾も持つ場合はBCF3024が報告します。かつてこれをBCF3021で拒否していましたが、根拠が誤りであったため撤回しました(付録B.5)。

```csharp
// 入力(設計時のC#式)
Input.Type("text").Bind("value", "oninput", () => _name)
```

```csharp
// 出力(生成コード): 属性フレームとイベントフレームの2つ、そして再同期の記録
__b.OpenElement(k,   "input");
__b.AddAttribute(k+1, "type", "text");
__b.AddAttribute(k+2, "value", _name);                  // 属性フレーム
__b.AddAttribute(k+3, "oninput", EventCallbackFactoryBinderExtensions.CreateBinder(
    EventCallback.Factory, this, __value => _name = __value, _name));   // イベントフレーム
__b.SetUpdatesAttributeName("value");                   // シーケンス引数を取らない
__b.CloseElement();
```

`CreateBinder` を拡張メソッドの静的呼び出しとして書くのは、生成ファイルが `using` を持たず、Razorの書くインスタンス構文(`EventCallback.Factory.CreateBinder(…)`)がCS1061になるためです。同じ正規化を作者の書いた拡張メソッドにも適用しています(§2.2)。setterを明示する形では、この `__value => …` の位置に `(Action<T>)(setter)` が、非同期setterでは `RuntimeHelpers.CreateInferredBindSetter(callback: setter, value: 現在値)` が入ります。いずれの形でも現在値を `CreateBinder` の最後の引数として渡す点と、フレーム数は変わりません。

`.Bind` は(D)の静的畳み込みに参加しません。値がフィールドやプロパティの読み出しである以上コンパイル時定数になり得ませんが、畳み込みを止めているのは値の非定数性ではなく述語そのものです(`StaticMarkupSerializer.IsFoldableElement` が、束縛のコレクション `ElementNode.Bindings` が空でない要素を畳み込み不可として返します)。値の判定に任せれば、束縛が黙って落ちてただの属性だけが残る出力を、この述語が原理的に作れてしまうためです。

コンポーネント側の `.Bind` はこの非対称性を持ちません。導かれた `{名前}Changed` と `{名前}Expression` は通常のパラメータフレームとして積まれるため、フレーム幅は `.Param` 2回ぶん、`{名前}Expression` を宣言している型に対しては3回ぶんです((D)末尾のコンポーネントのフレーム幅の式がそのまま成り立ちます)。要素側の `SetUpdatesAttributeName` に相当するものもありません。DOMを持つのは束縛先のコンポーネントであって、この呼び出し元ではないためです。

**(B) `ForEach`。入力: リストの変異 / 出力: キー整合の最小パッチ**

`ForEach`(SSC-3)は `foreach` へ展開され、テンプレート `content` に単一の静的シーケンス空間を割り当てた上で、反復インスタンス間の同一性を `SetKey(key(item))` で識別します。シーケンスが「テンプレート内の構文位置」を、キーが「データ同一性」を担い、責務が直交します。

```csharp
// 入力
ForEach(_items, key: t => t.Id, content: item =>
    Div.Class(item.Done ? "task done" : "task")[Span[item.Title]])
```

```csharp
// 出力(生成コード): テンプレートのseqは反復間で不変、同一性はキーが担う
__b.OpenRegion(k);
foreach (var item in _items)
{
    __b.OpenElement(k+1, "div");                        // Div (content の根要素): seq ∈ [k+1, k+1+W(content))
    __b.SetKey(item.Id);                                // ← 根要素を開いた「直後」に付ける
    __b.AddAttribute(k+2, "class", item.Done ? "task done" : "task");
    __b.OpenElement(k+3, "span"); __b.AddContent(k+4, item.Title); __b.CloseElement();
    __b.CloseElement();
}
__b.CloseRegion();
```

`SetKey` は Blazor の `RenderTreeBuilder` において「現在開いている要素/コンポーネントフレーム」にキーを付与します(Razor の `@key` と同型)。したがってキーは `content` の**根要素/コンポーネントを開いた直後**に出さなければならず、`OpenElement` の前(親がリージョンの状態)で呼ぶと実行時に `InvalidOperationException: Cannot set a key on a frame of type Region.` となります。この帰結として、`ForEach` の `content` は**単一の要素またはコンポーネントを根に持つ**必要があります(キーの置き場が要素/コンポーネントに限られるため)。`content` の根がリージョンになる形(裸の `if`/`ForEach`/`switch` 等)はキーを適用できず、診断 BCF3003(Error)で通知します。`Html.Fragment`(ラッパーレスなグルーピング)と `Html.Raw`(信頼済み生HTML注入)も単一の要素/コンポーネントフレームを開かない点で同じ制約を受け、`content` の根には使えません(BCF3003)。入れ子のキー付きリストは内側ループを容器要素で包みます(例: `content: o => Div[ForEach(o.Items, …)]`)。これは Razor で `@if` に直接 `@key` を付けられず要素で包むのと同じ制約です。

この非キー可能性の判定は2つの層で行われ、両者は一致します。テンプレート走査層(`KeyabilityResolver.ResolveRootKind`)は `IfTemplateNode` / `ForEachTemplateNode` / `TextContentTemplateNode` / `FragmentTemplateNode` / `RawMarkupTemplateNode` / `RenderFragmentContentTemplateNode` をすべて `ContentRootKind.Region` に分類し、`ComponentTemplateNode` / `ElementTemplateNode` のみが `ContentRootKind.Element` です。列挙の最後にある `RenderFragmentContentTemplateNode` は、外部由来の `RenderFragment?` を `AddContent(seq, RenderFragment?)` としてそのまま発行するノードです。静的展開後ツリー層(`ComposableExpander.IsKeyableRoot`)は `ComponentNode` / `ElementNode` のみを真とし、それ以外は既定で `false` を返します。

未知のノード型に対する扱いは、この2層で意図的に非対称です。`IsKeyableRoot` の既定 `false` は、新種のノードが増えてもキー可否判定を安全側(非キー可能)へ倒します。一方 `RenderViewEmitter.EmitNode` / `KeyabilityResolver.ResolveRootKind` / `ComposableExpander.ExpandNode` は未知のノード型に対して例外を送出し、ケース漏れを黙って通しません。フレーム発行・根種別解決は「未知のノード型はバグとして早期検出する」契約、`IsKeyableRoot` は「未知のノード型は非キー可能として扱う」既定、という分担です。

シーケンス幅を定める実装は発行そのものだけです。各 `Emit*` は自身が進めたカーソルを返し、兄弟の開始位置はその戻り値です。したがって新種のノードを追加する際に足すケースは `RenderViewEmitter.EmitNode` の1箇所で、漏れは例外で検出されます。かつては `SequenceAllocator.Width` が同じ算術を独立に実装しており、要素の分岐条件と順序を発行側と一致させる義務をコメントで課していましたが、#69 で削除しました。

シーケンス算術を守るのは、発行されたテキストが持つ性質です。生成コードに現れるシーケンス引数は、木の形に関わらず出現順で `0..N-1` の密な連番になります。どのノード種別も、予約した番号を必ずテキストへ書くためです(`If` は両分岐を予約し両方を発行し、`ForEach` は content 幅を予約し content を発行し、スロットは外側の平坦なカウンタを継続し、`CloseElement` / `CloseRegion` / `CloseComponent` / `SetKey` は消費しません)。全ノード種別を覆うコーパスに対して `RenderViewEmitterSequenceTests` がこれを検査します。独立計算した幅との比較は合計しか見ないため、相殺する2つの誤りと `If` の分岐レンジの重複を通しますが、この性質は両方を落とします。なおこの密性は本実装の割当方式の性質であり、Blazor の要求ではありません。Blazor が要求するのは、シーケンス番号が構文位置に対して安定であることだけです。

`RenderFragmentContentNode` が消費するシーケンス番号は、`RenderFragment?` の非nullを問わず常に1です。シーケンス引数を消費する `AddContent` 呼び出しが必ず要り、それが開くリージョンフレームだけが非nullのとき限りであるためです。

入力が `[A, B, C]` から先頭挿入で `[X, A, B, C]` へ変異した場合の出力パッチを追います。テンプレートのシーケンス番号は全反復で同一であり、識別はキーが担うため、Blazorはキー `A, B, C` を既存フレームへ一致させ(行の状態とDOMサブツリーを保持)、`X` の1行のみを挿入します。仮にキーがインデックス由来であれば、位置0を「A→X の変更」、位置1を「B→A の変更」…と誤認し、全行を書き換えて各行のローカル状態(フォーカス位置等)を失います。キーが「データ同一性」を、シーケンスが「テンプレート位置」を分担することが、この最小パッチと状態保持を同時に成立させます。

**(C) `[Composable]` の静的インライン展開。入力: 部品呼び出し / 出力: 連続seqへの直接展開**

`[Composable]` メソッド呼び出しは、呼び出しサイトへ本体をインライン展開します(§2.2 の `ComposableCall` ケース)。メソッド呼び出しもリージョン境界も生成されず、シーケンス番号は周囲の本体と連続します。引数は構文として移植されます。

```csharp
// 入力
protected override View Body =>
    Div[Toolbar("My App"), Span["Body"]];

[Composable]
private static View Toolbar(string title) =>
    Div.Class("toolbar")[Span[title]];
```

```csharp
// 出力(生成コード): Toolbar はインライン展開され、seqは 0 から連続する
__b.OpenElement(0, "div");                              // Div (Body の根要素)
//   ↓ Toolbar("My App") のインライン展開開始(リージョン境界なし)
__b.OpenElement(1, "div");                              // Div (Toolbar 本体)
__b.AddAttribute(2, "class", "toolbar");
__b.OpenElement(3, "span"); __b.AddContent(4, "My App"); __b.CloseElement();  // 引数 title を移植
__b.CloseElement();
//   ↑ Toolbar 展開終わり
__b.OpenElement(5, "span"); __b.AddContent(6, "Body"); __b.CloseElement();
__b.CloseElement();
```

`[Composable]` 呼び出しは、その本体を呼び出しサイトへ直接書いた場合と同じフレーム列・シーケンス区間を生みます。実行時ディスパッチもリージョン分離も介在しません。対照的に、`[Composable]` の付かない `View` 返却メソッドはOpaque(§2.3)として扱われ、リージョンで包まれ実行時に `RenderFragment` として描画され、診断BCF2001の対象となります。部品再利用の速度・トリミング特性を分けるのは、この静的展開可能性です。属性を付けたかどうかは関係しません。

**(D) 静的サブツリーの畳み込み。入力: 定数だけで書かれた兄弟の連なり / 出力: 1つの `AddMarkupContent` フレーム**

値がコンパイル時定数である要素・テキストだけで構成された部分は、マークアップ文字列へ直列化され単一の `AddMarkupContent` フレームとして発行されます。畳み込みの単位は**サブツリーではなく run**、すなわち連続する畳み込み可能な兄弟の極大列です。

```csharp
// 入力(設計時のC#式)
Div.Class("doc")[
    H1["BlazorCodeFirst"],
    Nav.Class("toc")[A.Href("#design")["Design"]],
    Span[$"Section {Index}"],
    P["Attributes are written before children."]]
```

```csharp
// 出力(生成コード): 動的な Span の前後がそれぞれ1フレームへ畳み込まれる
__b.OpenElement(0, "div");
__b.AddAttribute(1, "class", "doc");
__b.AddMarkupContent(2, "<h1>BlazorCodeFirst</h1><nav class=\"toc\"><a href=\"#design\">Design</a></nav>");
__b.OpenElement(3, "span"); __b.AddContent(4, $"Section {Index}"); __b.CloseElement();
__b.AddMarkupContent(5, "<p>Attributes are written before children.</p>");
__b.CloseElement();
```

畳まれた run のフレーム幅は、run が含む要素・属性・テキストの個数によらず1です。隣接する静的兄弟は、間に動的なものが無ければ1フレームへ合体します。この「サブツリーではなく run」という単位は #142 の測定が示した訂正であり、削減の実体は個々の静的サブツリーではなく静的兄弟の連なりから来ます。

ラッパー要素が要素フレームのまま残るのは、**畳み込めない子を持つときだけ**です。上の例で `div` が残るのは動的な `Span` を子に持つからで、マークアップフレームは完全なマークアップを運ぶため、開始タグと部分的な子リストを一緒には畳めません。逆に部分木全体が畳み込み可能なら根の開始タグも同じ文字列に入り、完全に静的な `Body` はコンポーネント全体で `AddMarkupContent(0, …)` の1フレームに落ちます。Razorコンパイラも同じ条件で同じ形を出します(#140 が引用しているフレーム比較で差分が frame 0 ではなく frame 2 から始まっていたのは、その例の `div` が動的な子を持っていたためです)。「ラッパーは常に要素フレームとして残る」はこの条件を落とした誤りです。

畳み込み可能性は SSC(§2.3)より真に狭いことに注意が必要です。SSCはシーケンス番号を静的に割り当てられるかの分類ですが、畳み込みはノードの**値**がコンパイル時定数であることを要求します。`Span[$"Count: {Count}"]` はSSCに属しますが、値が定数でないため畳み込みの対象になりません。

畳み込み対象のタグは allow-list、すなわち curated タグ ∪ void タグ ∪ カスタム要素名から、テキストの解釈が通常要素と異なる `pre` / `textarea` / `iframe` を除いたものです。`AddContent` に渡した値はBlazorがエスケープしますが `AddMarkupContent` はしないため、テキストと属性値のエスケープは直列化器の責務になります。`Html.Raw` は畳み込みから除外します。既に1フレームであり単独で畳んでも得が無く、隣接する run へ混ぜるのは危険なためです(`Raw("<i>")` のような不均衡な文字列は、run 全体を1回でパースするときに後続の兄弟を `<i>` の内側へ入れてしまいます)。

値がマークアップを往復できない場合も畳みません。除外は4つあり、いずれも Chromium での実測に基づきます。下の2つを見れば、仕様の読解だけでは足りないとわかります。うち1つは仕様上どの段も触れない文字です。

- **復帰(CR)**。パーサはCRLFと単独のCRを、トークン化より前の入力ストリーム前処理でLFへ正規化し、これは `<template>` に対するフラグメントパースでも同じですが、`setAttribute` / `createTextNode` は正規化しません。属性値は空白の畳み込みを受けないため `getAttribute` に差が直接現れます(畳み込み経路は `"a\rb"` と `"a\r\nb"` の双方に対し `"a\nb"` を返す)。4つのうち実際に踏みやすいのはこれだけで、CRLFで取得したファイル中の逐語的文字列リテラルはこれを含みます。LFは正規化されないため畳み込み可能です。
- **NUL**。乖離の形が位置によって2つに分かれます。マークアップ経路はテキスト内容ではNULを削除し、属性値では U+FFFD へ置換します。要素経路は双方で保ちます。NULの処理は前処理ではなくトークン化と木構築に分かれて置かれており、「パーサがU+FFFDへ置き換える」という以前の記述はテキスト側で誤りでした。
- **孤立サロゲート**。乖離は無く、保守的な除外です。.NET が描画バッチをUTF-8へエンコードする時点で U+FFFD へ置き換わるため、パーサに届く前に両経路とも U+FFFD になります。仕様上も、入力ストリーム中の孤立サロゲートは parse error であってストリームの書き換えではありません。
- **先頭のU+FEFF**。HTMLのパース段はこれに触れません。ブラウザは描画バッチのフレーム文字列をデコードする際、先頭にあるバイト順マークだけを剥がします。畳み込みは値の位置を動かす操作なので、非畳み込みでは値が自身のフレーム文字列の先頭にあって剥がされ、畳み込みでは `<` で始まるより大きな文字列の内部に入って残ります。したがって除外の条件は文字ではなく位置です。この1つだけはマークアップ経路の方が原文に忠実ですが、畳み込みの契約は「両方の綴りが同じDOMを作る」ことであって、どちらが原文に近いかではありません。

定数であっても、**文字列でない値は畳みません**(#158)。`AddAttribute` に渡した非文字列値が整形されるのは、コンポーネントがフレームを組み立てている時点のカルチャではなく、後に整形するスレッドが持つカルチャに従います(実測: コンポーネントの `OnInitialized` で `CultureInfo.CurrentCulture` を変えても属性の出力は変わらず、`CultureInfo.DefaultThreadCurrentCulture` を変えると変わる)。したがって `3.5` は `en-US` で `"3.5"`、`de-DE` で `"3,5"` としてDOMへ届き、コンパイラはどちらになるかを知り得ません。畳めば片方が markup へ焼き込まれ、同じ値が「周囲が静的かどうか」で違う文字列になります。除外の代償は畳み込みの取りこぼし1回です。

例外は2つあります。**定数 `null`** は、文字列でもそれ以外でも、`AddAttribute` が属性ごと省略するため、markup 側も何も書かないことで一致します。**定数 `bool`** は整形すべきものを持たず、markup が両方の結果を厳密に表現できます: `true` は `name=""`(実測でDOM等価。prerender 出力は `=""` の無い裸の `name` を書き、これも同じDOMへパースされる)、`false` は属性そのものの省略です。要素経路は `false` に対してフレームを1つも発行しないため、フレーム数も一致します。`.Attr(name, bool)` が非文字列の唯一の綴りであるのはこの理由によります(`DESIGN.md` §4.1 と #158)。クラスチャンネルは連結で畳むため、ここでも定数文字列だけを受け付けます。

#150 はこれ以外の文字クラスを掃き、いずれも両経路で一致することを実測しました(C0制御文字、DEL、NEL、NBSP、U+2028/U+2029、BMPおよび追加面の非文字、内部のBOM、U+FFFD自身、タブ、LF、連続空白、正しい対のサロゲート)。入力ストリーム前処理で書き換えが起きるのは改行正規化だけであり、サロゲート・非文字・制御文字は parse error に分類されるだけでストリームを書き換えません。

`ForEach` の content 根は畳みません。`SetKey` はマークアップフレームへ付けられないためです((B) 参照)。これを守っているのは発行側が content 根へ渡すキーの有無であり、独立した述語を置いていないので、両者が食い違う余地はありません。吸収するフレームが1つしかない run も畳みません。形だけ変えて、何も減らないからです。

**畳み込みは出力を変えずにコード経路を変えます。** 畳み込まれたマークアップと、要素経路が `HtmlEncoder` を通して書き出す出力は `&` `<` `>` `"` について同一です(それがDOM等価性の要件そのものなので当然そうなります)。したがって**出力に対するアサーションだけでは、畳み込み経路を通ったことを示せません**。畳み込みが静かに止まっても、そのテストは通り続けます。畳み込みを検査するテストは、出力と併せてフレーム数を固定しなければなりません。#140 の実装中に実際に踏んだ形が4つあります。(1) ベンチマークのフレームゲートは「畳み込み側が厳密に少ないフレームを出す」を要求していたが、発行側が畳むようになって両辺が等値になり、ベンチマークを1本も走らせずに終わる状態になった。守るべき性質(2辺が同形であること)は変わっておらず、それを表す条件が不等号から等値に変わった。(2) prerender のエスケープ検査は、`HtmlRenderer` が通常のテキストフレームを自分でエスケープするため、畳み込みが止まっても同一のバイト列を出して通る。(3) 畳み込み側と非畳み込み側の対は、非畳み込み側が定数に戻ると黙って畳まれ、DOMは一致するのに比較対象が消える。(4) ブラウザゲート自体も、prerender された内容がそのまま表示されている間はマークアップ挿入経路を呼ばない。ハイドレーション時の差分が空ならJSレンダラは何も挿入せず、比較しているのは.NET側 `HtmlRenderer` が書いたDOMになる。いずれもフレーム数、あるいは prerender 出力がゼロであることを実測で固定して初めて、畳み込み経路を検査したことになります。

**コンポーネントの fragment スロット**: `RenderFragment` 型のパラメータは、スカラー値を持たずノードツリーを
持つため `ComponentParameter`(スカラー)とは別チャンネル(`ComponentSlot` / `ComponentSlotNode`)に
格納します。発行されるフレーム幅は `1 + Parameters.Length + Σ(1 + 内容のフレーム幅)` で、スロット1つが
`AddComponentParameter` 1回とその内容の幅を消費します。

ラムダ内部のシーケンス番号は外側の平坦なカウンタを継続し、独立したシーケンス空間を作りません。
スロットのフレームは呼び出し元ではなく**子コンポーネントのフレーム列**に属します。BlazorCodeFirst のジェネレータは
常に `AddComponentParameter(seq, "ChildContent", (RenderFragment)(...))` を発行する側です。
fragment を直接 invoke するかどうかは渡し先コンポーネント(手書きでも Razor 生成でも)が `AddContent` に
渡すか自分で呼ぶかの問題であり、前者は Blazor のリージョンが隔離しますが、後者はリージョンが張られず、
我々の番号がホスト自身のフレームと隣接します。0 から振り直すとホストの低い番号と衝突してコンポーネントが
再生成され状態が失われるため(実測)、平坦継続が厳密に安全側です。これは Razor と同一の挙動で、
リージョンで包んでも解決しません(リージョンはホストのフレーム列における隣接関係を変えないため)。

**ジェネリックな fragment スロット**: `RenderFragment<TContext>` 型のパラメータは `.Template` で受け、
`TContext` を取る外側のラムダと `RenderTreeBuilder` を取る内側のラムダを重ねた2段の式として発行します。
外側の引数は、コンテキストを使わない綴りでは破棄 `_`、コンテキストを読む綴りでは
`__bcf_context_<論理プレオーダー番号>` という生成名になります。内側は非ジェネリックのスロットと同一です。

```csharp
// 入力(設計時のC#式)
Component<Card>()
    .Param(c => c.Title, "t")
    .Template(c => c.HeaderTemplate, Span["heading"])
    .Template(c => c.RowTemplate, row => Span[$"Row {row}"])
```

```csharp
// 出力(生成コード): スカラーが先、スロットはソース順、seqは平坦に継続する
// (キャストの型名は表示の都合で短縮。実際は §2.2 のとおり global:: 修飾で書き出されます)
__b.OpenComponent<global::T.Card>(0);
__b.AddComponentParameter(1, "Title", "t");
__b.AddComponentParameter(2, "HeaderTemplate", (RenderFragment<int>)((_) => (__builder) =>
{
    __builder.AddMarkupContent(3, "<span>heading</span>");
}));
__b.AddComponentParameter(4, "RowTemplate", (RenderFragment<int>)((__bcf_context_3) => (__builder) =>
{
    __builder.OpenElement(5, "span");
    __builder.AddContent(6, $"Row {__bcf_context_3}");
    __builder.CloseElement();
}));
__b.CloseComponent();
```

チャンネルの発行順は、スカラーのパラメータがソース順で先、続いてスロットがソース順です。スロット内容の
シーケンス番号は外側の平坦なカウンタをそのまま継続し、独立した空間を作りません(非ジェネリックのスロットと
同じ規則です)。上の例の `RowTemplate` が `__bcf_context_3` を名乗るのは論理プレオーダー番号が3だからで、
自身の `AddComponentParameter` のseq(4)とは別の数です。両者が一致する保証はありません。

コンテキストの名前は生成側が決め、作者の書いた識別子は生成コードに現れません。作者のラムダ引数は
`[Composable]` の引数と同じ**穴**としてテンプレートに記録され、展開時に生成名が差し込まれます。穴の位置は
解析時にパラメータの `ISymbol` から決まるため、同じ綴りの別物(同名のフィールド、内側のラムダが再宣言した
同名の変数)は書き換わりません。ただし `ISymbol` と `TextSpan` はこの解析呼び出しの内側に閉じ、テンプレートへ
渡るのは書き換え後の文字列だけです。ジェネレータのインクリメンタルモデルは不変・値等価なレコードと
プリミティブと文字列だけで構成する必要があり、シンボルやスパンを持ち込めばキャッシュの等価判定が壊れます。

逆向きの衝突も2つ塞いであります。作者が `__bcf_context_*` という名前を自分で宣言していれば
`__bcf_authored_context_*` へ改名し、生成引数が作者の非静的メンバーを覆い隠す位置では `this.` を補います。

---

## 3. メモリレイアウト

### 3.1 SSC経路: 中間表現ゼロ

SSC(および Transplantable)経路の実行時像は、静的シーケンス定数を伴う `RenderTreeBuilder` 命令の直列実行です。生成物の形式はRazorコンパイラの出力と同じであり、UI記述に由来する中間オブジェクト(要素ツリー、ビルダー、`params` 配列)はヒープに生成されません。マーカー型 `View` は空の `readonly struct` であり、実行時に到達不能です。

SSC経路のアロケーション特性は、これにより等価なRazorコンポーネントと同等になります。`DESIGN.md` §7.1 の実測値であって、予測ではありません。残存するアロケーション源はBlazor自体に由来するものに限られます: イベントハンドラのデリゲート/クロージャ、`RenderTreeBuilder` 内部のフレーム配列(再利用される)、補間による一時文字列(`ISpanFormattable` 経路で部分的に緩和)。

### 3.2 Opaque経路: フラグメント内包 `View`

Opaque経路でのみ、`View` は実体を持ちます。この場合の `View` は `RenderFragment` への参照を内包する軽量ハンドルであり、ヒープ割り当ては内包フラグメントの構築分に限られます。コストとしては `RenderFragment` を手書きで合成した場合と同等です。

```csharp
public readonly struct View
{
    internal readonly RenderFragment? Fragment;   // SSC経路では常に null(到達不能)
    internal View(RenderFragment fragment) => Fragment = fragment;
}
```

外部由来の `RenderFragment?` を要素コンテンツとして受け取る `implicit operator View(RenderFragment?)` は、現状SSC経路しか存在しないため `=> default` を返すだけの inert な変換です。これは暫定の実装であり、Opaque経路(または付録CのDEBUG解釈モード)が実装された時点で、この節の `Fragment` フィールドを実際に構築して返す実体を持ちます。

### 3.3 静的サブツリーの畳み込み

状態に依存しないサブツリー(固定ヘッダー、利用規約等)について、Source Generatorは値がコンパイル時定数であるノードを検出し、連続する範囲を1つのマークアップ文字列へ直列化します。実行時に残るのは `AddMarkupContent` 1回の呼び出しであり、要素・属性・テキストのフレームは発行されません。値の再計算・再フォーマットが起きないだけでなく、フレーム自体が減ります。畳み込みの単位と条件は §2.7(D) に定めます。

畳み込まれなかった部分については、フレーム発行自体はBlazorの差分検知が要求するため毎回行われます。コンポーネント全体のフレーム数は、静的な部分については定数個(run ごとに1)へ、動的な部分については従来どおりノード構造に比例した数へ分かれます。

---

## 4. イベント・プロパゲーションと並行モデル

### 4.1 実行順序と単一方向データフロー

ユーザーアクションからDOM更新までは、次の順序で一方向に進む:

1. **イベント発火**(ブラウザ)
2. **ディスパッチ**: Blazor `SynchronizationContext` へのディスパッチ完了
3. **状態遷移**: `s_t` から `s_{t+1}` への更新
4. **フレーム列生成**: `RenderView` の実行による `r_{t+1}` の生成
5. **差分適用**: `Δ(r_t, r_{t+1})` のDOM同期

この順序の要点は、状態遷移がフレーム列生成に先行しなければならない(状態遷移 → 生成)という一点にあります。これは単一方向データフローの強制であり、`RenderView` の実行中に状態遷移を発生させてはならないことを意味します。現行のソースレベル実装では「設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)内での状態変更禁止」に対応し、違反は診断BCF3001となります。`Button` のonClickラムダ(`DeferredEventHandler`コンテキスト)はレンダリング中に走らず、イベント後に実行されるため除外されます。任意のメソッド呼び出し経由の副作用の完全な検出は保証しません(§1.1 BCF3001注記参照)。`[Composable]` 本体への同等の検証は将来拡張候補であり、この初期契約には含めません。

### 4.2 Blazor標準ディスパッチとの役割分担

Blazorは既に `SynchronizationContext`(および `ComponentBase.InvokeAsync`)により、レンダリングスレッドへの直列化ディスパッチを提供しています。BlazorCodeFirstはこれを置換しません。本ライブラリが並行モデルに追加するのは次の2点に限定されます。

第一に、§4.1の順序のうち「状態遷移 → フレーム列生成」のアナライザーによる静的検証(Blazor標準は規約のみで強制機構を持たない)。第二に、外部スレッドからの複数の状態変更通知を単一の再レンダリングへ合流させる、`Interlocked` ベースのロックフリー通知合流:

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

Wasm環境(現状実質シングルスレッド)ではCASが常に無競合で成功するため、オーバーヘッドは分岐1回に縮退します。

### 4.3 Runtime Async(net11.0 条件付き)

net11.0ターゲットでは、Runtime Async(ランタイムネイティブ非同期)により非同期イベントハンドラのステートマシンオーバーヘッドが低減され、スタックトレースが平坦化されます。BlazorCodeFirst側のコード変更は不要であり、TFM切替のみで恩恵を受けます。

---

## 5. WebAssemblyとAOTコンパイル適合性

BlazorCodeFirstは実行時メタデータ分析・動的ディスパッチを排除します。全パラメータバインディング(`Component<T>().Param(...)` を含む)は、Source Generatorが生成する静的セッター経由で行われます。`Param` の式引数はSGが構文解析してセッター生成にのみ利用し、式木(`System.Linq.Expressions`)のランタイムコンパイルは行いません。`System.Reflection` / `System.Linq.Expressions` へのランタイム依存は0です。

さらに、設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)と設計時API、すなわち `Html`・`Decorations` の全メンバーと設計時慣性型 `View` / `ComponentView<T>` / `ElementBuilder`(付録A、BCF3014)の全メンバーは、いずれも実行時に到達不能であるため、ILトリマーはこれらを丸ごと除去できます。UI記述のソースコードはバイナリサイズに寄与しません。実行時に評価するコードファースト方式では得られない性質です。除去は `TrimMode=full`・`ILLinkTreatWarningsAsErrors=true` を有効にした状態で、`System.Reflection.Metadata` のMethodDef走査により確認できる設計であり、トリムテストはコンポーネントとレイアウトの双方(派生型の `Body`/`Chrome` と基底の抽象ゲッター)についてこれを検査します。

リフレクションベースのバインディングを持つ同等構成との比較で、AOTコンパイル後のWasmペイロードサイズを約20〜30%削減(予測値)と見込みます。この予測値は、(a) BlazorCodeFirst構成、(b) リフレクションバインディング構成、(c) 素のRazor構成の3系統のベンチマークにより確定値へ置き換えられます。素のRazor構成との比較ではほぼ同等となる見込みです。

BlazorCodeFirstのトリミング/AOT適合契約が対象とするのは、自身が生成するコード(リフレクション不使用の`RenderView`、実行時に到達不能な設計時API、`ComponentView`ビルダー)がトリミングで除去されることまでです。`Component<T>().Param(...)` によるコンポーネント埋め込みでは、パラメータが実行時に適用される段でフレームワーク側のリフレクションベース`[Parameter]`バインダー(`ComponentProperties.SetProperties`)が到達可能になりますが、これはBlazor SDKのトリミングプロファイルが担う範囲であり、BlazorCodeFirst自体の責務ではありません。トリムテストハーネス(`tests/BlazorCodeFirst.TrimTestApp`)では、Blazor SDKのプロファイルを持たない素のコンソールアプリという性質上この1点のフレームワーク側`IL2072`が表面化するため、`ComponentProperties.SetProperties`のみに限定した抑制(`ILLink.LinkAttributes.xml`)を適用しています。

`Component<T>()` の型引数は生成コード中の `OpenComponent<T>` へリテラルとして落ちるため、BlazorCodeFirstのジェネレータが走る時点で解決している必要があります。ソースジェネレータは互いの出力が見えないため、**同一プロジェクト内**の `.razor` コンポーネントはこの条件を満たさず、BCF3012として報告されます。参照先プロジェクトやNuGetパッケージに含まれる `.razor` コンポーネントは通常どおり解決するため、この制約は同一コンパイル内に限られます。手書きのC#コンポーネントは常に利用できます。

---

## 6. .NET 11 条件付き形式定義: 閉世界 `ViewNode`(参考仕様)

net11.0ターゲットでは、C# 15のUnion型と `closed` 修飾子を用いて、Source Generatorの内部表現であるUIノード集合を閉じた判別共用体として定義します:

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

閉世界化により、コンパイラ内部のビジター(フレーム発行、依存解析、診断)の網羅性がコンパイル時に検証され(ケース漏れはコンパイルエラー)、`FrameWidth`(§2.2)の全域性が型システムで保証されます。

> 注記: Union型は.NET 11プレビュー時点で一部機能(member provider等)が未実装であり、本章はGA後に正式化される参考仕様です。net10.0ターゲットでは同等の構造を `sealed` クラス階層+網羅性アナライザーで近似します。

---

## 7. 技術適合仕様サマリー

| 評価項目                   | Blazor(通常Razor)                 | BlazorCodeFirst(本システム)                                    | 備考                                      |
| -------------------------- | --------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| 記述パラダイム             | マークアップファースト(HTML + C#) | コードファースト(純粋C#)                                     | SwiftUI/Compose と同系統の記述体験          |
| 型安全性(Style/Layout)     | 低(文字列CSS/クラス名依存)        | 完全型安全(コンパイル時検証)                                 | IDEインテリセンスが駆動               |
| コンパイル方式             | Razorコンパイラ(マークアップ→C#)  | Source Generator(C#式→C#)                                    | 生成物は同形式                            |
| シーケンス番号管理         | コンパイラによる静的割当          | SGによる静的割当(SSC)+ リージョン分離(Transplantable/Opaque) | 開発者はシーケンス制御を意識不要          |
| 実行時の中間表現           | なし                              | なし(SSC経路)/ フラグメント内包 `View`(Opaque経路のみ)       | UI記述由来のヒープ割当ゼロ                |
| GCアロケーション           | 基準                              | 同等(実測値)                                                 | `DESIGN.md` §7.1 の実測。静的サブツリーの畳み込み(§2.7(D))によりフレーム列も一致する |
| レンダリング時間           | 基準                              | 同等(未掲載)                                                 | 測定済みだが分散が機械依存のため `DESIGN.md` §7.1 は数値を掲載しない            |
| AOT / Wasm互換性           | 適合                              | 完全適合(リフレクション依存0、UI記述コードはトリム除去)      | 対リフレクション構成で20〜30%削減(予測値) |
| Hot Reload                 | ツーリングに統合済み              | EnC標準経路(メソッド本体差替+`MetadataUpdateHandler`)        | 編集後の意味論はRazorと同一(§2.6)         |
| 対応TFM                    | —                                 | net10.0(ベースライン)/ net11.0(Union型内部表現等)            | LTS優先のマルチターゲット                 |

---

## 付録A: 診断一覧

### A.0 報告経路の制約: コンパイルエラーを説明する診断はアナライザーでは報告できない

csc は宣言レベルのエラー(CS0534、CS0246、CS0234 等)を含むコンパイルに対してアナライザードライバを実行しません。アナライザーは妥当なシンボルモデルを前提とするため、これは Roslyn の標準動作です。一方 Source Generator ドライバにはこのゲートがなく、生成器が報告した診断は宣言エラーと共存して出力されます。

ここから、診断の実装先を決める規則が導かれます。

> **その診断の役割が「利用者が単独では読み解けないコンパイルエラーの原因を名指すこと」であるなら、その診断は Source Generator が報告しなければならない。** アナライザーとして実装した場合、診断が発火すべき条件そのものがアナライザードライバを停止させるため、原理的に到達不能になる。

BCF1001 はこの規則に違反していました(#76)。`partial` の欠落は `RenderView` の非生成を意味し、それは宣言レベルのエラーである CS0534 を必ず発生させるため、アナライザーとしての BCF1001 は実ビルドで一度も報告され得ませんでした。診断すべき条件が診断自身を抑止していたことになります。BCF1001 は生成器報告へ移されています。同じ理由で BCF1003 / BCF1005 は当初から生成器報告であり、CS0534 と共に出力されます。

副次的な帰結として、**宣言エラーを1つ含むコンパイルでは、そのプロジェクトのアナライザー診断が BlazorCodeFirst 以外(CA/IDE 規則を含む)もすべて消えます**。これは BlazorCodeFirst 固有の性質ではありませんが、非 partial なコンポーネントはこの落とし穴にはまる最も容易な経路であり、その意味でも BCF1001 を生成器から即座に報告する価値があります。

現行の報告経路は、BCF3001 のみ `RenderMutationAnalyzer`(状態変更を含む設計時表現はコンパイル自体は成立するため、アナライザードライバが動く)、それ以外はすべて `BlazorCodeFirstGenerator` です。新しい診断を追加する際は、その発火形状がコンパイル可能かどうかを先に判定してください。

この節の内容は文書上の約束にとどまりません。テストで固定されています。`tests/BlazorCodeFirst.DiagnosticTests` が `tests/diagnostic-fixtures` の各プロジェクトを実 MSBuild でビルドし、SARIF ログから「どの診断が、どの位置に報告されたか」を検証します。同一の CA1050 違反型を全フィクスチャに含めることで、宣言エラーのあるコンパイルではアナライザー診断が消えること・ないコンパイルでは報告されることの両方が固定されており、`DiagnosticDescriptors` の全記述子はこの層で網羅されているか、理由付きの除外リストに載っているかのいずれかである必要があります。

次節の表そのものも同じテストプロジェクトが検証します。`DiagnosticTableTests` が A.1 の表を読み取り、`DiagnosticDescriptors` と双方向で突き合わせます。記述子があって行が無ければ失敗し、行があって記述子が無い場合も、実装に先行して仕様化されている理由を `DiagnosticExpectations.DocumentedWithoutDescriptor` に記録していない限り失敗します。その登録は実装時に記述子と入れ替わり、入れ替え漏れは「理由を失った例外」として別のテストが落とします。種別列も記述子の `DefaultSeverity` と照合されるため、診断の severity を変えることは表を変えることでもあります(記述子を持たない行は照合対象外です)。

### A.1 診断一覧

| ID     | 種別    | 内容                                                                                  |
| ------ | ------- | ------------------------------------------------------------------------------------- |
| BCF1001 | Error   | 設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)の override を宣言するクラスが `partial` として宣言されていない(同一クラスへ `RenderView` を生成できない)。BlazorCodeFirstベースを継承するだけで override を宣言しないクラス(中間abstract基底、基底が既に宣言している葉、再abstract化)、および `RenderView` を手書きしているクラス(生成物が無いため `partial` は不要)は対象外。ネストクラスは BCF1005 が優先する(`partial` を足しても解決しないため)。生成器が報告する(理由はA.0)  |
| BCF1002 | Error   | `[Composable]` の静的展開が成立しない。宣言位置では、Source Generatorのサポートする静的展開契約を満たさない場合に報告する(拡張メンバー(`DESIGN.md` §4.3、#203)、非静的、ジェネリック、ジェネリック型に含まれる、式本体でない、`View` を返さない、`params`・参照渡し・`View`・`ElementBuilder` 型のパラメータ、生成コードから名指せない型のパラメータ、静的にシーケンス可能でない本体)。呼び出しサイトでは次の3条件で報告する。(1) 当該メソッドのソース宣言が現コンパイルに無い(メタデータのみ)。定義は現コンパイルの構文から `ForAttributeWithMetadataName` で収集され、ILは本体構文を持たないため、参照先プロジェクトやNuGetパッケージの `[Composable]` は常にこれに当たる。(2) 再帰的な展開がサイクルを形成する。(3) 本体が参照する `private` / `protected` メンバーへ展開先から到達できない |
| BCF1003 | Error   | 設計時表現(`Body` / `Chrome`)が静的にシーケンス可能な部分集合へ分類できず、実行時フォールバックも未実装のため `RenderView` を生成できない。Opaque/Transplantable 経路の実装により発火条件は縮小する(過渡的) |
| BCF1004 | Error   | 設計時表現(`Body` / `Chrome`)の override が、ジェネレータの翻訳できないゲッターを宣言している(文を含むゲッター、または本体を持たない自動プロパティ)。`=> expr` / `get => expr` / `get { return expr; }` のいずれかに書き直すか、`RenderView` を手書きする。再abstract化(`abstract override`)は対象外。実装部を持たない partial プロパティも対象外(CS9248 が原因を名指す) |
| BCF1005 | Error   | ネストしたクラスが設計時表現を宣言している。生成コードは外側の型宣言の連鎖を再現できないため、トップレベルの型へ移す必要がある |
| BCF2001 | Info    | Opaque構文を検出。動的リージョンへ縮退し、当該領域の静的差分最適化が失われる(将来の対象範囲: `AddContent(seq, RenderFragment?)` を発行する `RenderFragmentContentNode` は仕様上のOpaque経路であり、BCF2001実装時の対象に含まれる想定。未実装。なお #32 の `ComponentSlot` は `AddComponentParameter` と静的採番済みラムダのみで構成される完全なSSC経路であり、BCF2001の対象ではない。名前が似ている `RenderFragmentContentNode`(Razor→BlazorCodeFirst 方向)とは逆向きの構文である) |
| BCF3001 | Error   | 現行実装では設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)本体内での状態変更(単一方向データフロー違反)。初期検出範囲: コンポーネントインスタンスメンバーへの直接書き込み(代入/複合代入/インクリメント/デクリメント)。遅延ハンドラ引数(入れ子ラムダを含む)内は除外。除外対象はイベントデコレーション(`.OnClick` 等のイベント短縮形と `.On`)のハンドラ引数と `.Bind` のセッター引数であり、これは名前の列挙ではなく `KnownSymbols` の分類そのものから導かれる。`.Bind` のゲッター引数はフレーム生成中に評価されるため除外しない。任意の副作用の完全検出は保証しない。`[Composable]` 本体への適用は将来拡張候補 |
| BCF3002 | Warning | `ForEach` の `key` セレクタが要素の恒等性を保証しない可能性(インデックスベースキー等) |
| BCF3003 | Error   | `ForEach` の `content` が単一の要素/コンポーネントを根に持たず、キーを適用できない(根がリージョンになる裸の `if`/`ForEach`、`Fragment`、`Raw` 等)。内側を容器要素で包む(例: `Div[...]`)必要がある |
| BCF3004 | Error   | `ForEach` の `content`/`key` がインライン式ラムダでない(ブロック本体ラムダ/メソッドグループ等)ため静的解析できない |
| BCF3005 | Error   | `Component<T>()` のパラメータ束縛(`.Param` / `.Template` / `.Bind`)のセレクタが単純なプロパティ選択(`c => c.Prop`)でない(キャスト/メソッド呼び出し/捕捉変数のメンバー等) |
| BCF3006 | Error   | `Component<T>()` のパラメータ束縛(`.Param` / `.Template` / `.Bind`)の対象が settable な `[Parameter]` プロパティでない(実行時 throw を防ぐためコンパイル時に拒否) |
| BCF3007 | Error   | `Component<T>()` のチェーンが同一プロパティを複数回バインドしている。`.Param` / `.Template` / `.Bind` と角括弧の子コンテンツのすべてを数える(Blazorは最後の値のみ適用するため重複はコンパイル時に拒否) |
| BCF3008 | Error   | 装飾(`.Class`/`.Attr`/型付き属性ショートカット/`.OnClick`/`.On`)が単一要素を開くノード(要素ヘルパ/`Element`)以外に書かれている。装飾は `ElementBuilder` の拡張であるため、レシーバが `View`/`ComponentView<T>`(`If`/`ForEach`/`Fragment`/`Raw`/`[Composable]`結果/`Component`、および子を与え終えた要素)の場合は `Decorations` に対するオーバーロード解決が失敗する。外部から渡された `RenderFragment` もレシーバとして受理する。`View` へ暗黙変換されるものの、拡張メソッドのレシーバは恒等/参照/ボクシング変換しか取らずユーザー定義変換を適用しないため、同じく解決に失敗し、作者の誤りは `Fragment`/`Raw` を装飾した場合と同一である。翻訳に失敗した設計時表現を走査し、この失敗したチェーンを検出して報告する(型システムが挙げるCS1929は宣言段階の打ち切りにより作者へ届かないため。§2.2) |
| BCF3009 | Error   | `Element` のタグ引数が非空のコンパイル時定数文字列でない(宣言性・予測可能性のため) |
| BCF3010 | Error   | 同一要素上で属性またはイベントが複数回バインドされている(属性チャネル内の重複は後勝ちで前が死に、属性チャネルとイベントチャネルにまたがる同名バインディングは両方が生き残って二重発火する。いずれも書いたとおりにならないため拒否)。畳み込まれる `class` のみ例外で、その例外に収まらない `.Bind("class", …)` との共存はBCF3024が見る |
| BCF3011 | Error   | `.Attr` の名前 / `.On` のイベント名 / `.Bind` の属性名とイベント名が非空のコンパイル時定数文字列でない(宣言性・タイポ検査・class畳み込み判定・重複検出の前提) |
| BCF3012 | Error   | `Component<T>()` の型引数がジェネレータ実行時に解決できない。同一プロジェクト内の `.razor` コンポーネントはRazorコンパイラ自身がソースジェネレータであるため相互に出力が見えず、常にこの状態になる。参照先プロジェクト/NuGetパッケージの `.razor` と手書きC#コンポーネントは正常に解決する。タイポや `using` 漏れの場合は同じ位置に CS0246 も報告される |
| BCF3013 | Error   | `Component<T>()[…]` で子コンテンツが与えられているが、`T` がそれを受け取れる `ChildContent`(settable な `[Parameter]`、非ジェネリック `RenderFragment`)を持たない |
| BCF3014 | Error   | 設計時慣性型(`View` / `ComponentView<T>` / `ElementBuilder` / `ContentView`)がジェネリック `.Param` の値位置に渡された |
| BCF3015 | Error   | body 内の値式で、生成コードへ安全に移植できない未解決の型参照 |
| BCF3016 | Error   | void要素に子が与えられている。対象はHTML Living Standardのvoid elements 13要素(`area` / `base` / `br` / `col` / `embed` / `hr` / `img` / `input` / `link` / `meta` / `source` / `track` / `wbr`)で、curatedヘルパーと、タグを非空の定数で受けた `Element` の双方を見る。静的SSRは閉じタグを出力し、HTMLパーサが子を兄弟へ押し出すため、prerenderとinteractive描画で異なるDOMになる(理由と計測は `DESIGN.md` §4.1)。要素タグについての単項述語で判定するため、(親, 子) で決まる同種の破れは対象外。未知タグとカスタム要素も対象外 |
| BCF3017 | Error   | `.Bind` の getter が本体式を持つインラインラムダでない(ブロック本体ラムダ/メソッドグループ等)。getter の本体式は属性値と `CreateBinder` の現在値の双方へ移植されるため、式として取り出せなければならない。setter 側にこの制約はない(`EventCallback` へ渡すだけで本体を取り出さないため) |
| BCF3018 | Error   | getterだけを渡す形の `.Bind` で getter の本体が代入可能でない。許可されるのはメンバーアクセス(`_name` / `_form.Name` / `Model.Items[0].Title`)と要素アクセス(`_dict["k"]`)で、対象が setter を持つこと。呼び出し・演算(`() => _name.ToUpper()`)、get-only プロパティ、`readonly` フィールドは拒否する。ローカル変数・パラメータ・`ForEach` の反復変数そのものへの直接代入も拒否する(`Body` はプロパティゲッターでありローカルはレンダリングごとに死ぬため、書き戻しが次のレンダリングに残らない)。反復変数の**メンバー**(`o.Title`)は元の要素を書き換えるので許可する。setter を明示する形へ誘導する。要素とコンポーネントの双方で発火し、同じ形でも引数の個数は面によって違う(要素は3と4、コンポーネントは2と3)ため、形の呼び分けに個数を使わない |
| BCF3019 | Error   | `.Bind` / `.On` のイベント名が `on` で始まらない。Blazor のイベント属性名は常に `on` で始まり、そうでない名前は属性として静かに追加されてハンドラが一度も発火しない。`.Bind` は属性名とイベント名の2つの文字列を隣り合って取るため、取り違えがこの検査で止まる |
| BCF3020 | Error   | `ComponentView<T>.Bind` の対象に対応する `{名前}Changed` パラメータが `T` に無い、または `EventCallback<TValue>` でない。要素側と違いコンポーネント側は名前を導くが、導けるのは型シンボルで確かめられるからであり、`{名前}Changed` は存在と型が合わなければこの診断で拒否する。もう一方の `{名前}Expression` はこの診断の対象ではなく、宣言されていて型が合うときにだけ発行し、そうでなければ無言で省く(Razorと同じ挙動。宣言しない型に対して常に発行すれば束縛自体が失敗するため) |
| BCF3022 | Error   | `Component<T>().Template` の文脈付きオーバーロード(`Func<TContext, View>` を取る形)の content がインライン式ラムダでない(メソッドグループ/匿名メソッド/ブロック本体ラムダ等)。生成器はシーケンス対象の式と、生成する文脈変数を代入するパラメータシンボルの双方を必要とするため、いずれも取り出せない形は拒否する。位置は content 引数の全体で、書き直す対象が引数の形そのものだからである。引数が0個または2個以上のラムダはこの規則の対象外で、`Func<TContext, View>` へ変換できずC#が先に拒否する。BCF3004 と同じ制約を `ForEach` ではなくテンプレートに置いたもの。番号が BCF3021 を飛ばしているのは、BCF3021 が撤回済み(付録B.5)で再利用しないためである |
| BCF3023 | Error   | `.Attr("class", …)` が `bool` オーバーロード(#158)で書かれている。`class` はクラスチャネルへ畳み込まれ、このチャネルは装飾を1つの値へ連結するため、`bool` はそこで意味を持たない。しかも意味が1つに定まらない。要素が持つクラス装飾が1つならチャネルは値をそのまま出すため `AddAttribute(int, string, bool)` が束縛され、`true` は `class=""` すなわちクラス一覧の消去になる。2つ以上なら `+` で連結するため同じ `true` が文字列化され `class="a True"` になる(いずれも実測、#159)。同じ綴りがチェーンの別の場所にある個数で二通りに翻訳される、生成器自身の畳み込みから生じた翻訳の破れである。対象は名前が `class` の場合だけで、`.Attr("disabled", flag)` は `bool` オーバーロードの本来の用途であり対象外。位置は値引数で、書き直す対象がそちらだからである(条件付きクラスは文字列側の条件式、`.Class(active ? "on" : null)` として書く)。値を書かない綴り `.Attr("class")` も同じ規則に届く。裸の綴りは存在を表すが、チャネルはテキストとして連結するため存在には連結すべきものが無い(#178)。指す値引数が無いため、この場合の位置は装飾名である |
| BCF3024 | Error   | クラスチャネルへの装飾(`.Class` / `.Attr("class", …)`)と、属性名が `class` の `.Bind` が同じ要素に載っている。チャネルは装飾を何個持っても1フレームへ畳み込むが、`.Bind` はそこへ加わらず束縛ループから自分のフレームを出すため、要素は `class` 属性を2つ持って発行される。BCF3010が唯一通す名前に届いた重複であり、その例外はチャネルが畳み込むことで買ったものであるから、名前ではなくチャネルに対して問う。`class` に届く3つ目の綴りである `.Bind` だけが畳み込まないので、この名前の他のすべての装飾と衝突し、それ以外とは衝突しない(#188)。どちらのフレームが残るかは規定しない。prerenderのマークアップではHTMLパーサが先勝ちで解決し、interactive描画ではDOMへの後勝ちの書き込みになるため、答えが1つでないからである。報告に必要な事実はそこではなく、両方のフレームが欲しかったと読める書き方が無いことである。位置は後から書かれた側の装飾名で、BCF3010と同じく検査が走る装飾を指す |
| BCF3025 | Error   | `Slot` が、呼び出し側のコンテンツを受け取らない宣言の中に書かれている。または、コンテンツを取ると宣言した `[Composable]`(戻り値 `ContentView`)が `Slot` を1回以外の回数だけ書いている。`Slot` は呼び出し側が角括弧で与えたコンテンツを置く位置の印であるから、置くべきコンテンツが存在しない場所では意味を持たない。コンポーネントの `Body`/`Chrome` は角括弧を受け取らず、`View` を返す `[Composable]` は角括弧なしで呼ばれる。0個は呼び出し側が渡す義務のあるコンテンツを捨て、2個は1つの角括弧から2回発行するため、いずれも書いたとおりにならない。位置は、置き場所の誤りは `Slot` 自身、個数の誤りは宣言の識別子で、いずれも作者が直す対象を指す。この表層で新設が必要な診断はこれ1つだけである。角括弧の書き忘れ(`Div[Card("x")]`)、装飾(`Card("t").Class("x")`)、#176が退けた位置引数の綴り(`Card("t", P["x"])`)はいずれもC#が先に拒否する。`ContentView` が `View` への変換を持たないためであり、`Div["x"].Class("y")` がCS1929である仕組みと同じである(#34, #176) |

## 付録B: 検討した代替アーキテクチャと不採用理由

**B.1 Interceptor方式(C# 14)**: `Body` を実行時に評価し、各設計時API呼び出しサイトをInterceptorで静的シーケンス付き実装へ置換する方式。呼び出しサイト置換自体は成立するが、(a) 実行時評価を前提とするため装飾チェーンの合成型に対する統一戻り値型が構成できない(C#に不透明戻り値型が存在せず、`ref struct` はインターフェースへ変換できない)、(b) `[InterceptsLocation]` の位置指定子がソース変更のたびに再計算され、ビルドパイプラインが位置データに敏感になる、(c) 本方式(全体生成)が採用可能である以上、部分置換に固有の利点がない、の3点により採用しませんでした。

**B.2 ランタイム `ref struct` ツリー方式**: 要素を `readonly ref struct` としてスタック上に構築し、実行時に `Render` を再帰呼び出しする方式。GC回避には有効だが、(a) 可変個の子要素を受け取る手段がない(`ref struct` は配列・`params` に格納不可、ジェネリックオーバーロードはアリティ上限を持つ)、(b) B.1と同じ戻り値型問題、(c) 静的サブツリーのキャッシュと両立しない(`ref struct` はフィールド格納不可)、により採用しませんでした。本方式(生成コードによる直接発行)は、同じゼロアロケーション特性を型システム上、無理なく達成します。

**B.3 `ChromeLayoutBase` を `BodyComponentBase` から派生させ `SetParametersAsync` で介入する方式**: レイアウトを通常のBlazorCodeFirstコンポーネントと同じ基底型に載せ、Blazorが渡す `Body` パラメータを `SetParametersAsync` で抜き取ってから残りのパラメータを基底へ転送する方式。当初はこの案を採る判断をしていましたが、実装して実行した結果、成立しないことが確認されたため撤回しました。残りのパラメータを転送する唯一の公開手段は `ParameterView.FromDictionary` です。ところがその列挙子は `cascading: false` を固定値で返すため、cascading値のみを受け取るプロパティに対して `ComponentProperties.SetProperties` が例外を投げます(*"The property 'X' … cannot be set explicitly because it only accepts cascading values."*)。影響は `[CascadingParameter]` に限りません。この検査は `CascadingParameterAttributeBase` を基準とするため `[SupplyParameterFromQuery]` も同じ理由で落ち、認証テンプレートが標準で用いる `[CascadingParameter] Task<AuthenticationState>` もレイアウトで受け取れなくなります。加えてナビゲーションごとに `RenderTreeFrame[]` を確保します。採用した方式(`ChromeLayoutBase : LayoutComponentBase`)は、Blazorが名前で要求する `Body` を正しい名前のまま継承し、`SetParametersAsync` に付与された `[DynamicDependency]` トリマーヒントもそのまま引き継ぐため、プラットフォームのパラメータ結線と競合しません。教訓として、プラットフォーム側のパラメータ結線に介入する方式は本設計では採りません。

**B.4 `[Composable]` メソッドに `〜AsFragment` 兄弟メソッドを併生成する方式**: 各 `[Composable]` に対し `RenderFragment` を返す静的メソッドを生成し、既存の `.razor` から `@Widgets.StatusBadgeAsFragment(status)` の形でコードファーストUIの断片を埋め込めるようにする方式。`DESIGN.md` §6.1 と `CONTRIBUTING.md` の不変条件が当初これを約束していましたが、実装されたことは一度もなく、#144 で撤回しました。理由は4点です。(a) この方式が満たそうとした要求は、コンポーネント粒度ですでに満たされています。`.razor` からBlazorCodeFirstコンポーネントをタグとして名指すことに同一プロジェクト制限はなく、`site/BlazorCodeFirst.Site/App.razor` が現にそうしています。Razorが解決するのは作者が書いたクラス名であり、生成物は `RenderView` の本体だけだからです。(b) 生成される兄弟メソッドは実体を持つため参照元アセンブリから呼べてしまい、「静的展開は宣言のソース構文を要するため同一コンパイル内に限られる」という `[Composable]` の境界(§4.3、BCF1002)に例外を作ります。同一の属性が「呼び出しサイトへ展開される同一コンパイル内の仕組み」と「公開APIを生やす宣言」という二つの顔を持ってしまい、`[Composable]` と `Component<T>()` の使い分けを説明できなくなります。(c) 実装は、含有型への `partial` 要求(現行の `[Composable]` にはなく、`site/BlazorCodeFirst.Site/Pages/NotFoundContent.cs` は非partialの `static class` です)、`〜AsFragment` の名前衝突に対する診断、`private` な `[Composable]` に対する無用な兄弟の扱いを新たに必要とします。さらに、同一プロジェクトの `.razor` が生成された静的メソッドを呼べるかは未検証です。これはBCF3012を生んだのと同じ「ソースジェネレータは互いの出力が見えない」領域にあり、不成立なら本方式は参照先アセンブリからしか使えず、その場合は(a)のコンポーネント経路が常に優ります。(d) 得られるのはコンポーネントより細かい断片粒度の埋め込みのみで、代替手段は `BodyComponentBase` で包むクラス1つです。教訓として、再利用の単位も相互運用の単位もコンポーネントとし、`[Composable]` は同一コンパイル内の分割手段に徹します。

**B.5 同一要素の2つ目の `.Bind` をBCF3021で拒否する方式**: 1つの要素に双方向束縛が2つ以上現れたら、2つの名前がいずれも空いていてもコンパイルエラーとする方式。#71で実装して出荷しましたが、#162で撤回しました。根拠としていたのは「`SetUpdatesAttributeName` の記録先は要素であり、2つ目の束縛が1つ目の再同期先を上書きする」という主張です。この主張は#71自身の最終レビューで誤りと指摘されましたが、指摘は解消されないまま規則だけが出荷されました。#162で実測した結果は次のとおりです。`SetUpdatesAttributeName` が名前を書くのは要素ではなく直前の属性フレームです。生成コードは束縛ごとに属性フレーム・イベントフレーム・`SetUpdatesAttributeName` の順で出すため、ここでいう直前の属性フレームはその束縛自身のイベントフレームであり、読み戻す `RenderTreeUpdater.UpdateToMatchClientState` が見るのもイベント自身のフレームです。つまり書き込み先と読み出し元は同一のフレームであり、そのフレームが束縛ごとに別であるため、同一要素の2つの束縛は互いの再同期を壊しません(§2.7(A))。残る選択は、別の根拠を立て直して規則を維持するか、規則を落とすかでした。落としたのは `DESIGN.md` §4.1 の原則によります。この表層が検査するのは妥当性ではなく翻訳の破れであり、2つの束縛の背後に破れはありません。Blazorはこの形を通常の差分検知で正しく描き、動機となる形も実在します(双方向のプロパティを2つ以上持つWeb Component、`DESIGN.md` §4.1)。同じ原則が#132と#133の計測済みの残余を未検査のまま置いている以上、何も破らない形だけを拒否する位置は取れません。撤回は欠番の解放ではありません。プレビュービルドでこのエラーに当たった読者が番号で検索したとき、別の規則が同じ名前を着ていてはならないためです。`AnalyzerReleases.Shipped.md` が空である以上 `CONTRIBUTING.md` のID再利用禁止はこの番号に届かないので、`DiagnosticExpectations.RetiredIds` と `DiagnosticTableTests.RetiredIds_AreNeitherDeclaredNorDocumented` が、BCF3021が記述子にも付録Aにも戻らないことを機械的に固定します。教訓として、プラットフォームの挙動についての主張を根拠に置く診断は、その挙動を実測してから出荷します。根拠への指摘を解消しないまま出せば、指摘のほうは記録に残らず規則だけが残ります。

**B.6 void性を `ElementBuilder` の型で表現する方式**: void要素13タグのcuratedヘルパーが、インデクサを持たない `VoidElementBuilder` を返す方式。`Img["child"]` はBCF3016ではなくCS0021になり、表層はHTMLに居場所のない形を差し出さなくなります。§4.1の系譜のうち3つがこの経路を採っています。Giraffe.ViewEngineの `XmlNode` は `VoidElement` ケースを持ち、`br []` がリストを1つ取るのに対し `div [] []` は2つ取ります。Falco.Markupは `ParentNode` と `SelfClosingNode` に分け、`_hr [ _class_ "divider" ]` と書きます。TyXMLは多相バリアントの内容モデルに符号化しています。#179で検討し、採用しませんでした。理由は4点です。(a) 得られるのは形だけです。どちらも今日すでにコンパイルエラーであり、BCF3016はこの誤りのために書かれた文面を持つのに対し、CS0021は「インデクサを適用できない」としか言いません。表層は読みやすくなり、診断は読みにくくなります。(b) コストは `Decorations` に落ちます。装飾は22個すべてが `ElementBuilder` を受けて `ElementBuilder` を返す形であり(`Decorations.cs`)、チェーンを通してvoid性を保つには、void型のために全体を複製するか、自己参照制約を持つビルダーインターフェースで全体をジェネリックにするかのいずれかを要します。どちらも大きく、しかも新しい装飾が必ず触るファイルに払われるため、#156と#178がそれぞれ高くつきます。(c) 検査は消えません。`Element("br")["x"]` は文字列経路で同じタグに達し、そこには変えるべき型が存在しないため、BCF3016はいずれにせよ必要です。型が覆うのはこの検査のcurated側の半分だけになります。§4.1は両経路が単一のタグ文字列に落ちてから同じ表を引くことで構成上一致すると述べており、片方の経路しか覆わない型規則はその逆の配置です。(d) ミラーとしての論拠は見かけより弱いものです。`DESIGN.md` §4.1が引く境界はタグ単独から決定できるかであり、void性はその内側にあります。設計はこれを型の領分から外し、検査の領分として扱っています。#179は#132・#133と同じ意味での記録であり、再検討には上の4点が答えていない理由を要します。

## 付録C: 開発時フォールバック案(解釈モード)

§2.6のツーリング検証で、特定環境においてSource Generatorの再実行がEnCに反映されないと判明した場合に限り、次のDEBUGビルド限定フォールバックを導入する余地を残します。

DEBUG構成では、設計時API群を慣性実装から実働実装(`View` に `RenderFragment` を構築して内包する)へ条件コンパイルで切り替え、`RenderView` の代わりに `Body` を実行時評価します。全体は単一のリージョン内で動的シーケンスを用いて描画されます。Hot Reloadは `Body` プロパティ本体の差し替え(EnC標準サポート)として自然に機能し、SGの再実行に依存しません。RELEASE構成では本仕様の生成コード経路のみが用いられるため、出荷物の性能・サイズ特性に影響しません。

本案は開発時と実行時で描画経路が二重化する複雑性を伴うため、§2.6のツーリング確認で必要性が示されるまで導入しません。
