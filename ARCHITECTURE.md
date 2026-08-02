# BlazorCompose Architecture

**内部アーキテクチャ — コンパイルアルゴリズム、シーケンス割当、メモリレイアウト**

前提環境: .NET 10(ベースライン)、.NET 11(条件付き機能)

> 背景・目的・使い方の概要は `DESIGN.md` を参照。

---

## 0. 表記と前提

記号を用いるのは、シーケンス番号の安定条件(§1.2)という本設計の中核を厳密に述べる箇所に限ります。そこでは集合・写像の素朴な記法(`f : A → B` は写像、`|X|` は要素数)を用います。それ以外の箇所は通常の文章で記述します。

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

Source Generatorはビルド時に、設計時のUI式を「状態を受け取ってフレーム列を返す関数」(型でいえば `S → R`)へコンパイルします。実行時に動くのはこの生成関数だけであり、`r_t` はそれを状態 `s_t` に適用した結果です。UI式そのもの(設計時の構文的実体)は実行時には評価されません。Razorとの対比で言えば、Razorコンパイラはこの入力をマークアップとして受け取り、BlazorComposeはC#式として受け取る、という違いです。

生成された関数は純粋(状態のみに依存し副作用を持たない)であることを規約とします(単一方向データフロー、§4.1)。設計時表現(`ComposeComponentBase.Body` または `ComposeLayoutBase.Chrome`)内の状態変更は診断BC3001の対象となります。BC3001の初期検出範囲はコンポーネントのインスタンスメンバーへの静的識別可能な直接書き込み(フィールド代入、プロパティ代入、複合代入、インクリメント/デクリメント演算子)に限ります。`Button` のonClickラムダ(`DeferredEventHandler`として分類)内の変更はレンダリング後に実行されるため除外されます。任意のメソッド呼び出し経由の副作用(非同期連鎖等)の完全な検出は初期スライスでは保証しません。

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

本方式では `σ` はビルド時にSource Generatorが構成し、生成コードへリテラル定数として埋め込まれるため、条件(2)は構造的に満たされます。対照的に、ランタイムインクリメント方式(`seq(n) = 生成順序`)は、条件付きレンダリングや要素挿入により `π` と生成順序の対応が崩れた時点で条件(1)に違反し、一致すべきフレーム以降のサブツリー全体が「削除+新規挿入」と誤判定されます(計算量が最悪 O(n) のサブツリー再構築へ劣化)。これに伴い、再構築されたコンポーネントの内部状態(入力中のテキスト等)が消失します。

---

## 2. コンパイルアルゴリズム

### 2.1 全体パイプライン

```
[ユーザーコード]                     [Source Generator]
partial class C :                    ① partial検証・Body発見
ComposeComponentBase                 ② SSC分類(§2.3)
  View Body => …        ──AST──▶    ③ DFS順シーケンス割当(§2.2)
  [Composable] View F() => …         ④ RenderView(RenderTreeBuilder) の生成
                                        — 静的seq定数の埋め込み
                                        — 動的式・ラムダの構文移植
                                        — [Composable] のインライン展開
```

生成物は同一partialクラス内の `RenderView` オーバーライドであり、基底クラス `ComposeComponentBase` の `BuildRenderTree` から呼び出されます。`Body` プロパティおよび設計時API — `Html`・`Decorations` の全メンバー、および設計時慣性型 `View` / `ComponentView<T>` / `ElementBuilder`(付録A、BC3014)の全メンバー — はいずれも実行時に到達不能であり、AOTビルドではILトリマーが除去します。除去は `System.Reflection.Metadata` によるMethodDef不在検査をもって確認できる設計であり、その確認手段はトリムテストが担います。

設計時表現のゲッターは**単一の式に還元できなければなりません**。`=> expr` / `get => expr` /
`get { return expr; }` の 3 つの綴りは同一であり、いずれも同じ `RenderView` を生成します。文を含む
ゲッター(例: return の前のローカル変数宣言)は Transplantable 経路の領域であり未実装のため、BC1004 と
して報告されます。自動プロパティは翻訳対象となるゲッター本体を宣言しないため、これも BC1004 となります
(再abstract化 `abstract override` および実装部を持たない partial プロパティは対象外、後者は CS9248 が
原因を名指します)。設計時表現は実行時に評価されない不活性な構文であり、この制約は「式を静的に翻訳する」
という前提そのものです。

設計時表現の代わりに `RenderView` を手書きでオーバーライドすることは合法であり、SSC部分集合で表現できない
ボディのためのエスケープハッチです。この場合ジェネレータは何も生成しません(生成すると同名メンバーの重複で
CS0111 になり、著者は自分のコードを消すしか手がなくなります)。設計時表現は未使用となり、BC1004 も報告され
ません。

Composeコンポーネントとして認識される宣言形状は、トップレベルの `partial class` です。ジェネリック
(`partial class Foo<T>`)はサポートされ、生成部は同じ型パラメータ名を再掲します(制約句は再掲しません。
制約は型パラメータに属するため一方の宣言にあれば十分です)。ネストした型は BC1005 で拒否されます。
`record` は `object` または別の `record` しか継承できないため(CS8864)、Composeコンポーネントにはできません。

### 2.2 シーケンス割当

`Body` の式ツリー `e` を深さ優先(preorder)で走査し、各UIノードに互いに素なシーケンス区間を予約します。`counter` はソースコード上の絶対オフセットではなく、構文ツリーの論理的な preorder 走査順で割り振られる整数(preorder 序数)です。これにより、コメントや空白の変更がシーケンス番号の安定性に影響しないことが保証されます。

```
procedure Compile(e: ExpressionTree, model: SemanticModel) → RenderView:
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

`FrameWidth` はシーケンス引数を消費する `RenderTreeBuilder` 呼び出し数のみをカウントし、`CloseElement`・`CloseRegion` のようにシーケンス引数を持たない呼び出しは含みません。ノード種別ごとに静的に定まります(例: 子を持たない `Span` = 1 [`OpenElement`]、文字列子を1つ持つ `Span`(`Span["..."]`)= 2 [`OpenElement` + `AddContent`]、onclick属性1個付き `Button` = 3 [`OpenElement` + `AddAttribute` + `AddContent`])。装飾チェーンのうち `class` は親要素の `class` 属性へ静的に合成されるため、`.Class` の追加はフレーム数を増やしません(`.Class("a").Class("b")` は単一の `AddAttribute` に畳み込まれます)。`class` 以外の属性・イベント装飾(`.Href` / `.Attr` / `.OnClick` / `.On` 等)はそれぞれ1装飾につき1フレームが追加されます(詳細は§2.7(A))。動的引数(補間文字列、状態参照、イベントラムダ)は評価されず、構文として `EmitFrames` の出力へ移植されます。同一partialクラス内に生成されるため、`this` 経由のprivateアクセスは保存されます。

値式を生成コードへ移植するとき、解決済みの型名は `global::` から始まる完全修飾名へ正規化します。未解決の型名は、元ファイルの `using` や名前空間に依存する表記のままでは安全に移植できないためBC3015とします。ただし、作者が `global::` から記述した型参照は字句コンテキストに依存しないので通常のC#の名前解決に委ねます。ジェネリック型の外側と各型引数は独立に判定します。

`Html.Fragment`(ラッパーレスなグルーピング)は自身のフレームを開かないため、その `FrameWidth` は子ノードの `FrameWidth` の総和です(ローカル変数を持たない `[Composable]` 展開ノードと同型)。`Html.Raw`(信頼済み生HTML注入)は `AddMarkupContent` を1回発行するだけの単一フレームで、`FrameWidth` = 1 です(子を持たない文字列コンテンツノードの `AddContent` と同型)。いずれも要素/コンポーネントのフレームを開かないため、`ForEach` の `content` の根には使えず(BC3003)、装飾もできません(BC3008、詳細は§2.7(A)と付録A)。この装飾不可は型システムでも表現されています — 装飾は `ElementBuilder` の拡張であり、`Fragment`/`Raw` は `View` なのでCS1929です — が、その上でBC3008も報告します。設計時表現が翻訳できないコンポーネントには `RenderView` が生成されず、クラスは必ず宣言段階エラーのCS0534を負うため、`csc` はメソッド本体の束縛へ進まずCS1929を作者へ届けません(実MSBuildでの測定値 — `RejectedDecorationScanner` が存在しなかった時点: フィクスチャ `Bc3008Host` が報告したのはCS0534とBC1003だけで、CS1929は現れませんでした。BC3008を報告するようになった現在は、同じフィクスチャがそれも報告します)。同じビルドでBC1003が届いていることが示すとおり、この打ち切りを越えられるのは生成器の診断だけであり、何が間違っているかを名指せる診断はBC3008です。

### 2.3 静的シーケンス可能サブセット(SSC)

任意のC#コードに対して条件(2)の `σ` を構成することはできません(呼び出しグラフが実行時にのみ確定するため)。解析の適用範囲を次の3階層に分類します:

**SSC(完全静的)** — 静的シーケンス割当の対象:
- SSC-1: `Body` 本体、および `[Composable]` メソッド本体における、要素ヘルパー/装飾の直接記述、および `Component<T>()`・`Fragment`・`Raw` の直接呼び出し
- SSC-2: `If(cond, then, otherwise)` コンビネータ(両分岐がインラインラムダであること)
- SSC-3: `ForEach(source, key, content)` コンビネータ(`content` がインラインラムダ、`key` は必須)
- SSC-4: SSC-1〜3の任意のネスト、および `[Composable]` 呼び出しの静的インライン展開

**Transplantable(構文移植)** — ネイティブ `if` / `foreach` / `switch` 等の制御構文。生成コードへ構文ごと移植され、境界リージョンで包まれます(§2.5)。

**Opaque(実行時評価)** — `[Composable]` の付かない `View` 返却メソッド呼び出し、デリゲート経由の間接呼び出し等。SGは内部を解析できないため、呼び出し式を生成コードへ移植し、実行時に返された `View` に内包される `RenderFragment` をリージョン内で描画します。診断BC2001(Info)で通知されます。

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

`ForEach`(SSC-3)は `foreach` へ展開され、テンプレート `content` に単一の静的シーケンス空間を割り当てた上で、反復インスタンス間の同一性を `SetKey(key(item))` で識別します。シーケンスが「テンプレート内の構文位置」を、キーが「データ同一性」を担う責務分担と、その下でのリスト変異時の最小パッチは §2.7(B) に入出力例として示します。

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

### 2.7 主要な変換の入出力仕様: 装飾の畳み込み・リスト・部品再利用

本方式で要となるのは、単純な要素発行ではなく、装飾チェーン・リスト・`[Composable]` の3つの変換です。§2.4の `If` と同じ密度で、それぞれ「どの入力を、どの生成コードに変えるか」を定めます。

**(A) 装飾チェーンの畳み込み — 入力: 装飾の連鎖 / 出力: `class` は畳み込み、他の属性・イベントは1:1のフレーム**

装飾メソッドは所有要素の属性・イベントへ静的に合成され、ラッパーノードを増やしません。`class` は特別で、`.Class`(または `.Attr("class", …)`)を何個連ねても単一の `class` 属性へ畳み込まれ、追加の属性フレームは生まれません。`class` 以外の属性・イベント(`.Href` / `.Attr` / `.OnClick` / `.On` 等)はそれぞれ独立した属性/イベントフレームとして1:1で発行され、同一属性・イベントの重複バインディングはBC3010で診断されます。

```csharp
// 入力(設計時のC#式)
Button
    .Class("btn")
    .Class("btn-primary")
    .OnClick(() => Save())["Save"]
```

```csharp
// 出力(生成コード) — 2つの .Class は1つの class 属性へ畳み込まれ、.OnClick は独立したフレーム
__b.OpenElement(k,   "button");
__b.AddAttribute(k+1, "class", "btn btn-primary");
__b.AddAttribute(k+2, "onclick", /* () => Save() */);
__b.AddContent(k+3, "Save");
__b.CloseElement();
```

この `Button` の `FrameWidth` は4(`OpenElement` + `class` 属性 + `onclick` イベント + `AddContent`)です。`.Class` を何回連ねてもフレーム幅は増えませんが、`class` 以外の装飾を1つ追加するとフレーム幅も1つ増えます。ラッパーノード方式(装飾ごとに専用のラッパー要素を生成する方式)であれば装飾はDOMノードそのものを増やしますが、本方式はいずれの装飾も所有要素の属性・イベントとして合成するためDOM深さは増えません。要点は「`class` は装飾の個数によらずフレーム幅が一定に畳み込まれる一方、それ以外の属性・イベントは1装飾につき1フレームの1:1対応である」という非対称性で、この不変性が装飾を重ねても差分検知のシーケンス割当が安定する根拠です。

**(B) `ForEach` — 入力: リストの変異 / 出力: キー整合の最小パッチ**

`ForEach`(SSC-3)は `foreach` へ展開され、テンプレート `content` に単一の静的シーケンス空間を割り当てた上で、反復インスタンス間の同一性を `SetKey(key(item))` で識別します。シーケンスが「テンプレート内の構文位置」を、キーが「データ同一性」を担い、責務が直交します。

```csharp
// 入力
ForEach(_items, key: t => t.Id, content: item =>
    Div.Class(item.Done ? "task done" : "task")[Span[item.Title]])
```

```csharp
// 出力(生成コード) — テンプレートのseqは反復間で不変、同一性はキーが担う
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

`SetKey` は Blazor の `RenderTreeBuilder` において「現在開いている要素/コンポーネントフレーム」にキーを付与します(Razor の `@key` と同型)。したがってキーは `content` の**根要素/コンポーネントを開いた直後**に出さなければならず、`OpenElement` の前(親がリージョンの状態)で呼ぶと実行時に `InvalidOperationException: Cannot set a key on a frame of type Region.` となります。この帰結として、`ForEach` の `content` は**単一の要素またはコンポーネントを根に持つ**必要があります(キーの置き場が要素/コンポーネントに限られるため)。`content` の根がリージョンになる形(裸の `if`/`ForEach`/`switch` 等)はキーを適用できず、診断 BC3003(Error)で通知します。`Html.Fragment`(ラッパーレスなグルーピング)と `Html.Raw`(信頼済み生HTML注入)も単一の要素/コンポーネントフレームを開かない点で同じ制約を受け、`content` の根には使えません(BC3003)。入れ子のキー付きリストは内側ループを容器要素で包みます(例: `content: o => Div[ForEach(o.Items, …)]`)。これは Razor で `@if` に直接 `@key` を付けられず要素で包むのと同じ制約です。

この非キー可能性の判定は2つの層で行われ、両者は一致します。テンプレート走査層(`KeyabilityResolver.ResolveRootKind`)は `IfTemplateNode` / `ForEachTemplateNode` / `TextContentTemplateNode` / `FragmentTemplateNode` / `RawMarkupTemplateNode` / `RenderFragmentContentTemplateNode`(外部由来の `RenderFragment?` を `AddContent(seq, RenderFragment?)` としてそのまま発行するノード)をすべて `ContentRootKind.Region` に分類し(`ComponentTemplateNode` / `ElementTemplateNode` のみが `ContentRootKind.Element`)、静的展開後ツリー層(`ComposableExpander.IsKeyableRoot`)は `ComponentNode` / `ElementNode` のみを真とし、それ以外は既定で `false` を返します。この既定 `false` は、新種のノードが増えてもキー可否判定が安全側(非キー可能)に倒れるという意味で正しい設計です。一方、`SequenceAllocator.Width` / `RenderViewEmitter.EmitNode` / `KeyabilityResolver.ResolveRootKind` / `ComposableExpander.ExpandNode` は未知のノード型に対してはいずれも例外を送出し、ケース漏れを黙って通しません。両者は非対称です — フレーム発行・幅計算・根種別解決は「未知のノード型はバグとして早期検出する」契約であるのに対し、`IsKeyableRoot` だけは「未知のノード型は非キー可能として扱う」既定を持ちます。この網羅契約により、展開後ノード `RenderFragmentContentNode`(`SequenceAllocator.Width` では常に1 — シーケンス引数を消費する `AddContent` 呼び出しが `RenderFragment?` の非nullを問わず不可欠であるため)を追加した際も、`SequenceAllocator.Width` と `RenderViewEmitter.EmitNode` の両方にケースを足す必要があり、片方だけの更新は例外で検出されます。

入力が `[A, B, C]` から先頭挿入で `[X, A, B, C]` へ変異した場合の出力パッチを追います。テンプレートのシーケンス番号は全反復で同一であり、識別はキーが担うため、Blazorはキー `A, B, C` を既存フレームへ一致させ(行の状態とDOMサブツリーを保持)、`X` の1行のみを挿入します。仮にキーがインデックス由来であれば、位置0を「A→X の変更」、位置1を「B→A の変更」…と誤認し、全行を書き換えて各行のローカル状態(フォーカス位置等)を失います。キーが「データ同一性」を、シーケンスが「テンプレート位置」を分担することが、この最小パッチと状態保持を同時に成立させます。

**(C) `[Composable]` の静的インライン展開 — 入力: 部品呼び出し / 出力: 連続seqへの直接展開**

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
// 出力(生成コード) — Toolbar はインライン展開され、seqは 0 から連続する
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

`[Composable]` 呼び出しは、その本体を呼び出しサイトへ直接書いた場合と同じフレーム列・シーケンス区間を生みます。実行時ディスパッチもリージョン分離も介在しません。対照的に、`[Composable]` の付かない `View` 返却メソッドはOpaque(§2.3)として扱われ、リージョンで包まれ実行時に `RenderFragment` として描画され、診断BC2001の対象となります。属性付与の有無ではなく、この静的展開可能性が部品再利用の速度・トリミング特性を分けます。

**コンポーネントの fragment スロット** — `RenderFragment` 型のパラメータは、値ではなくノードツリーを
持つため `ComponentParameter`(スカラー)とは別チャンネル(`ComponentSlot` / `ComponentSlotNode`)に
格納します。幅は `1 + Parameters.Length + Σ(1 + Width(slot.Content))` で、スロット1つが
`AddComponentParameter` 1回とその内容の幅を消費します。

ラムダ内部のシーケンス番号は外側の平坦なカウンタを継続し、独立したシーケンス空間を作りません。
スロットのフレームは呼び出し元ではなく**子コンポーネントのフレーム列**に属します。Compose のジェネレータは
常に `AddComponentParameter(seq, "ChildContent", (RenderFragment)(...))` を発行する側です。
fragment を直接 invoke するかどうかは渡し先コンポーネント(手書きでも Razor 生成でも)が `AddContent` に
渡すか自分で呼ぶかの問題であり、前者は Blazor のリージョンが隔離しますが、後者はリージョンが張られず、
我々の番号がホスト自身のフレームと隣接します。0 から振り直すとホストの低い番号と衝突してコンポーネントが
再生成され状態が失われるため(実測)、平坦継続が厳密に安全側です。これは Razor と同一の挙動で、
リージョンで包んでも解決しません(リージョンはホストのフレーム列における隣接関係を変えないため)。

---

## 3. メモリレイアウト

### 3.1 SSC経路: 中間表現ゼロ

SSC(および Transplantable)経路の実行時像は、静的シーケンス定数を伴う `RenderTreeBuilder` 命令の直列実行です。これはRazorコンパイラの生成物と同形式であり、UI記述に由来する中間オブジェクト(要素ツリー、ビルダー、`params` 配列)はヒープに生成されません。マーカー型 `View` は空の `readonly struct` であり、実行時に到達不能です。

したがって、SSC経路のアロケーション特性は等価なRazorコンポーネントと同等です(予測値)。残存するアロケーション源はBlazor自体に由来するものに限られます: イベントハンドラのデリゲート/クロージャ、`RenderTreeBuilder` 内部のフレーム配列(再利用される)、補間による一時文字列(`ISpanFormattable` 経路で部分的に緩和)。

### 3.2 Opaque経路: フラグメント内包 `View`

Opaque経路でのみ、`View` は実体を持ちます。この場合の `View` は `RenderFragment` への参照を内包する軽量ハンドルであり、ヒープ割り当ては内包フラグメントの構築分に限られます。これは `RenderFragment` を手書きで合成した場合と同等のコストです。

```csharp
public readonly struct View
{
    internal readonly RenderFragment? Fragment;   // SSC経路では常に null(到達不能)
    internal View(RenderFragment fragment) => Fragment = fragment;
}
```

外部由来の `RenderFragment?` を要素コンテンツとして受け取る `implicit operator View(RenderFragment?)` は、現状SSC経路しか存在しないため `=> default` を返すだけの inert な変換です。これは暫定の実装であり、Opaque経路(または付録CのDEBUG解釈モード)が実装された時点で、この節の `Fragment` フィールドを実際に構築して返す実体を持ちます。

### 3.3 静的サブツリーの定数化

状態に依存しないサブツリー(固定ヘッダー、利用規約等)について、Source Generatorは依存解析により状態参照を持たない領域を検出し、生成コード上で属性文字列・コンテンツを定数化します。フレーム発行自体はBlazorの差分検知が要求するため毎回行われますが、値の再計算・再フォーマットは発生しません。

---

## 4. イベント・プロパゲーションと並行モデル

### 4.1 実行順序と単一方向データフロー

ユーザーアクションからDOM更新までは、次の順序で一方向に進む:

1. **イベント発火**(ブラウザ)
2. **ディスパッチ**: Blazor `SynchronizationContext` へのディスパッチ完了
3. **状態遷移**: `s_t` から `s_{t+1}` への更新
4. **フレーム列生成**: `RenderView` の実行による `r_{t+1}` の生成
5. **差分適用**: `Δ(r_t, r_{t+1})` のDOM同期

この順序の要点は、状態遷移がフレーム列生成に先行しなければならない(状態遷移 → 生成)という一点にあります。これは単一方向データフローの強制であり、`RenderView` の実行中に状態遷移を発生させてはならないことを意味します。現行のソースレベル実装では「設計時表現(`ComposeComponentBase.Body` または `ComposeLayoutBase.Chrome`)内での状態変更禁止」に対応し、違反は診断BC3001となります。`Button` のonClickラムダ(`DeferredEventHandler`コンテキスト)はレンダリングではなくイベント後に実行されるため除外されます。任意のメソッド呼び出し経由の副作用の完全な検出は保証しません(§1.1 BC3001注記参照)。`[Composable]` 本体への同等の検証は将来拡張候補であり、この初期契約には含めません。

### 4.2 Blazor標準ディスパッチとの役割分担

Blazorは既に `SynchronizationContext`(および `ComponentBase.InvokeAsync`)により、レンダリングスレッドへの直列化ディスパッチを提供しています。BlazorComposeはこれを置換しません。本ライブラリが並行モデルに追加するのは次の2点に限定されます。

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

net11.0ターゲットでは、Runtime Async(ランタイムネイティブ非同期)により非同期イベントハンドラのステートマシンオーバーヘッドが低減され、スタックトレースが平坦化されます。BlazorCompose側のコード変更は不要であり、TFM切替のみで恩恵を受けます。

---

## 5. WebAssemblyとAOTコンパイル適合性

BlazorComposeは実行時メタデータ分析・動的ディスパッチを排除します。全パラメータバインディング(`Component<T>().Param(...)` を含む)は、Source Generatorが生成する静的セッター経由で行われます。`Param` の式引数はSGが構文解析してセッター生成にのみ利用し、式木(`System.Linq.Expressions`)のランタイムコンパイルは行いません。`System.Reflection` / `System.Linq.Expressions` へのランタイム依存は0です。

さらに、`Body` プロパティと設計時API — `Html`・`Decorations` の全メンバー、および設計時慣性型 `View` / `ComponentView<T>` / `ElementBuilder`(付録A、BC3014)の全メンバー — はいずれも実行時に到達不能であるため、ILトリマーはこれらを丸ごと除去できます。UI記述のソースコードはバイナリサイズに寄与しません。これは実行時評価を行うコードファースト方式では得られない性質です。除去は `TrimMode=full`・`ILLinkTreatWarningsAsErrors=true` の下で、`System.Reflection.Metadata` のMethodDef走査により確認できる設計です。

リフレクションベースのバインディングを持つ同等構成との比較で、AOTコンパイル後のWasmペイロードサイズを約20〜30%削減(予測値)と見込みます。この予測値は、(a) BlazorCompose構成、(b) リフレクションバインディング構成、(c) 素のRazor構成の3系統のベンチマークにより確定値へ置き換えられます。素のRazor構成との比較ではほぼ同等となる見込みです。

BlazorComposeのトリミング/AOT適合契約が対象とするのは、自身が生成するコード(リフレクション不使用の`RenderView`、実行時に到達不能な設計時API、`ComponentView`ビルダー)がトリミングで除去されることまでです。`Component<T>().Param(...)` によるコンポーネント埋め込みでは、パラメータが実行時に適用される段でフレームワーク側のリフレクションベース`[Parameter]`バインダー(`ComponentProperties.SetProperties`)が到達可能になりますが、これはBlazor SDKのトリミングプロファイルが担う範囲であり、BlazorCompose自体の責務ではありません。トリムテストハーネス(`tests/BlazorCompose.TrimTestApp`)では、Blazor SDKのプロファイルを持たない素のコンソールアプリという性質上この1点のフレームワーク側`IL2072`が表面化するため、`ComponentProperties.SetProperties`のみに限定した抑制(`ILLink.LinkAttributes.xml`)を適用しています。

`Component<T>()` の型引数は生成コード中の `OpenComponent<T>` へリテラルとして落ちるため、BlazorComposeのジェネレータが走る時点で解決している必要があります。ソースジェネレータは互いの出力を観測できないため、**同一プロジェクト内**の `.razor` コンポーネントはこの条件を満たさず、BC3012として報告されます。参照先プロジェクトやNuGetパッケージに含まれる `.razor` コンポーネントは通常どおり解決するため、この制約は同一コンパイル内に限られます。手書きのC#コンポーネントは常に利用できます。

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

| 評価項目                   | Blazor(通常Razor)                 | BlazorCompose(本システム)                                    | 備考                                      |
| -------------------------- | --------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| 記述パラダイム             | マークアップファースト(HTML + C#) | コードファースト(純粋C#)                                     | SwiftUI/Compose と同系統の記述体験          |
| 型安全性(Style/Layout)     | 低(文字列CSS/クラス名依存)        | 完全型安全(コンパイル時検証)                                 | IDEインテリセンスが駆動               |
| コンパイル方式             | Razorコンパイラ(マークアップ→C#)  | Source Generator(C#式→C#)                                    | 生成物は同形式                            |
| シーケンス番号管理         | コンパイラによる静的割当          | SGによる静的割当(SSC)+ リージョン分離(Transplantable/Opaque) | 開発者はシーケンス制御を意識不要          |
| 実行時の中間表現           | なし                              | なし(SSC経路)/ フラグメント内包 `View`(Opaque経路のみ)       | UI記述由来のヒープ割当ゼロ                |
| メモリ・レンダリングコスト | 基準                              | 同等(予測値)                                                 | 生成コードが同形式のため                  |
| AOT / Wasm互換性           | 適合                              | 完全適合(リフレクション依存0、UI記述コードはトリム除去)      | 対リフレクション構成で20〜30%削減(予測値) |
| Hot Reload                 | ツーリングに一級統合              | EnC標準経路(メソッド本体差替+`MetadataUpdateHandler`)        | 編集後の意味論はRazorと同一(§2.6)         |
| 対応TFM                    | —                                 | net10.0(ベースライン)/ net11.0(Union型内部表現等)            | LTS優先のマルチターゲット                 |

---

## 付録A: 診断一覧

### A.0 報告経路の制約: コンパイルエラーを説明する診断はアナライザーでは報告できない

csc は宣言レベルのエラー(CS0534、CS0246、CS0234 等)を含むコンパイルに対してアナライザードライバを実行しません。アナライザーは妥当なシンボルモデルを前提とするため、これは Roslyn の標準動作です。一方 Source Generator ドライバにはこのゲートがなく、生成器が報告した診断は宣言エラーと共存して出力されます。

ここから、診断の実装先を決める規則が導かれます。

> **その診断の役割が「利用者が単独では読み解けないコンパイルエラーの原因を名指すこと」であるなら、その診断は Source Generator が報告しなければならない。** アナライザーとして実装した場合、診断が発火すべき条件そのものがアナライザードライバを停止させるため、原理的に到達不能になる。

BC1001 はこの規則に違反していました(#76)。`partial` の欠落は `RenderView` の非生成を意味し、それは CS0534 — すなわち宣言レベルのエラー — を必ず発生させるため、アナライザーとしての BC1001 は実ビルドで一度も報告され得ませんでした。診断すべき条件が診断自身を抑止していたことになります。BC1001 は生成器報告へ移されています。同じ理由で BC1003 / BC1005 は当初から生成器報告であり、CS0534 と共に出力されます。

副次的な帰結として、**宣言エラーを1つ含むコンパイルでは、そのプロジェクトのアナライザー診断が BlazorCompose 以外(CA/IDE 規則を含む)もすべて消えます**。これは BlazorCompose 固有の性質ではありませんが、非 partial なコンポーネントはこの崖に落ちる最も容易な経路であり、その意味でも BC1001 を生成器から即座に報告する価値があります。

現行の報告経路は、BC3001 のみ `RenderMutationAnalyzer`(状態変更を含む設計時表現はコンパイル自体は成立するため、アナライザードライバが動く)、それ以外はすべて `BlazorComposeGenerator` です。新しい診断を追加する際は、その発火形状がコンパイル可能かどうかを先に判定してください。

この節の内容は文書上の約束ではなく、テストで固定されています。`tests/BlazorCompose.DiagnosticTests` が `tests/diagnostic-fixtures` の各プロジェクトを実 MSBuild でビルドし、SARIF ログから「どの診断が、どの位置に報告されたか」を検証します。同一の CA1050 違反型を全フィクスチャに含めることで、宣言エラーのあるコンパイルではアナライザー診断が消えること・ないコンパイルでは報告されることの両方が固定されており、`DiagnosticDescriptors` の全記述子はこの層で網羅されているか、理由付きの除外リストに載っているかのいずれかである必要があります。

次節の表そのものも同じテストプロジェクトが検証します。`DiagnosticTableTests` が A.1 の表を読み取り、`DiagnosticDescriptors` と双方向で突き合わせます — 記述子があって行が無ければ失敗し、行があって記述子が無い場合も、実装に先行して仕様化されている理由を `DiagnosticExpectations.DocumentedWithoutDescriptor` に記録していない限り失敗します。BC2001 が現在の唯一の登録項目です。**種別**列も記述子の `DefaultSeverity` と照合されるため、診断の severity を変えることは表を変えることでもあります(記述子を持たない行は照合対象外です)。

### A.1 診断一覧

| ID     | 種別    | 内容                                                                                  |
| ------ | ------- | ------------------------------------------------------------------------------------- |
| BC1001 | Error   | 設計時表現(`ComposeComponentBase.Body` または `ComposeLayoutBase.Chrome`)の override を宣言するクラスが `partial` として宣言されていない(同一クラスへ `RenderView` を生成できない)。Composeベースを継承するだけで override を宣言しないクラス(中間abstract基底、基底が既に宣言している葉、再abstract化)、および `RenderView` を手書きしているクラス(生成物が無いため `partial` は不要)は対象外。ネストクラスは BC1005 が優先する(`partial` を足しても解決しないため)。生成器が報告する(理由はA.0)  |
| BC1002 | Error   | `[Composable]` メソッドがSource Generatorのサポートする静的展開契約を満たさない(`View` 型パラメータ等)                                     |
| BC1003 | Error   | 設計時表現(`Body` / `Chrome`)が静的にシーケンス可能な部分集合へ分類できず、実行時フォールバックも未実装のため `RenderView` を生成できない。Opaque/Transplantable 経路の実装により発火条件は縮小する(過渡的) |
| BC1004 | Error   | 設計時表現(`Body` / `Chrome`)の override が、ジェネレータの翻訳できないゲッターを宣言している(文を含むゲッター、または本体を持たない自動プロパティ)。`=> expr` / `get => expr` / `get { return expr; }` のいずれかに書き直すか、`RenderView` を手書きする。再abstract化(`abstract override`)は対象外。実装部を持たない partial プロパティも対象外(CS9248 が原因を名指す) |
| BC1005 | Error   | ネストしたクラスが設計時表現を宣言している。生成コードは外側の型宣言の連鎖を再現できないため、トップレベルの型へ移す必要がある |
| BC2001 | Info    | Opaque構文を検出。動的リージョンへ縮退し、当該領域の静的差分最適化が失われる(将来射程: `AddContent(seq, RenderFragment?)` を発行する `RenderFragmentContentNode` は仕様上のOpaque経路であり、BC2001実装時の対象に含まれる想定。未実装。なお #32 の `ComponentSlot` は `AddComponentParameter` と静的採番済みラムダのみで構成される完全なSSC経路であり、BC2001の対象ではない。名前が似ている `RenderFragmentContentNode`(Razor→Compose 方向)とは逆向きの構文である) |
| BC3001 | Error   | 現行実装では設計時表現(`ComposeComponentBase.Body` または `ComposeLayoutBase.Chrome`)本体内での状態変更(単一方向データフロー違反)。初期検出範囲: コンポーネントインスタンスメンバーへの直接書き込み(代入/複合代入/インクリメント/デクリメント)。`.OnClick`/`.On` の遅延イベントハンドラ引数(入れ子ラムダを含む)内は除外。任意の副作用の完全検出は保証しない。`[Composable]` 本体への適用は将来拡張候補 |
| BC3002 | Warning | `ForEach` の `key` セレクタが要素の恒等性を保証しない可能性(インデックスベースキー等) |
| BC3003 | Error   | `ForEach` の `content` が単一の要素/コンポーネントを根に持たず、キーを適用できない(根がリージョンになる裸の `if`/`ForEach`、`Fragment`、`Raw` 等)。内側を容器要素で包む(例: `Div[...]`)必要がある |
| BC3004 | Error   | `ForEach` の `content`/`key` がインライン式ラムダでない(ブロック本体ラムダ/メソッドグループ等)ため静的解析できない |
| BC3005 | Error   | `Component<T>().Param` のセレクタが単純なプロパティ選択(`c => c.Prop`)でない(キャスト/メソッド呼び出し/捕捉変数のメンバー等) |
| BC3006 | Error   | `Component<T>().Param` の対象が settable な `[Parameter]` プロパティでない(実行時 throw を防ぐためコンパイル時に拒否) |
| BC3007 | Error   | `Component<T>().Param` のチェーンが同一プロパティを複数回バインドしている(Blazorは最後の値のみ適用するため重複はコンパイル時に拒否) |
| BC3008 | Error   | 装飾(`.Class`/`.Attr`/型付き属性ショートカット/`.OnClick`/`.On`)が単一要素を開くノード(要素ヘルパ/`Element`)以外に書かれている。装飾は `ElementBuilder` の拡張であるため、レシーバが `View`/`ComponentView<T>`(`If`/`ForEach`/`Fragment`/`Raw`/`[Composable]`結果/`Component`、および子を与え終えた要素)の場合は `Decorations` に対するオーバーロード解決が失敗する。外部から渡された `RenderFragment` もレシーバとして受理する — `View` へ暗黙変換されるものの、拡張メソッドのレシーバは恒等/参照/ボクシング変換しか取らずユーザー定義変換を適用しないため、同じく解決に失敗し、作者の誤りは `Fragment`/`Raw` を装飾した場合と同一である(DESIGN.md §4.1)。翻訳に失敗した設計時表現を掃引し、この失敗したチェーンを検出して報告する(型システムが挙げるCS1929は宣言段階の打ち切りにより作者へ届かないため。§2.2) |
| BC3009 | Error   | `Element` のタグ引数が非空のコンパイル時定数文字列でない(宣言性・予測可能性のため) |
| BC3010 | Error   | 同一要素上で属性またはイベントが複数回バインドされている(属性チャネル内の重複は後勝ちで前が死に、属性チャネルとイベントチャネルにまたがる同名バインディングは両方が生き残って二重発火する。いずれも書いたとおりにならないため拒否)。畳み込まれる `class` のみ例外 |
| BC3011 | Error   | `.Attr` の名前 / `.On` のイベント名が非空のコンパイル時定数文字列でない(宣言性・タイポ検査・class畳み込み判定・重複検出の前提) |
| BC3012 | Error   | `Component<T>()` の型引数がジェネレータ実行時に解決できない。同一プロジェクト内の `.razor` コンポーネントはRazorコンパイラ自身がソースジェネレータであるため相互に出力が見えず、常にこの状態になる。参照先プロジェクト/NuGetパッケージの `.razor` と手書きC#コンポーネントは正常に解決する。タイポや `using` 漏れの場合は同じ位置に CS0246 も報告される |
| BC3013 | Error   | `Component<T>()[…]` で子コンテンツが与えられているが、`T` がそれを受け取れる `ChildContent`(settable な `[Parameter]`、非ジェネリック `RenderFragment`)を持たない |
| BC3014 | Error   | 設計時慣性型(`View` / `ComponentView<T>` / `ElementBuilder`)がジェネリック `.Param` の値位置に渡された |
| BC3015 | Error   | body 内の値式で、生成コードへ安全に移植できない未解決の型参照 |

## 付録B: 検討した代替アーキテクチャと不採用理由

**B.1 Interceptor方式(C# 14)** — `Body` を実行時に評価し、各設計時API呼び出しサイトをInterceptorで静的シーケンス付き実装へ置換する方式。呼び出しサイト置換自体は成立するが、(a) 実行時評価を前提とするため装飾チェーンの合成型に対する統一戻り値型が構成できない(C#に不透明戻り値型が存在せず、`ref struct` はインターフェースへ変換できない)、(b) `[InterceptsLocation]` の位置指定子がソース変更のたびに再計算され、ビルドパイプラインが位置データに敏感になる、(c) 本方式(全体生成)が採用可能である以上、部分置換に固有の利点がない、の3点により採用しませんでした。

**B.2 ランタイム `ref struct` ツリー方式** — 要素を `readonly ref struct` としてスタック上に構築し、実行時に `Render` を再帰呼び出しする方式。GC回避には有効だが、(a) 可変個の子要素を受け取る手段がない(`ref struct` は配列・`params` に格納不可、ジェネリックオーバーロードはアリティ上限を持つ)、(b) B.1と同じ戻り値型問題、(c) 静的サブツリーのキャッシュと両立しない(`ref struct` はフィールド格納不可)、により採用しませんでした。本方式(生成コードによる直接発行)は、同じゼロアロケーション特性を型システム上、無理なく達成します。

**B.3 `ComposeLayoutBase` を `ComposeComponentBase` から派生させ `SetParametersAsync` で介入する方式** — レイアウトを通常のComposeコンポーネントと同じ基底型に載せ、Blazorが渡す `Body` パラメータを `SetParametersAsync` で抜き取ってから残りのパラメータを基底へ転送する方式。当初はこの案を採る判断をしていましたが、実装して実行した結果、成立しないことが確認されたため撤回しました。残りのパラメータを転送する唯一の公開手段である `ParameterView.FromDictionary` は、その列挙子が `cascading: false` を固定値で返すため、cascading値のみを受け取るプロパティに対して `ComponentProperties.SetProperties` が例外を投げます(*"The property 'X' … cannot be set explicitly because it only accepts cascading values."*)。影響は `[CascadingParameter]` に限りません。この検査は `CascadingParameterAttributeBase` を基準とするため `[SupplyParameterFromQuery]` も同じ理由で落ち、認証テンプレートが標準で用いる `[CascadingParameter] Task<AuthenticationState>` もレイアウトで受け取れなくなります。加えてナビゲーションごとに `RenderTreeFrame[]` を確保します。採用した方式(`ComposeLayoutBase : LayoutComponentBase`)は、Blazorが名前で要求する `Body` を正しい名前のまま継承し、`SetParametersAsync` に付与された `[DynamicDependency]` トリマーヒントもそのまま引き継ぐため、プラットフォームのパラメータ結線と競合しません。教訓として、プラットフォーム側のパラメータ結線に介入する方式は本設計では採りません。

## 付録C: 開発時フォールバック案 — 解釈モード(コンチネンシー)

§2.6のツーリング検証で、特定環境においてSource Generatorの再実行がEnCに反映されないと判明した場合に限り、次のDEBUGビルド限定フォールバックを導入する余地を残します。

DEBUG構成では、設計時API群を慣性実装から実働実装(`View` に `RenderFragment` を構築して内包する)へ条件コンパイルで切り替え、`RenderView` の代わりに `Body` を実行時評価します。全体は単一のリージョン内で動的シーケンスを用いて描画されます。Hot Reloadは `Body` プロパティ本体の差し替え(EnC標準サポート)として自然に機能し、SGの再実行に依存しません。RELEASE構成では本仕様の生成コード経路のみが用いられるため、出荷物の性能・サイズ特性に影響しません。

本案は開発時と実行時で描画経路が二重化する複雑性を伴うため、§2.6のツーリング確認で必要性が示されるまで導入しません。
