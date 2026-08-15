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
  [ViewPart] View F() => …         ④ RenderView(RenderTreeBuilder) の生成
                                        — 静的seq定数の埋め込み
                                        — 動的式・ラムダの構文移植
                                        — [ViewPart] のインライン展開
```

生成物は同一partialクラス内の `RenderView` オーバーライドであり、基底クラス(`BodyComponentBase` またはレイアウトの `ChromeLayoutBase`)の `BuildRenderTree` から呼び出されます。設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)と設計時APIは、いずれも実行時に到達不能であり、AOTビルドではILトリマーが除去します。ここでいう設計時APIとは、`Html`・`Decorations` の全メンバーと、設計時慣性型 `View` / `ComponentView<T>` / `ElementView`(付録A、BCF3014)の全メンバーです。除去は `System.Reflection.Metadata` によるMethodDef不在検査をもって確認できる設計であり、その確認手段はトリムテストが担います。

設計時表現のゲッターは**1つの `return` へ到達できなければなりません**。`=> expr` / `get => expr` /
`get { return expr; }` の 3 つの綴りは同一であり、いずれも同じ `RenderView` を生成します。その `return`
の手前には、ローカル宣言文と式文を並べられます。これは `ForEach` の `content` が受け付けるブロックと同じ
形であり(§2.3 Transplantable)、書かれた文はフレーム発行の手前へ移植されます。形を読む実装は1つで、
`RenderExpressionAnalyzer.TryReadTransplantableBlock` がゲッターと `content` の双方に答えます。文を置ける
ことは副作用を許すことではなく、状態変更は BCF3001 のままです。BCF1004 として残るのは4つで、2つ目の
`return`、ネイティブの制御構文、生成器の予約名(`__bcf_` 接頭辞と `__builder`)を持つローカル、そして
翻訳対象のゲッター本体を宣言しない自動プロパティです。再abstract化(`abstract override`)と、実装部を
持たない partial プロパティは対象外です。後者は CS9248 が原因を名指します。設計時表現は実行時に評価され
ない不活性な構文であり、この制約は「構文を静的に翻訳する」という前提そのものです。

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
            case ViewPartCall(m):
                code += Compile(Body(m), model)            // インライン展開(再帰)
            case Transplantable(stmt):                     // ネイティブ if/foreach 等
                code += WrapInRegion(Transplant(stmt), seq: counter); counter += 1
            case Opaque(expr):                             // 非[ViewPart]のView返却呼び出し等
                code += WrapInRegion(EmitFragmentOf(expr), seq: counter); counter += 1
                report BCF2001(v)
    return code
```

上の擬似コードはノード単位のループとして書いていますが、畳み込みの単位は**連続する兄弟の run** であり(§2.7(D))、run 全体が1つの `AddMarkupContent` として発行されます。したがって幅を定めるのは発行そのものであって、ノード種別ではありません(§2.7(B))。幅を独立に計算する実装は存在せず、増やしてもいけません。

`FrameWidth` はシーケンス引数を消費する `RenderTreeBuilder` 呼び出し数のみをカウントし、`CloseElement`・`CloseRegion` のようにシーケンス引数を持たない呼び出しは含みません。ノード種別と、そのノードが畳み込み可能かどうかから定まります。例えば、子を持たない `Span` = 1 [`OpenElement`]、**動的な**文字列子を1つ持つ `Span`(`Span[$"...{x}"]`)= 2 [`OpenElement` + `AddContent`]、onclick属性1個付き `Button` = 3 [`OpenElement` + `AddAttribute` + `AddContent`] です。イベントは畳み込みを阻むため、`Button` の子が定数でもこの幅です。対して**定数**の文字列子を1つ持つ `Span`(`Span["..."]`)はそれ自体が畳み込み可能なので幅 1 です(`AddMarkupContent` 1回)。

装飾チェーンのうち `class` は親要素の `class` 属性へ静的に合成されるため、`.Class` の追加はフレーム数を増やしません(`.Class("a").Class("b")` は単一の `AddAttribute` に畳み込まれます)。畳み込む値は、コンパイル時に読める項を先に片付けてから組み立てます。定数 `null` の項は落ち、隣接する定数文字列は1つのリテラルへ畳まれ、残った項が2つ以上あるときだけ、生成クラスが自身のために持つ `private static` の join を呼びます。この join は実行時に `null` の項を飛ばすため、区切りの空白は項と一緒に消えます(#236)。項が全て落ちても `AddAttribute` は発行されるため、フレーム幅は装飾の個数だけで決まり、値によって動きません(#234)。

`class` 以外の属性・イベント装飾(`.Href` / `.Attr` / `.OnClick` / `.On` 等)はそれぞれ1装飾につき1フレームが追加されます(詳細は§2.7(A))。例外は `.Bind` で、1つにつき属性フレームとイベントフレームの2つを追加します。同一要素に何個でも置けるため、この2フレームがその個数ぶん積まれます(§2.7(A))。動的引数(補間文字列、状態参照、イベントラムダ)は評価されず、構文として `EmitFrames` の出力へ移植されます。同一partialクラス内に生成されるため、`this` 経由のprivateアクセスは保存されます。

値式を生成コードへ移植するとき、解決済みの型名は `global::` から始まる完全修飾名へ正規化します。未解決の型名は、元ファイルの `using` や名前空間に依存する表記のままでは安全に移植できないためBCF3015とします。ただし、作者が `global::` から記述した型参照は字句コンテキストに依存しないので通常のC#の名前解決に委ねます。ジェネリック型の外側と各型引数は独立に判定します。分解宣言の `var` だけは書かれたまま残します。括弧付き designation の手前に言語はどの型も置けないため、正規化した名前を書ける形がそこには無いからです(#342)。

`Html.Fragment`(ラッパーレスなグルーピング)は自身のフレームを開かないため、その `FrameWidth` は子ノードの `FrameWidth` の総和です(ローカル変数を持たない `[ViewPart]` 展開ノードと同型)。ただし子がすべて畳み込み可能な場合、fragment 全体が1つの run となり幅は 1 になります(§2.7(D))。`Html.Raw`(信頼済み生HTML注入)は `AddMarkupContent` を1回発行するだけの単一フレームで、`FrameWidth` = 1 です(子を持たない文字列コンテンツノードの `AddContent` と同型)。いずれも要素/コンポーネントのフレームを開かないため、`ForEach` の `content` の根には使えず(BCF3003)、装飾もできません(BCF3008、詳細は§2.7(A)と付録A)。

装飾不可は型システムでも表現されています。装飾は `ElementView` の拡張であり、`Fragment` / `Raw` は `View` なのでCS1929です。それでもBCF3008を報告するのは、このCS1929が作者へ届かないためです。設計時表現が翻訳できないコンポーネントには `RenderView` が生成されず、クラスは必ず宣言段階エラーのCS0534を負うため、`csc` はメソッド本体の束縛へ進みません。`RejectedDecorationScanner` が存在しなかった時点の実MSBuild測定では、フィクスチャ `Bcf3008Host` が報告したのはCS0534とBCF1003だけで、CS1929は現れませんでした。BCF3008を報告するようになった現在は、同じフィクスチャがそれも報告します。同じビルドでBCF1003は届いています。この打ち切りを越えられるのは生成器の診断だけです。

### 2.3 静的シーケンス可能サブセット(SSC)

任意のC#コードに対して条件(2)の `σ` は構成できません(呼び出しグラフが実行時にのみ確定するため)。解析の適用範囲を次の3階層に分類します:

**SSC(完全静的)**: 静的シーケンス割当の対象。
- SSC-1: `Body` 本体、および `[ViewPart]` メソッド本体における、要素ヘルパー/装飾の直接記述、および `Component<T>()`・`Fragment`・`Raw` の直接呼び出し
- SSC-2: `If(cond, then, otherwise)` コンビネータ(両分岐がインラインラムダであること)
- SSC-3: `ForEach(source, key, content)` コンビネータ(`content` がインラインラムダ、`key` はインライン式ラムダまたは書かれた `null`)、およびその糖衣である子リスト内のスプレッド `[.. <source>.Select(<インライン式ラムダ>)]`(同一のノードへ畳まれ、`SetKey` を出さない点まで一致する)
- SSC-4: SSC-1〜3の任意のネスト、および `[ViewPart]` 呼び出しの静的インライン展開

**Transplantable(構文移植)**: 文が生成コードへ構文ごと移植され、境界リージョンで包まれます(§2.5)。受理する形は1つで、ローカル宣言文と式文が並び、最後に `return <SSC式>;` が1つ来るブロックです。書ける位置は3つあり、`ForEach` の `content` に書かれたブロック本体ラムダ、設計時表現(`Body` / `Chrome`)のゲッター、そして `[ViewPart]` の本体です。1つ目では文がループの内側へ、2つ目では `RenderView` のフレーム発行の手前へ、3つ目では展開先へ落ちます。移植した文はシーケンス引数を持つ呼び出しを含まないため、シーケンス幅は式1つで書いた場合と同じです。複数の `return` とネイティブの `if` / `foreach` / `switch` は、それぞれ独自のシーケンス空間を要するので受理しません。診断は位置で分かれ、`content` はBCF3004、ゲッターはBCF1004、`[ViewPart]` はBCF1002 です。

`[ViewPart]` の本体が囲みスコープへ束縛するローカルは、その定義でだけ生成名を受け取ります(#336、#343)。定義の本体は呼び出しごとに複製されるため、著者の書いた名前は1つの生成スコープで2度宣言されえます。束縛の経路は2つあり、先頭の文が宣言するローカルと、返却式の designation(パターン変数、`out var`、分解)です。後者だけを持つ式本体もこの命名を受けます。ラムダの内側は対象外です。`If` の分岐も `ForEach` の content も生成コードでは自分の波括弧に落ちるため、2つの展開が1つのスコープで出会いません。命名は反復変数と同じ機構で、宣言子の識別子も参照も同じ hole が担い、名前は展開が鋳造します。設計時表現の側は書かれた名前のままです。1つの設計時表現の中では著者の入れ子と生成コードの入れ子が一致するため、書かれた名前はそこで必ず合法だからです(兄弟のブロックは兄弟の生成スコープになり、ゲッターのローカルとブロックのローカルが同名なら著者のファイルで既にCS0136 です)。

囲みスコープのローカルを受理する位置は2つで、いずれも lowered された構文のヘッダです(#361)。`If` の条件は生成された `if` のヘッダへ落ち、両分岐を包みます。`ForEach` と、その糖衣である `..source.Select(…)` のソースは生成された `foreach` のヘッダへ落ち、ループ本体を包みます。`key` の本体はそのループ本体の `SetKey` へ落ちるため同じく読めます。移植ブロックと同じ機構(`ViewPartBodyContext.PushTransplantedScope`)で登録し、設計時表現と `[ViewPart]` の双方が同じ検査を読むので、2つの位置は同時に閉じます。

受理はこの2つに限り、判定は包含だけでは足りません。理由は位置ごとに違います。コンポーネントのスロットは中身が `RenderFragment` ラムダ1つに包まれるため、あるスロットで宣言したローカルは兄弟のスロットにも兄弟のパラメータにも届きません。要素の兄弟は逆に1つのブロックへ並ぶものの、著者の順序では並びません。class チャネルは属性ループの手前へ、イベントとバインドはその後ろへ落ち、定数の子の連なりは1つの markup フレームへ畳まれるためです(§2.7)。著者のファイルでは `out var` が囲む文までスコープを持つので、どちらの形もC#としては通ります。境界の両側は `LoweredHeaderLocalTests` が押さえています。

**Opaque(実行時評価)**: `[ViewPart]` の付かない `View` 返却メソッド呼び出し、デリゲート経由の間接呼び出し等。SGは内部を解析できないため、呼び出し式を生成コードへ移植し、実行時に返された `View` の内包する `RenderFragment` を描画します。診断BCF2001(Info)で通知されます。

この経路には前提が1つあり、それが受理範囲を決めます。`View` にフラグメントを入れる綴りは `implicit operator View(RenderFragment?)` だけであり、設計時表層のメンバーはすべて既定値を返します(§3.2)。したがって表層から組まれた `View` はフラグメントを持たず、Opaque経路へ載せても何も描画しません。呼び出し先のソース宣言が現コンパイル内にあり、その本体が設計時表層を参照している場合は、この経路へ落とさずBCF3030(Error)で止めます。Opaqueとして受けるのは、本体が表層を参照していない場合と、宣言が読めない場合です。後者には判定できない残余があり、付録A のBCF2001 行に記録しています。

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

`Body` 式または `[ViewPart]` 本体の変更は、再生成された `RenderView` のメソッド本体差し替えとして現れます。メソッド本体の更新はEnCが安定してサポートする編集クラスです。`[ViewPart]` メソッドの新規追加は既存型へのメンバー追加であり、同じくサポート範囲内です。コンポーネントクラスのシグネチャ変更等のrude editは、Razorコンポーネントと同様にアプリケーション再起動を要します。

リロード後の初回レンダリングの意味論は §1.2 から直接導かれます。編集により構文位置写像 `π` が変化した場合、新旧の `σ(π(n))` は一般に一致しないため(条件(1)の不成立)、当該コンポーネントのフレーム列は差分検知上「排他的破棄と新規生成」として扱われます。コンポーネントインスタンス自体は保持されるためC#フィールドの状態は残り、DOMローカル状態(フォーカス、スクロール位置等)は失われます。これはRazorファイル編集時と同一の意味論であり、追加の仕様を要しません。

適用経路もBlazor標準に乗ります。生成コードは通常の `ComponentBase` 派生型のメソッドであるため、Blazorが備える `MetadataUpdateHandler` による更新後再レンダリング機構がそのまま機能します。本設計固有のツーリング依存は「編集セッション中にSource Generatorが再実行され、生成コードの更新がEnCへ適用されること」の一点のみです。Visual Studio / `dotnet watch` / Riderで挙動差が生じうるため、環境ごとの確認を要します。特定環境で再実行がEnCへ反映されないと判明した場合の開発時フォールバックは付録Cに示します。

### 2.7 主要な変換の入出力仕様: 装飾の畳み込み・リスト・部品再利用・静的畳み込み・フレーム装飾

本方式で要となるのは、装飾チェーン・リスト・`[ViewPart]`・静的サブツリー・非属性のフレーム装飾の5つの変換です(単純な要素発行はここに含みません)。§2.4の `If` と同じ密度で、それぞれ「どの入力を、どの生成コードに変えるか」を定めます。

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

この `Button` の `FrameWidth` は4(`OpenElement` + `class` 属性 + `onclick` イベント + `AddContent`)です。`.Class` を何回連ねてもフレーム幅は増えませんが、`class` 以外の装飾を1つ追加するとフレーム幅も1つ増えます。ラッパーノード方式(装飾ごとに専用のラッパー要素を生成する方式)であれば装飾はDOMノードそのものを増やしますが、本方式はいずれの装飾も所有要素の属性・イベントとして合成するためDOM深さは増えません。この非対称性が、装飾を重ねても差分検知のシーケンス割当が安定する根拠です。

1:1の唯一の例外が双方向束縛です。`.Bind` は属性フレーム1つとイベントフレーム1つを発行するため、この装飾の `FrameWidth` は2です。束縛先の属性が `value` または `checked` のときは、加えて `SetUpdatesAttributeName` を1回呼びます。これはシーケンス引数を取らないためフレームを増やしません(直前の属性フレームに、再同期対象の属性名を記録するだけです)。この2つの属性名に限るのは、クライアントが返すのが `EventFieldInfo` の組み立てるその要素自身の `value`(チェックボックスなら `checked`)だけだからです。`RenderTreeUpdater` はその値を、この呼び出しが指名したフレームへ書きます。それ以外の属性名を指名すると、フォーム要素では無関係のフレームを上書きして本来の属性を取り残し、フォーム要素以外では `EventFieldInfo.fromEvent` が `null` を返すため呼び出し自体が空振りになります。記録先が要素ではなく直前の属性フレームであるため、同一要素に2つの束縛を置いても各々が自分の名前を保ち、上書きも再同期の喪失も起きません(実測)。同一要素に束縛を何個置いても構いません。モデル側も要素あたりの束縛をコレクションとして持ちます。名前が衝突した場合はBCF3010が報告し、束縛先が `class` で同じ要素がクラスチャネルへの装飾も持つ場合はBCF3024が報告します。かつてこれをBCF3021で拒否していましたが、根拠が誤りであったため撤回しました(付録B.5)。

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

`CreateBinder` を拡張メソッドの静的呼び出しとして書くのは、生成ファイルが `using` を持たず、Razorの書くインスタンス構文(`EventCallback.Factory.CreateBinder(…)`)がCS1061になるためです。同じ正規化を作者の書いた拡張メソッドにも適用しています(§2.2)。setterを明示する形では、この `__value => …` の位置に `(Action<T>)(setter)` が入ります。非同期setterでは `RuntimeHelpers.CreateInferredBindSetter(callback: setter, value: 現在値)` が入ります。いずれの形でも現在値を `CreateBinder` の最後の引数として渡す点と、フレーム数は変わりません。

`.Bind` は(D)の静的畳み込みに参加しません。値がフィールドやプロパティの読み出しである以上、コンパイル時定数になり得ません。ただし畳み込みを止めているのは値の非定数性ではなく、述語そのものです。`StaticMarkupSerializer.IsFoldableElement` が、束縛のコレクション `ElementNode.Bindings` が空でない要素を畳み込み不可として返します。値の判定に任せれば、束縛が黙って落ちてただの属性だけが残る出力を、この述語が原理的に作れてしまうためです。

コンポーネント側の `.Bind` はこの非対称性を持ちません。導かれた `{名前}Changed` と `{名前}Expression` は、通常のパラメータフレームとして積まれます。したがってフレーム幅は `.Param` 2回ぶんで、`{名前}Expression` を宣言している型に対しては3回ぶんです((D)末尾のコンポーネントのフレーム幅の式がそのまま成り立ちます)。要素側の `SetUpdatesAttributeName` に相当するものもありません。DOMを持つのは束縛先のコンポーネントであって、この呼び出し元ではないためです。

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

`SetKey` は Blazor の `RenderTreeBuilder` において「現在開いている要素/コンポーネントフレーム」にキーを付与します(Razor の `@key` と同型)。したがってキーは `content` の**根要素/コンポーネントを開いた直後**に出さなければなりません。`OpenElement` の前(親がリージョンの状態)で呼ぶと、実行時に `InvalidOperationException: Cannot set a key on a frame of type Region.` となります。この帰結として、`ForEach` の `content` は**単一の要素またはコンポーネントを根に持つ**必要があります(キーの置き場が要素/コンポーネントに限られるため)。`content` の根がリージョンになる形(裸の `if`/`ForEach`/`switch` 等)はキーを適用できず、診断 BCF3003(Error)で通知します。`Html.Fragment`(ラッパーレスなグルーピング)と `Html.Raw`(信頼済み生HTML注入)も単一の要素/コンポーネントフレームを開かない点で同じ制約を受け、`content` の根には使えません(BCF3003)。入れ子のキー付きリストは内側ループを容器要素で包みます(例: `content: o => Div[ForEach(o.Items, …)]`)。これは Razor で `@if` に直接 `@key` を付けられず要素で包むのと同じ制約です。

この非キー可能性の判定は2つの層で行われ、両者は一致します。テンプレート走査層(`KeyabilityResolver.ResolveRootKind`)と静的展開後ツリー層(`ViewPartExpander.IsKeyableRoot`)のいずれも、キー可能な根を要素とコンポーネントに限ります。どのノード型がどう分類されるかは `KeyabilityResolverTests` が型ごとに固定します。

未知のノード型に対する扱いは、この2層で意図的に非対称です。`IsKeyableRoot` の既定 `false` は、新種のノードが増えてもキー可否判定を安全側(非キー可能)へ倒します。一方 `RenderViewEmitter.EmitNode` / `KeyabilityResolver.ResolveRootKind` / `ViewPartExpander.ExpandNode` は未知のノード型に対して例外を送出し、ケース漏れを黙って通しません。フレーム発行・根種別解決は「未知のノード型はバグとして早期検出する」契約、`IsKeyableRoot` は「未知のノード型は非キー可能として扱う」既定、という分担です。

シーケンス幅を定める実装は発行そのものだけです。各 `Emit*` は自身が進めたカーソルを返し、兄弟の開始位置はその戻り値です。したがって新種のノードを追加する際に足すケースは `RenderViewEmitter.EmitNode` の1箇所で、漏れは例外で検出されます。

シーケンス算術を守るのは、発行されたテキストが持つ性質です。生成コードに現れるシーケンス引数は、木の形に関わらず出現順で `0..N-1` の密な連番になります。どのノード種別も、予約した番号を必ずテキストへ書くためです。`If` は両分岐を予約して両方を発行し、`ForEach` は content 幅を予約して content を発行します。スロットは外側の平坦なカウンタを継続し、`CloseElement` / `CloseRegion` / `CloseComponent` / `SetKey` は消費しません。全ノード種別を覆うコーパスに対して `RenderViewEmitterSequenceTests` がこれを検査します。独立計算した幅との比較は合計しか見ないため、相殺する2つの誤りと `If` の分岐レンジの重複を通しますが、この性質は両方を落とします。なおこの密性は本実装の割当方式の性質であり、Blazor の要求ではありません。Blazor が要求するのは、シーケンス番号が構文位置に対して安定であることだけです。

`RenderFragmentContentNode` が消費するシーケンス番号は、`RenderFragment?` の非nullを問わず常に1です。シーケンス引数を消費する `AddContent` 呼び出しが必ず要り、それが開くリージョンフレームだけが非nullのとき限りであるためです。

入力が `[A, B, C]` から先頭挿入で `[X, A, B, C]` へ変異した場合の出力パッチを追います。テンプレートのシーケンス番号は全反復で同一であり、識別はキーが担うため、Blazorはキー `A, B, C` を既存フレームへ一致させ(行の状態とDOMサブツリーを保持)、`X` の1行のみを挿入します。仮にキーがインデックス由来であれば、位置0を「A→X の変更」、位置1を「B→A の変更」…と誤認し、全行を書き換えて各行のローカル状態(フォーカス位置等)を失います。キーが「データ同一性」を、シーケンスが「テンプレート位置」を分担することが、この最小パッチと状態保持を同時に成立させます。

**(C) `[ViewPart]` の静的インライン展開。入力: 部品呼び出し / 出力: 連続seqへの直接展開**

`[ViewPart]` メソッド呼び出しは、呼び出しサイトへ本体をインライン展開します(§2.2 の `ViewPartCall` ケース)。メソッド呼び出しもリージョン境界も生成されず、シーケンス番号は周囲の本体と連続します。引数は構文として移植されます。

```csharp
// 入力
protected override View Body =>
    Div[Toolbar("My App"), Span["Body"]];

[ViewPart]
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

`[ViewPart]` 呼び出しは、その本体を呼び出しサイトへ直接書いた場合と同じフレーム列・シーケンス区間を生みます。実行時ディスパッチもリージョン分離も介在しません。対照的に、`[ViewPart]` の付かない `View` 返却メソッドはOpaque(§2.3)として扱われ、リージョンで包まれ実行時に `RenderFragment` として描画され、診断BCF2001の対象となります。部品再利用の速度・トリミング特性を分けるのは、この静的展開可能性です。属性を付けたかどうかは関係しません。

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

ラッパー要素が要素フレームのまま残るのは、**畳み込めない子を持つときだけ**です。上の例で `div` が残るのは動的な `Span` を子に持つからで、マークアップフレームは完全なマークアップを運ぶため、開始タグと部分的な子リストを一緒には畳めません。逆に部分木全体が畳み込み可能なら根の開始タグも同じ文字列に入り、完全に静的な `Body` はコンポーネント全体で `AddMarkupContent(0, …)` の1フレームに落ちます。Razorコンパイラも同じ条件で同じ形を出します(#140 が引用しているフレーム比較で差分が frame 0 ではなく frame 2 から始まっていたのは、その例の `div` が動的な子を持っていたためです)。

畳み込み可能性は SSC(§2.3)より真に狭いことに注意が必要です。SSCはシーケンス番号を静的に割り当てられるかの分類ですが、畳み込みはノードの**値**がコンパイル時定数であることを要求します。`Span[$"Count: {Count}"]` はSSCに属しますが、値が定数でないため畳み込みの対象になりません。

畳み込み対象のタグは allow-list、すなわち curated タグ ∪ void タグ ∪ カスタム要素名から、テキストの解釈が通常要素と異なる `pre` / `textarea` / `iframe` を除いたものです。`AddContent` に渡した値はBlazorがエスケープしますが `AddMarkupContent` はしないため、テキストと属性値のエスケープは直列化器の責務になります。`Html.Raw` は畳み込みから除外します。既に1フレームであり単独で畳んでも得が無く、隣接する run へ混ぜるのは危険なためです(`Raw("<i>")` のような不均衡な文字列は、run 全体を1回でパースするときに後続の兄弟を `<i>` の内側へ入れてしまいます)。

値がマークアップを往復できない場合も畳みません。除外は復帰(CR)・NUL・孤立サロゲート・先頭のU+FEFFの4つです。

定数であっても、**文字列でない値は畳みません**(#158)。整形が従うカルチャをコンパイラが知り得ないためです(整形がいつどこで起きるかは付録E.2)。`3.5` が `en-US` で `"3.5"`、`de-DE` で `"3,5"` としてDOMへ届くのに、コンパイラはどちらになるかを知り得ません。畳めば片方が markup へ焼き込まれ、同じ値が「周囲が静的かどうか」で違う文字列になります。除外の代償は畳み込みの取りこぼし1回です。

例外は2つあります。**定数 `null`** は、文字列でもそれ以外でも `AddAttribute` が属性ごと省略するため、markup 側も何も書かないことで一致します。**定数 `bool`** は整形すべきものを持たないため、markup が両方の結果を厳密に表現できます(`true` は `name=""`、`false` は属性そのものの省略)。`.Attr(name, bool)` が非文字列の唯一の綴りであるのはこの理由によります(`DESIGN.md` §4.1 と #158)。クラスチャネルは連結で畳むため、ここでも定数文字列だけを受け付けます。

4つの除外それぞれの乖離の形、2つの例外を裏づける実測、および一致が確認できて掃き出した文字クラスは付録Eに置きます。

`ForEach` の content 根は畳みません。`SetKey` はマークアップフレームへ付けられないためです((B) 参照)。これを守っているのは発行側が content 根へ渡すキーの有無であり、独立した述語を置いていないので、両者が食い違う余地はありません。吸収するフレームが1つしかない run も畳みません。形だけ変えて、何も減らないからです。

**畳み込みは出力を変えずにコード経路を変えます。** 畳み込まれたマークアップと、要素経路が `HtmlEncoder` を通して書き出す出力は `&` `<` `>` `"` について同一です(それがDOM等価性の要件そのものなので当然そうなります)。したがって**出力に対するアサーションだけでは、畳み込み経路を通ったことを示せません**。畳み込みが静かに止まっても、そのテストは通り続けます。畳み込みを検査するテストが出力と併せて何を固定しなければならないかは、`CONTRIBUTING.md` §Conventions the code must uphold にあります。

**コンポーネントの fragment スロット**: `RenderFragment` 型のパラメータは、スカラー値を持たずノードツリーを
持ちます。そのため `ComponentParameter`(スカラー)とは別チャンネル(`ComponentSlot` / `ComponentSlotNode`)へ
格納します。発行されるフレーム幅は `1 + Parameters.Length + Σ(1 + 内容のフレーム幅)` で、スロット1つが
`AddComponentParameter` 1回とその内容の幅を消費します。

ラムダ内部のシーケンス番号は外側の平坦なカウンタを継続し、独立したシーケンス空間を作りません。
スロットのフレームは呼び出し元ではなく**子コンポーネントのフレーム列**に属します。BlazorCodeFirst のジェネレータは
常に `AddComponentParameter(seq, "ChildContent", (RenderFragment)(...))` を発行する側です。
fragment を直接 invoke するかどうかは、渡し先コンポーネント(手書きでも Razor 生成でも)が `AddContent` に
渡すか自分で呼ぶかの問題です。前者は Blazor のリージョンが隔離しますが、後者はリージョンが張られず、
我々の番号がホスト自身のフレームと隣接します。0 から振り直すとホストの低い番号と衝突し、コンポーネントが
再生成されて状態が失われます(実測)。平坦継続が厳密に安全側です。これは Razor と同一の挙動で、
リージョンで包んでも解決しません(リージョンはホストのフレーム列における隣接関係を変えないため)。

**ジェネリックな fragment スロット**: `RenderFragment<TContext>` 型のパラメータは `.Template` で受けます。
名前が `ChildContent` の場合は角括弧でも受け、そちらはコンテキストを使わない綴りと同じものを発行します。
発行するのは、`TContext` を取る外側のラムダと `RenderTreeBuilder` を取る内側のラムダを重ねた2段の式です。
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
`[ViewPart]` の引数と同じ**穴**としてテンプレートに記録され、展開時に生成名が差し込まれます。穴の位置は
解析時にパラメータの `ISymbol` から決まるため、同じ綴りの別物(同名のフィールド、内側のラムダが再宣言した
同名の変数)は書き換わりません。ただし `ISymbol` と `TextSpan` はこの解析呼び出しの内側に閉じ、テンプレートへ
渡るのは書き換え後の文字列だけです。ジェネレータのインクリメンタルモデルは不変・値等価なレコードと
プリミティブと文字列だけで構成する必要があり、シンボルやスパンを持ち込めばキャッシュの等価判定が壊れます。

逆向きの衝突も2つ塞いであります。作者が `__bcf_context_*` という名前を自分で宣言していれば
`__bcf_authored_context_*` へ改名し、生成引数が作者の非静的メンバーを覆い隠す位置では `this.` を補います。

**(E) 非属性のフレーム装飾。入力: `.Key` / `.Ref` / `.RenderMode` / 出力: 属性フレーム群の後ろに置かれる、属性ではないフレーム**

`.Key`(Razorの `@key`)、`.Ref`(`@ref`)、`.RenderMode`(`@rendermode`)は、所有ノードの属性へは合成されません。(A)の畳み込みにも(D)の静的畳み込みにも参加せず、`RenderTreeBuilder` の専用の呼び出しへ落ちます。装飾は3つ、落ちる先の呼び出しは4つです(`.Ref` が受け手ごとに割れるため)。4つは互いに性質が違い、その違いがそのまま発行規則を決めます。

| 綴り | 呼び出し | シーケンス | フレーム | 付け先 | `null` |
| --- | --- | --- | --- | --- | --- |
| `.Key` | `SetKey(object?)` | 消費しない | 積まない(開いているフレームのキーフィールドを書く) | 要素/コンポーネント | 早期return |
| `.Ref`(要素) | `AddElementReferenceCapture(int, Action<ElementReference>)` | **1つ消費** | 積む | 要素のみ | — |
| `.Ref`(コンポーネント) | `AddComponentReferenceCapture(int, Action<object>)` | **1つ消費** | 積む | コンポーネントのみ | — |
| `.RenderMode` | `AddComponentRenderMode(IComponentRenderMode?)` | 消費しない | 積む | コンポーネントのみ | 早期return |

フレームを積む3つは、**その所有ノードの属性・イベント・バインド・スロットをすべて発行し終えた後、子より前**に置かなければなりません。`RenderTreeBuilder` の `AssertCanAddAttribute` と `AssertCanAddComponentParameter` は直前の非属性フレームの種別を見ており、参照キャプチャやレンダーモードのフレームを積んだ後に属性を足すと `InvalidOperationException` になります。コンポーネントではスロットも `AddComponentParameter` として積まれるため、「パラメータの後」はスカラーとスロットの両方の後を意味します。`SetKey` だけはフレームを積まず親フレームを書き換えるだけなので、この規則の外にあり、`ForEach` のキーと同じく `OpenElement` / `OpenComponent` の直後に出します((B))。

```csharp
// 入力(設計時のC#式)
Div.Class("tab").Key(tab.Id)[Span[tab.Label]]
Component<Editor>().Param(c => c.Text, _text).RenderMode(RenderMode.InteractiveServer)
```

```csharp
// 出力(生成コード)
__b.OpenElement(k,   "div");
__b.SetKey(tab.Id);                                       // シーケンスを消費しない
__b.AddAttribute(k+1, "class", "tab");
__b.OpenElement(k+2, "span"); __b.AddContent(k+3, tab.Label); __b.CloseElement();
__b.CloseElement();

__b.OpenComponent<Editor>(m);
__b.AddComponentParameter(m+1, "Text", _text);
__b.AddComponentRenderMode(RenderMode.InteractiveServer);  // 属性の後、シーケンスを消費しない
__b.CloseComponent();
```

`FrameWidth` を増やすのは `.Ref` だけです。ただし `.Key` も要素のフレーム数を動かし得ます。`SetKey` はマークアップで表現できないため、`.Key` を持つ要素は畳み込み不可となり、定数だけで書かれていても(D)の1フレームに畳まれず自前のフレーム列を出します。同じことが `.Ref` にも当てはまります。コンポーネントは元から畳み込みの対象外なので、`.RenderMode` はどちらにも効きません。幅を定めるのは発行そのものであるという(D)末尾の規則はここでも変わらず、これら3つのために独立した幅計算を足してはいけません。

同じノードに同じフレーム装飾を2つ書くことは、3つとも「書いたとおりにならない」形で壊れます。`SetKey` は親フレームのキーフィールドを上書きするため後に出したほうだけが残り、`AddComponentRenderMode` は逆に `Renderer.FindCallerSpecifiedRenderMode` が最初の `ComponentRenderMode` フレームを返すため先に出したほうだけが効き、参照キャプチャは2つのフレームが積まれて両方のActionが発火します。どれも作者の書いた優先順位ではないため、BCF3033 で拒否します。`ForEach` のキーと content 根の `.Key` が衝突する形は、報告の層が違うため BCF3032 が別に見ます。

---

## 3. メモリレイアウト

### 3.1 SSC経路: 中間表現ゼロ

SSC(および Transplantable)経路の実行時像は、静的シーケンス定数を伴う `RenderTreeBuilder` 命令の直列実行です。生成物の形式はRazorコンパイラの出力と同じであり、UI記述に由来する中間オブジェクト(要素ツリー、ビルダー、`params` 配列)はヒープに生成されません。マーカー型 `View` は空の `readonly struct` であり、実行時に到達不能です。

SSC経路のアロケーション特性は、これにより等価なRazorコンポーネントと同等になります。`DESIGN.md` §7.1 の実測値であって、予測ではありません。残存するアロケーション源はBlazor自体に由来するものに限られます。イベントハンドラのデリゲート/クロージャ、`RenderTreeBuilder` 内部のフレーム配列(再利用される)、補間による一時文字列(`ISpanFormattable` 経路で部分的に緩和)です。

### 3.2 Opaque経路: フラグメント内包 `View`

Opaque経路でのみ、`View` は実体を持ちます。この場合の `View` は `RenderFragment` への参照を内包する軽量ハンドルであり、ヒープ割り当ては内包フラグメントの構築分に限られます。コストとしては `RenderFragment` を手書きで合成した場合と同等です。

```csharp
public readonly struct View
{
    internal readonly RenderFragment? Fragment;   // SSC経路では常に null(到達不能)
    internal View(RenderFragment fragment) => Fragment = fragment;
}
```

`implicit operator View(RenderFragment?)` がこのフィールドを構築します。`View` が実体を持つ唯一の綴りがこれであり、設計時表層(`Html` / `ElementView` / `Decorations`)のメンバーはすべて既定値を返すため、表層から組まれた `View` はフラグメントを持ちません。この非対称がBCF3030 の根拠です(付録A、付録B.11)。

生成コードは利用者のアセンブリに置かれるため `internal` フィールドを読めません。読む経路は `BlazorCodeFirst.CompilerServices.ViewRuntime.FragmentOf` の1つだけで、Razor の生成コードが `Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers` を呼ぶのと同じ位置づけです。発行は `AddContent(seq, RenderFragment?)` 1フレームで、`OpenRegion` は書きません。この呼び出しに対してBlazor 自身がフラグメント用のリージョンを開くためで、`RenderFragmentContentNode` が依拠しているのと同じ挙動です。

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

この順序の要点は、状態遷移がフレーム列生成に先行しなければならない(状態遷移 → 生成)という一点にあります。これは単一方向データフローの強制であり、`RenderView` の実行中に状態遷移を発生させてはならないことを意味します。現行のソースレベル実装では「設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)内での状態変更禁止」に対応し、違反は診断BCF3001となります。`Button` のonClickラムダ(`DeferredEventHandler`コンテキスト)はレンダリング中に走らず、イベント後に実行されるため除外されます。任意のメソッド呼び出し経由の副作用の完全な検出は保証しません(§1.1 BCF3001注記参照)。`[ViewPart]` 本体への同等の検証は将来拡張候補であり、この初期契約には含めません。

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

BlazorCodeFirstは実行時メタデータ分析・動的ディスパッチを排除します。全パラメータバインディング(`Component<T>().Param(...)` を含む)は、Source Generatorが生成する静的セッター経由で行われます。`Param` の式引数はSGが構文解析してセッター生成にのみ利用し、式木(`System.Linq.Expressions`)のランタイムコンパイルは行いません。**生成コードが `System.Reflection` / `System.Linq.Expressions` を呼ぶ箇所は0です。** 生成コードが呼び出すフレームワークの側にはリフレクションを通る経路が2つあり、いずれも本節後段の切り分け(契約は自身が生成するコードまで)の内側にあります。`Component<T>().Param` の `ComponentProperties.SetProperties` と、enum を束縛したときの `BindConverter.ParserDelegateCache`(`MakeGenericMethod` で `ConvertToEnum` を取り出す、#307)です。後者はトリムを生き延びることを実測しました(`TrimmedOutputTests`、費用は付録E.2)。

さらに、設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)と設計時APIは、いずれも実行時に到達不能であるため、ILトリマーがこれらを丸ごと除去できます。ここでいう設計時APIとは、`Html`・`Decorations` の全メンバーと、設計時慣性型 `View` / `ComponentView<T>` / `ElementView`(付録A、BCF3014)の全メンバーです。UI記述のソースコードはバイナリサイズに寄与しません。実行時に評価するコードファースト方式では得られない性質です。除去は `TrimMode=full`・`ILLinkTreatWarningsAsErrors=true` を有効にした状態で、`System.Reflection.Metadata` のMethodDef走査により確認できる設計です。トリムテストはコンポーネントとレイアウトの双方(派生型の `Body`/`Chrome` と基底の抽象ゲッター)についてこれを検査します。

リフレクションベースのバインディングを持つ同等構成との比較で、AOTコンパイル後のWasmペイロードサイズを約20〜30%削減(予測値)と見込みます。この予測値は、(a) BlazorCodeFirst構成、(b) リフレクションバインディング構成、(c) 素のRazor構成の3系統のベンチマークにより確定値へ置き換えられます。素のRazor構成との比較ではほぼ同等となる見込みです。

BlazorCodeFirstのトリミング/AOT適合契約が対象とするのは、自身が生成するコード(リフレクション不使用の`RenderView`、実行時に到達不能な設計時API、`ComponentView`ビルダー)がトリミングで除去されることまでです。`Component<T>().Param(...)` によるコンポーネント埋め込みでは、パラメータが実行時に適用される段で、フレームワーク側のリフレクションベース `[Parameter]` バインダー(`ComponentProperties.SetProperties`)が到達可能になります。これはBlazor SDKのトリミングプロファイルが担う範囲であり、BlazorCodeFirst自体の責務ではありません。トリムテストハーネス(`tests/BlazorCodeFirst.TrimTestApp`)は、Blazor SDKのプロファイルを持たない素のコンソールアプリです。その性質上この1点のフレームワーク側 `IL2072` が表面化するため、`ComponentProperties.SetProperties` のみに限定した抑制(`ILLink.LinkAttributes.xml`)を適用しています。

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
| シーケンス番号管理         | コンパイラによる静的割当          | SGによる静的割当(SSC)+ リージョン分離(Transplantable/Opaque) | 作者はシーケンス制御を意識不要          |
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

> **その診断の役割が「作者が単独では読み解けないコンパイルエラーの原因を名指すこと」であるなら、その診断は Source Generator が報告しなければならない。** アナライザーとして実装した場合、診断が発火すべき条件そのものがアナライザードライバを停止させるため、原理的に到達不能になる。

BCF1001 はこの規則に違反していました(#76)。`partial` の欠落は `RenderView` の非生成を意味し、それは宣言レベルのエラーである CS0534 を必ず発生させるため、アナライザーとしての BCF1001 は実ビルドで一度も報告され得ませんでした。診断すべき条件が診断自身を抑止していたことになります。BCF1001 は生成器報告へ移されています。同じ理由で BCF1003 / BCF1005 は当初から生成器報告であり、CS0534 と共に出力されます。

副次的な帰結として、**宣言エラーを1つ含むコンパイルでは、そのプロジェクトのアナライザー診断が BlazorCodeFirst 以外(CA/IDE 規則を含む)もすべて消えます**。これは BlazorCodeFirst 固有の性質ではありませんが、非 partial なコンポーネントはこの落とし穴にはまる最も容易な経路であり、その意味でも BCF1001 を生成器から即座に報告する価値があります。

現行の報告経路は、BCF3001 が `RenderMutationAnalyzer`、BCF3029 が `InertSurfaceAnalyzer`、それ以外はすべて `BlazorCodeFirstGenerator` です。アナライザーである2つは、いずれも発火形状がコンパイル可能であるためにそうできています。BCF3001 は状態変更を含む設計時表現、BCF3029 は読み手のいない位置に書かれた設計時APIで、どちらも型検査を通るのでアナライザードライバが動きます。新しい診断を追加する際は、その発火形状がコンパイル可能かどうかを先に判定してください。

この節の内容は文書上の約束にとどまりません。テストで固定されています。`tests/BlazorCodeFirst.DiagnosticTests` が `tests/diagnostic-fixtures` の各プロジェクトを実 MSBuild でビルドし、SARIF ログから「どの診断が、どの位置に報告されたか」を検証します。同一の CA1050 違反型を全フィクスチャに含めることで、2つを同時に固定しています。宣言エラーのあるコンパイルではアナライザー診断が消えることと、そのエラーがないコンパイルでは報告されることです。`DiagnosticDescriptors` の全記述子は、この層で網羅されているか、理由付きの除外リストに載っているかのいずれかである必要があります。

次節の表そのものも同じテストプロジェクトが検証します。`DiagnosticTableTests` が A.1 の表を読み取り、`DiagnosticDescriptors` と双方向で突き合わせます。記述子があって行が無ければ失敗し、行があって記述子が無い場合も、実装に先行して仕様化されている理由を `DiagnosticExpectations.DocumentedWithoutDescriptor` に記録していない限り失敗します。その登録は実装時に記述子と入れ替わり、入れ替え漏れは「理由を失った例外」として別のテストが落とします。種別列も記述子の `DefaultSeverity` と照合されるため、診断の severity を変えることは表を変えることでもあります(記述子を持たない行は照合対象外です)。

### A.1 診断一覧

| ID     | 種別    | 内容                                                                                  |
| ------ | ------- | ------------------------------------------------------------------------------------- |
| BCF1001 | Error   | 設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)の override を宣言するクラスが `partial` として宣言されていない(同一クラスへ `RenderView` を生成できない)。BlazorCodeFirstベースを継承するだけで override を宣言しないクラス(中間abstract基底、基底が既に宣言している葉、再abstract化)、および `RenderView` を手書きしているクラス(生成物が無いため `partial` は不要)は対象外。ネストクラスは BCF1005 が優先する(`partial` を足しても解決しないため)。生成器が報告する(理由はA.0)  |
| BCF1002 | Error   | `[ViewPart]` の静的展開が成立しない。宣言位置では、Source Generatorのサポートする静的展開契約を満たさない場合に報告する。満たさないのは次の形である。拡張メンバー(`DESIGN.md` §4.3、#203)、非静的、ジェネリック、ジェネリック型に含まれる、1つの `return` へ到達しない本体(2つ目の `return`、ネイティブの制御構文、生成器の予約名を持つローカル。受理する形は §2.3 Transplantable と同一で、読む実装も1つである)、`View` も `SlotView` も返さない、`params`・参照渡し・`ElementView` 型のパラメータ、`SlotView` を返さない宣言の `View` 型のパラメータ、生成コードから名指せない型のパラメータ、静的にシーケンス可能でない本体。呼び出しサイトでは次の3条件で報告する。(1) 当該メソッドのソース宣言が現コンパイルに無い(メタデータのみ)。定義は現コンパイルの構文から `ForAttributeWithMetadataName` で収集され、ILは本体構文を持たないため、参照先プロジェクトやNuGetパッケージの `[ViewPart]` は常にこれに当たる。(2) 再帰的な展開がサイクルを形成する。(3) 本体が参照する `private` / `protected` メンバーへ展開先から到達できない。第三の位置として、コンポーネント自身の設計時表現(`Body` / `Chrome`)の本体も同じ検査を読む。生成コードから名指せない構文(ローカル関数、生成コードのスコープが包まない位置で宣言されたローカル、範囲変数、ラベル)への参照がこれに当たり、包む位置の規定は §2.3 にある。メッセージの主語は位置ごとに分かれ、`[ViewPart]` は `ViewPart method 'X'`、設計時表現は `The Body design-time expression of 'C'` である。後者はメソッドではないため、前者の文言では作者が自分のファイルに無いものを探すことになる(#361) |
| BCF1003 | Error   | 設計時表現(`Body` / `Chrome`)が静的にシーケンス可能な部分集合へ分類できない。Opaque と Transplantable の経路が入ったため、`View` 返却呼び出しとブロック本体の `ForEach` コンテンツはこの診断ではなく、それぞれ BCF2001 / BCF3030 と BCF3004 が見る。残るのは、呼び出しでも設計時構文でもない式である。保存された `View` の読み出しがその形にあたる |
| BCF1004 | Error   | 設計時表現(`Body` / `Chrome`)の override が、ジェネレータの翻訳できないゲッターを宣言している。受理する形は、1つの `return` へ到達するゲッターである。その手前にはローカル宣言文と式文を並べられ、書かれた文はフレーム発行の手前へ移植される(§2.3 Transplantable)。したがって残る形は4つで、2つ目の `return`、ネイティブの `if` / `foreach` / `switch`、生成器の予約名(`__bcf_` 接頭辞と `__builder`)を持つローカル、そして本体を持たない自動プロパティである。前3つは `ForEach` の `content` が同じ理由で拒む形と同一であり、読む実装も1つである。書き直すか、`RenderView` を手書きする。再abstract化(`abstract override`)は対象外。実装部を持たない partial プロパティも対象外(CS9248 が原因を名指す) |
| BCF1005 | Error   | ネストしたクラスが設計時表現を宣言している。生成コードは外側の型宣言の連鎖を再現できないため、トップレベルの型へ移す必要がある |
| BCF2001 | Info    | 静的に展開できない `View` 返却呼び出しを検出。返された `View` が内包する `RenderFragment` で描画され、当該領域の静的差分最適化が失われる。正確性は変わらない。対象は、呼び出し先のソース宣言が現コンパイルに無いか、あってもその本体が設計時表層を参照していない場合である。参照している場合はBCF3030が止める。`AddContent(seq, RenderFragment?)` を発行する `RenderFragmentContentNode` は仕様上のOpaque経路だが、書かれた側が既に `RenderFragment` であり呼び出しの分類に届かないため対象ではない。#32 の `ComponentSlot` も対象外で、`AddComponentParameter` と静的採番済みラムダのみで構成される完全なSSC経路である。**計測できない残余**: 参照先アセンブリの `View` 返却メソッドが設計時表層で組まれていた場合、その `View` は実行時に空であり何も描画しないが、ソース宣言が無いため判定できず、この診断が出る。`DESIGN.md` §4.3 はアセンブリを越える再利用をコンポーネントへ導いている |
| BCF3001 | Error   | 現行実装では設計時表現(`BodyComponentBase.Body` または `ChromeLayoutBase.Chrome`)本体内での状態変更(単一方向データフロー違反)。初期検出範囲: コンポーネントインスタンスメンバーへの直接書き込み(代入/複合代入/インクリメント/デクリメント)。遅延ハンドラ引数(入れ子ラムダを含む)内は除外。除外対象はイベントデコレーション(`.OnClick` 等のイベント短縮形と `.On`)のハンドラ引数と `.Bind` のセッター引数であり、これは名前の列挙ではなく `KnownSymbols` の分類そのものから導かれる。`.Bind` のゲッター引数はフレーム生成中に評価されるため除外しない。任意の副作用の完全検出は保証しない。`[ViewPart]` 本体への適用は将来拡張候補 |
| BCF3002 | Warning | `ForEach` の `key` セレクタが要素の恒等性を保証しない可能性(インデックスベースキー等)。`key: null` と書かれた場合は問う対象のキーが無いため報告しない。この診断は書かれたキーについての警告であって、書かないことについての警告ではない(#172) |
| BCF3003 | Error   | キーを持つ `ForEach` の `content` が単一の要素/コンポーネントを根に持たず、キーを適用できない(根がリージョンになる裸の `if`/`ForEach`、`Fragment`、`Raw` 等)。内側を容器要素で包む(例: `Div[...]`)必要がある。`key: null` の `ForEach` とその糖衣であるスプレッドは `SetKey` を発行しないため対象外で、そこでは `Fragment` / `Raw` / 裸の `If` が根に立てる。この規則が存在する理由は `SetKey` の付け先が要素かコンポーネントのフレームに限ることであり、付けるキーが無ければ制約の根拠も無い(#172) |
| BCF3004 | Error   | `ForEach` の `key` がインライン式ラムダでも書かれた `null` でもない、または `content` が生成器の受け付ける形でない。`key` の本体は `SetKey` へ移植されるため式であることを要する。`key` の不在は構文として読む。生成器が移植するのは書かれた本体であって実行時の値ではないため、`null` を保持する変数を渡した形はインライン式ラムダではなくこの診断のままである(#172)。`content` が受け付けるのは3つで、インライン式ラムダ、末尾の `return` を1つだけ持ちローカル宣言文と式文のみを並べたブロック本体ラムダ(§2.3 Transplantable)、単一引数で `View` を返すメソッドグループである。メソッドグループは反復変数を1つ渡す呼び出しとして読み替えられ、他の呼び出しと同じ3分岐(静的展開 / BCF3030 / Opaque)へ落ちる。複数の `return` とネイティブの制御構文はそれぞれ独自のシーケンス空間を要するため対象外。構築済みデリゲート(`new Func<T, View>(M)`)も対象外で、呼び出しサイトで呼び出し先を名指せない |
| BCF3005 | Error   | `Component<T>()` のパラメータ束縛(`.Param` / `.Template` / `.Bind`)のセレクタが単純なプロパティ選択(`c => c.Prop`)でない(キャスト/メソッド呼び出し/捕捉変数のメンバー等) |
| BCF3006 | Error   | `Component<T>()` のパラメータ束縛(`.Param` / `.Template` / `.Bind`)の対象が settable な `[Parameter]` プロパティでない(実行時 throw を防ぐためコンパイル時に拒否) |
| BCF3007 | Error   | `Component<T>()` のチェーンが同一プロパティを複数回バインドしている。`.Param` / `.Template` / `.Bind` と角括弧の子コンテンツのすべてを数える(Blazorは最後の値のみ適用するため重複はコンパイル時に拒否) |
| BCF3008 | Error   | 装飾(`.Class`/`.Attr`/型付き属性ショートカット/`.OnClick`/`.On`)が単一要素を開くノード(要素ヘルパ/`Element`)以外に書かれている。装飾は `ElementView` の拡張である。したがってレシーバが `View`/`ComponentView<T>`(`If`/`ForEach`/`Fragment`/`Raw`/`[ViewPart]`結果/`Component`、および子を与え終えた要素)の場合は、`Decorations` に対するオーバーロード解決が失敗する。`ComponentView<T>` のレシーバには例外があり、`.Key` と `.RenderMode` は `Decorations` ではなく `ComponentView<T>` 自身が宣言するため解決し、この診断の対象にならない(§2.7(E))。`Component<T>()[…]` は `View` を返すので、子を与え終えたコンポーネントへの `.Key` は他の装飾と同じくここで拒否される。外部から渡された `RenderFragment` もレシーバとして受理する。`View` へ暗黙変換されるものの、拡張メソッドのレシーバは恒等/参照/ボクシング変換しか取らずユーザー定義変換を適用しないため、同じく解決に失敗し、作者の誤りは `Fragment`/`Raw` を装飾した場合と同一である。翻訳に失敗した設計時表現を走査し、この失敗したチェーンを検出して報告する(型システムが挙げるCS1929は宣言段階の打ち切りにより作者へ届かないため。§2.2) |
| BCF3009 | Error   | `Element` のタグ引数が非空のコンパイル時定数文字列でない(宣言性・予測可能性のため)。実行時のタグに経路を与える案は付録B.14 で退けた |
| BCF3010 | Error   | 同一要素上で属性またはイベントが複数回バインドされている(属性チャネル内の重複は後勝ちで前が死に、属性チャネルとイベントチャネルにまたがる同名バインディングは両方が生き残って二重発火する。いずれも書いたとおりにならないため拒否)。畳み込まれる `class` のみ例外で、その例外に収まらない `.Bind("class", …)` との共存はBCF3024が見る |
| BCF3011 | Error   | `.Attr` の名前 / `.On` のイベント名 / `.Bind` の属性名とイベント名が非空のコンパイル時定数文字列でない(宣言性・タイポ検査・class畳み込み判定・重複検出の前提)。実行時の名前と属性スプレッドに経路を与える案は付録B.14 で退けた |
| BCF3012 | Error   | `Component<T>()` の型引数がジェネレータ実行時に解決できない。同一プロジェクト内の `.razor` コンポーネントはRazorコンパイラ自身がソースジェネレータであるため相互に出力が見えず、常にこの状態になる。参照先プロジェクト/NuGetパッケージの `.razor` と手書きC#コンポーネントは正常に解決する。タイポや `using` 漏れの場合は同じ位置に CS0246 も報告される |
| BCF3013 | Error   | `Component<T>()[…]` で子コンテンツが与えられているが、`T` がそれを受け取れる `ChildContent`(settable な `[Parameter]`、型は `RenderFragment` または `RenderFragment<TContext>`)を持たない。ジェネリックな場合、角括弧は文脈を捨てる外側のラムダを伴って束縛するため対象外である。`ChildContent` 以外の名前を持つジェネリックなフラグメントは角括弧では束縛できないため、その名前だけを持つ型に角括弧を与えればこの診断となる |
| BCF3014 | Error   | 設計時慣性型(`View` / `ComponentView<T>` / `ElementView` / `SlotView`)がジェネリック `.Param` の値位置に渡された |
| BCF3015 | Error   | body 内の値式で、生成コードへ安全に移植できない未解決の型参照 |
| BCF3016 | Error   | void要素に子が与えられている。対象はHTML Living Standardのvoid elements 13要素である(`area` / `base` / `br` / `col` / `embed` / `hr` / `img` / `input` / `link` / `meta` / `source` / `track` / `wbr`)。curatedヘルパーと、タグを非空の定数で受けた `Element` の双方を見る。静的SSRは閉じタグを出力し、HTMLパーサが子を兄弟へ押し出すため、prerenderとinteractive描画で異なるDOMになる(理由と計測は `DESIGN.md` §4.1)。要素タグについての単項述語で判定するため、(親, 子) で決まる同種の破れは対象外。未知タグとカスタム要素も対象外 |
| BCF3017 | Error   | `.Bind` の getter が本体式を持つインラインラムダでない(ブロック本体ラムダ/メソッドグループ等)。getter の本体式は属性値と `CreateBinder` の現在値の双方へ移植されるため、式として取り出せなければならない。setter 側にこの制約はない(`EventCallback` へ渡すだけで本体を取り出さないため) |
| BCF3018 | Error   | getterだけを渡す形の `.Bind` で getter の本体が代入可能でない。許可されるのはメンバーアクセス(`_name` / `_form.Name` / `Model.Items[0].Title`)と要素アクセス(`_dict["k"]`)で、対象が setter を持つこと。呼び出し・演算(`() => _name.ToUpper()`)、get-only プロパティ、`readonly` フィールドは拒否する。ローカル変数・パラメータ・`ForEach` の反復変数そのものへの直接代入も拒否する(`Body` はプロパティゲッターでありローカルはレンダリングごとに死ぬため、書き戻しが次のレンダリングに残らない)。反復変数の**メンバー**(`o.Title`)は元の要素を書き換えるので許可する。setter を明示する形へ誘導する。要素とコンポーネントの双方で発火し、同じ形でも引数の個数は面によって違う(要素は3と4、コンポーネントは2と3)ため、形の呼び分けに個数を使わない |
| BCF3019 | Error   | `.Bind` / `.On` のイベント名が `on` で始まらない。Blazor のイベント属性名は常に `on` で始まり、そうでない名前は属性として静かに追加されてハンドラが一度も発火しない。`.Bind` は属性名とイベント名の2つの文字列を隣り合って取るため、取り違えがこの検査で止まる |
| BCF3020 | Error   | `ComponentView<T>.Bind` の対象に対応する `{名前}Changed` パラメータが `T` に無い、または `EventCallback<TValue>` でない。要素側と違いコンポーネント側は名前を導くが、導けるのは型シンボルで確かめられるからであり、`{名前}Changed` は存在と型が合わなければこの診断で拒否する。もう一方の `{名前}Expression` はこの診断の対象ではなく、宣言されていて型が合うときにだけ発行し、そうでなければ無言で省く(Razorと同じ挙動。宣言しない型に対して常に発行すれば束縛自体が失敗するため) |
| BCF3022 | Error   | `Component<T>().Template` の文脈付きオーバーロード(`Func<TContext, View>` を取る形)の content がインライン式ラムダでない(メソッドグループ/匿名メソッド/ブロック本体ラムダ等)。生成器はシーケンス対象の式と、生成する文脈変数を代入するパラメータシンボルの双方を必要とするため、いずれも取り出せない形は拒否する。位置は content 引数の全体で、書き直す対象が引数の形そのものだからである。引数が0個または2個以上のラムダはこの規則の対象外で、`Func<TContext, View>` へ変換できずC#が先に拒否する。BCF3004 と同じ制約を `ForEach` ではなくテンプレートに置いたもの。番号が BCF3021 を飛ばしているのは、BCF3021 が撤回済み(付録B.5)で再利用しないためである |
| BCF3023 | Error   | クラスチャネルへ畳み込まれる装飾(`.Class` / `.Attr("class", …)`)の値が、解決されたオーバーロードにおいて `string` ではない。`class` はクラスチャネルへ畳み込まれ、このチャネルは装飾を1つの値へテキストとして連結するため、連結できる値の型は `string` だけである。条件はこのチャネルの要件であって、条件を満たさないオーバーロードの列挙ではない(`ClassChannel.Admit` は `string` かどうかを問い、それ以外を拒否する、#193)。今日この規則に届く綴りは `bool` オーバーロード(#158)と値を書かない `.Attr("class")` の2つだけであり、`.Attr` が取る値の型がその2つしかないことによる。非 `string` のオーバーロードが後から増えれば(#171 #178)解析器に触れずに同じ門で止まるため、メッセージは規則を到達可能にしている型を仮定せず、見つけた型を名指す(#223)。`bool` の場合、意味はそこで持たないだけでなく1つに定まらない。要素が持つクラス装飾が1つならチャネルは値をそのまま出すため `AddAttribute(int, string, bool)` が束縛され、`true` は `class=""` すなわちクラス一覧の消去になる。2つ以上なら `+` で連結するため同じ `true` が文字列化され `class="a True"` になる(いずれも実測、#159)。同じ綴りがチェーンの別の場所にある個数で二通りに翻訳される、生成器自身の畳み込みから生じた翻訳の破れである。対象は名前が `class` の場合だけで、`.Attr("disabled", flag)` は `bool` オーバーロードの本来の用途であり対象外。位置は値引数で、書き直す対象がそちらだからである(条件付きクラスは文字列側の条件式、`.Class(active ? "on" : null)` として書く)。値を書かない綴り `.Attr("class")` も同じ規則に届く。裸の綴りは存在を表すが、チャネルはテキストとして連結するため存在には連結すべきものが無い(#178)。指す値引数が無いため、この場合の位置は装飾名であり、メッセージも合成された `bool` ではなく綴りそのものを名指す。作者が書いていない値の型を告げれば、コードではなくコンパイラ自身の手順を説明することになるためである |
| BCF3024 | Error   | クラスチャネルへの装飾(`.Class` / `.Attr("class", …)`)と、属性名が `class` の `.Bind` が同じ要素に載っている。チャネルは装飾を何個持っても1フレームへ畳み込むが、`.Bind` はそこへ加わらず束縛ループから自分のフレームを出すため、要素は `class` 属性を2つ持って発行される。BCF3010が唯一通す名前に届いた重複であり、その例外はチャネルが畳み込むことで買ったものであるから、名前ではなくチャネルに対して問う。`class` に届く3つ目の綴りである `.Bind` だけが畳み込まないので、この名前の他のすべての装飾と衝突し、それ以外とは衝突しない(#188)。どちらのフレームが残るかは規定しない。prerenderのマークアップではHTMLパーサが先勝ちで解決し、interactive描画ではDOMへの後勝ちの書き込みになるため、答えが1つでないからである。報告に必要な事実はそこではなく、両方のフレームが欲しかったと読める書き方が無いことである。位置は後から書かれた側の装飾名で、BCF3010と同じく検査が走る装飾を指す |
| BCF3025 | Error   | `Slot` が、呼び出し側のコンテンツを受け取らない宣言の中に書かれている。または、コンテンツを取ると宣言した `[ViewPart]`(戻り値 `SlotView`)が `Slot` を1回以外の回数だけ書いている。`Slot` は呼び出し側が角括弧で与えたコンテンツを置く位置の印であるから、置くべきコンテンツが存在しない場所では意味を持たない。コンポーネントの `Body`/`Chrome` は角括弧を受け取らず、`View` を返す `[ViewPart]` は角括弧なしで呼ばれる。0個は呼び出し側が渡す義務のあるコンテンツを捨て、2個は1つの角括弧から2回発行するため、いずれも書いたとおりにならない。位置は、置き場所の誤りは `Slot` 自身、個数の誤りは宣言の識別子で、いずれも作者が直す対象を指す。この表層で新設が必要な診断はこれ1つだけである。角括弧の書き忘れ(`Div[Card("x")]`)、装飾(`Card("t").Class("x")`)、#176が退けた位置引数の綴り(`Card("t", P["x"])`)はいずれもC#が先に拒否する。`SlotView` が `View` への変換を持たないためであり、`Div["x"].Class("y")` がCS1929である仕組みと同じである(#34, #176) |
| BCF3026 | Error   | 装飾の位置に書かれた名前を `BlazorCodeFirst.Decorations` が宣言していない。レシーバは要素を開くノード(`ElementView`)であり、破れているのは名前だけである。BCF3008 とはレシーバではなく名前について問う点で異なり、`KnownSymbols.DeclaresDecorationNamed` の真偽で排他になるため、同じ走査が両方を分類する。対象は2つの形である。束縛しない綴り(`Div.Clas("card")`)は、C#が挙げる CS1061 が宣言段階の打ち切りにより作者へ届かない(A.0)。`ElementView` を受けて `ElementView` を返す利用者宣言の拡張メソッドは束縛するためC#のエラー自体が無く、BCF1003 だけが残っていた。戻り値が `View` の宣言は対象外で、装飾ではなく包む形であるため BCF1003 のままとする。位置は装飾名で、書き直す対象がそちらだからである |
| BCF3027 | Error   | 要素を単純名で書いた位置に、`BlazorCodeFirst.Html` の外で宣言されたものが届いている。`using static BlazorCodeFirst.Html;` は curated ヘルパーを単純名スコープへ持ち込むが、近い位置の宣言がその探索に勝つ。届いた先は4通りある。メンバーの場合、その型が添字可能であれば式は正当なC#のまま、要素ではなくそのメンバーへの添字呼び出しになる(`string Data` に対する `Div[Data["Heading"]]` は `Data` の文字添字に `"Heading"` を渡す)。型・名前空間・メソッドの場合は束縛そのものが失敗する。C#はそれぞれ CS1503 / CS0119 / CS0118 / CS0021 を挙げるが、いずれも本体束縛段階の誤りであるためA.0の打ち切りにより作者へ届かず、残るのはBCF1003の「静的に解析できない構文」だけである。#127 は型を対象外とし、その根拠を「C#の `CS0119: 'Table' is a type, which is not valid in the given context` が遮蔽している宣言を名指す」に置いたが、その前提は実測されておらず誤りであった(#266)。4形状は作者にとって1つの誤り(`Html` より近い宣言に単純名が届いた)であり直し方も1つ(`Html.<名前>`)であるため、番号は1つとし、何が名前を取ったかはメッセージの引数で運ぶ(BCF3028 と同じ形)。候補が2つ以上の曖昧な探索は対象外である。どの宣言が取ったとも言えず、ヘルパー自身が候補に含まれうるためである。curatedヘルパーを22から100へ広げた#99はこの破れを作ってはいないが、頻度を稀から日常へ移した。`Code` `Data` `Label` `Summary` `Source` `Input` `Option` `Form` `Select` はいずれも通常のBlazorパラメータ名である。位置は遮蔽されたレシーバの識別子で、`Html.Data` と限定するのがその位置だからである。遮蔽している宣言を追加位置としては付けない(記述子はいずれも追加位置を持たない) |
| BCF3028 | Error   | イベントハンドラの引数型が、そのイベントが配送する型ではない。2つの形を1つの記述子で扱い、理由はメッセージの引数で運ぶ(BCF1002 と同じ形)。作者にとっては「イベントに対して引数型を取り違えた」という1つの誤りであり、直し方も同じである。C#が束縛できたかどうかは作者が引いていない区別であり、それで番号を割らない。(1) 束縛する食い違い(`.On("onclick", (KeyboardEventArgs e) => …)`)は成功経路の装飾アームから報告する。両辺とも既に手元にある。イベント名はBCF3011が定数を要求済みであり、引数型は生成器が式を見る前にC#が解決した型引数である。ラムダの内側は見ない。(2) `where TArgs : System.EventArgs` に反する型(`.On("onclick", (int x) => …)`)は束縛しないので、失敗経路の走査から報告する。位置はBCF3008 と同じで、理由もA.0と同じである。C#が挙げる CS0311 は宣言段階の打ち切りにより作者へ届かず、実測では CS0534 と BCF1003 だけが残った。判定は等価ではなく代入可能性である。`EventCallback<TArgs>` はイベントの引数オブジェクトを `TArgs` へキャストして受ける。基底型は受け取れ、兄弟型は受け取れない(`.On("onclick", (EventArgs e) => …)` は正当)。対応表の読み取り元は2つで、`Microsoft.AspNetCore.Components.Web.EventHandlers` に付いた `[EventHandler]` と、現コンパイル内で同属性を持つ型(カスタムイベントの登録経路)である。前者を先に引くため、後者が同じ名前を上書きすることはない。`[EventHandler]` の対応が無いイベントは報告しない。この表層のタグは文字列であり、他に照合先が無いためである。`Components.Web` を参照しないコンパイルは対応表を持たないので、検査ごと無言で飛ばす。参照アセンブリは走査しない(#155がその費用を見積もれないため)。そこに置かれた登録は読まれず、これは残余として記録する。位置はハンドラ引数で、書き直す対象がそこに書かれた引数型だからである。Razorは同じ `[EventHandler]` から同等の検査を持つ。したがってこの検査が無いことは、Razorに劣ることを意味する(`DESIGN.md` §4.1) |
| BCF3029 | Error   | 設計時APIの式が、それを読む設計時表現の外に書かれている。設計時APIとは §2.1 と §5 が挙げる集合、すなわち `Html` と `Decorations` の全メンバー、慣性型 `View` / `ElementView` / `SlotView` / `ComponentView<T>` の全メンバー、および `[ViewPart]` メソッドである。慣性型の値は空であり、生成器は式を読むだけであるから、この位置では出力も生まれずイベントハンドラも配線されない。型検査は通り、何かを組み立てたように見え、症状は出力が無いことだけである。発火条件は2つで、(1) その式を囲む最内の宣言(メソッド・プロパティ・アクセサ・ローカル関数・ラムダ)が慣性型を返さない、(2) その値が慣性型のフィールドまたはプロパティへ代入されていない。(1) は `Body` / `Chrome` / `[ViewPart]` を位置の許可リストなしで除外する。3つはいずれも慣性型を返し、`If` / `ForEach` のコンテンツラムダも同じであるから、慣性型を返す位置を新設しても書き足すものが無い。列挙を持たないことがここでの要点であり、検査の宿主集合を人間の列挙にした場合の費用は `FailurePathScanners` の remarks が記録している(#100)。(2) は `View` 型フィールドへのキャッシュを除外する。§2.3 が分類するのは呼び出しであって保存ではないため保存形は今日のところ未予約であり、設計が将来開けたい扉をErrorで閉じるのは道具が違う。初期化子も代入と同じに扱う。どちらで書いたかは作者が引いた区別ではなく、初期化子は囲む宣言の戻り値型を持たないので (1) が届かないからである。判定するのは代入先の型であって値の型ではない。`object` 型のフィールドがボクシングで受けても `View` を読み戻せる者はいないため、それはここで開けている保存ではない。ローカルは除外しない。ローカルは宣言と共に死に、返されるか捕捉されるなら (1) が既に除外している。作者自身の `View` 返却宣言は対象外である。それは `DESIGN.md` §5.3 が残す Opaque の綴りであり、付録B.11(b) がその消去を退けている。`[ViewPart]` の付け忘れに答えるのはBCF3030 であり、宣言ではなく呼び出しサイトを見る(#260、付録B.11)。この行が対象外とするのは宣言の側だけであって、その宣言を呼ぶ式は BCF3030 が受け持つ。報告は書かれた連鎖ごとに1つで、`Html.Div.Class("card").OnClick(DoThing)[Html.Span["hello"]]` は設計時APIへの参照を5つ含むが誤りは1つである。位置は最も外側の設計時式の全体で、誤っているのはその中身ではなく式の位置だからである(BCF3014と同じ据え方)。宿主は `InertSurfaceAnalyzer`。この形はコンパイルが成立するのでA.0の禁止は及ばず、BCF3001が先例である。BCF3001と別の型に置くのは、BCF3001が設計時表現の内側だけを見るのに対しこちらは外側だけを見るためで、逆向きの2つの範囲検査が1つの型にあると、後の変更がどちらの条件に触れたのか読めなくなる。登録は構文全体ではなく `Invocation` と `PropertyReference` の2つの操作種別で、最初の連言は#68が予定していた名前の前置フィルタではなく型判定である。#68はこの選択を計測してから決めることを要求しており、計測が示したのは問いの立て方が違うということだった。同じ2種を登録して何もしないアナライザーと、構文全体を登録して何もしないアナライザーは同じ費用に収まる。つまり登録の形は費用の所在ではなく、前置フィルタが狭めようとしていたのは無料な側だった。費用が出るのはコールバックであり、そこで効くのは連言の順序である。数値と方法は#68にあり、この表とアナライザーには置かない。時間は機械依存で、`DESIGN.md` §7.1 が時間を公表しないのと同じ理由が効くためである |
| BCF3030 | Error   | `View` を返す非 `[ViewPart]` メソッドの呼び出しで、その呼び出し先の本体が設計時表層を参照している。設計時表層は慣性であり、`View` に実体を入れる経路は `implicit operator View(RenderFragment?)` だけであるから、表層から組まれた `View` は実行時に必ず既定値になる。したがってこの呼び出しは何も描画しない。C#の型検査は通り、症状は出力が無いことだけである。BCF3029 が「読み手のいない位置に書かれた設計時式」を宣言の側から見るのに対し、これは呼び出しの側から同じ破れを見る。対象とする設計時表層はBCF3029 の行が定める集合と同じで、判定は同じ `KnownSymbols` の分類から引く。判定に使うのは「本体が設計時表層を参照しているか」だけであり、BCF1002 の静的展開契約は呼び出しサイトで走らせない。契約違反の内訳は、作者が属性を付けたあとBCF1002 が宣言の位置で名指す。直し方は2つある。静的メソッドなら `[ViewPart]` を付ける。インスタンスメソッドは `[ViewPart]` になれない(BCF1002)ためコンポーネントにする。対象は `View` を返す通常メソッドの呼び出しに限る。`ElementView` と `ComponentView<T>` は `View` への変換が既定値を返すので原理的にフラグメントを持てず、この経路にも載らずBCF1003 のままである。ソース宣言が現コンパイルに無い呼び出しも対象外で、そちらはBCF2001 が見る。位置は呼び出し式の全体で、書き直す対象がそれだからである。付録B.11 がこの診断へ改訂された経緯を記す |
| BCF3031 | Error   | `.Bind` に `format` が書かれているが、束縛値の型を受ける format 付きの変換器をフレームワークが宣言していない。`string format` を取る `CreateBinder` と `BindConverter.FormatValue` のオーバーロードは `DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly` とその `Nullable<>` の8型にしか存在しない。それ以外の型に format を書くと、生成コードの中で呼び出しが束縛できずCS1503になる。A.0 のとおりそれは作者に届かないため、呼び出しサイトで止める。受理する型の集合は`Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions` のメタデータから引き、ここでは著述しない。§4.1 の基準(フレームワークが正準として出荷している表を規則で写すだけなら検査する)と、BCF3028 が `[EventHandler]` を引いている先例に一致する。生成器が発行する呼び出しは `FormatValue` と `CreateBinder` の2つで、format を取るオーバーロードの型集合は両者で一致する。片方だけを読んでいる事実を放置しないため、2つの表の一致は `BindFormatTableSyncTests` が固定する(`KnownSymbolsSyncTests` が curated 表とvoid 表を双方向に固定しているのと同じ据え方)。表を解決できないコンパイルでは検査ごと無言で飛ばすが、`ElementView` を宣言するアセンブリが `Microsoft.AspNetCore.Components` を参照するため、`.Bind` を綴れるコンパイルは必ずこの型を見られる。防御であって想定される経路ではない。カルチャは対象外で、この表層が束縛できるどの型も書ける。制限があるのは format だけである。位置は format 引数で、作者が消すか書き直す対象がそこだからである。メッセージは規則に到達した型を仮定せず、見つけた型を名指す(BCF3023 と同じ形) |
| BCF3032 | Error   | キーを持つ `ForEach` の content 根が、自身にも `.Key` を書いている。`SetKey` が同じフレームへ2回呼ばれ、後に出したほうがキーフィールドを上書きするため前が黙って死ぬ。どちらが残るかは発行順の帰結であって作者の書いた優先順位ではないため拒否する。判定は根の種別を解く走査(`KeyabilityResolver.ResolveRootKind`)が同時に答え、BCF3003 と同じく到達非依存で定義あたり1回報告する。`key: null` の `ForEach` とその糖衣であるスプレッドは `SetKey` を出さないため対象外で、そこでは根の `.Key` が唯一のキーとして立つ。BCF3003 と同じ形について同時に発火することはない。根がリージョンになる形は `.Key` を書ける受け手を持たず、そちらはBCF3008 が見るためである |
| BCF3033 | Error   | 同一の要素またはコンポーネントに、同じ非属性のフレーム装飾(`.Key` / `.Ref` / `.RenderMode`)が2つ書かれている。3つとも書いたとおりにならず、壊れ方はチャネルごとに違う(§2.7(E))。属性チャネルとイベントチャネルの重複を見る BCF3010 とは別のIDである。3010 は属性名・イベント名についての規則であり、名前を持たないこれらのチャネルを同じ行へ足すと1つの行が2つの規則を述べることになる。位置は2つ目の装飾名で、消す対象がそちらだからである。メッセージは装飾名を引数に取る(BCF3026 と同じ形) |
| BCF3034 | Error   | `.RenderMode` が、`Microsoft.AspNetCore.Components.RenderModeAttribute` を派生する属性を宣言している型に書かれている。その属性は作者が自分で宣言したものである。フレームワークの `RenderModeAttribute` は抽象で、具象の派生を1つも出荷していない(Razorは `@rendermode` ディレクティブごとに派生クラスを生成する)。基底クラスが持つ場合も対象で、判定は基底の連鎖を遡る。フレームワークが同じ読み方をするためであり、派生型で止めるとこの診断が置き換える実行時throwへそのまま通す。実行時に `ComponentFactory` が `InvalidOperationException`(`The component type '…' has a fixed rendermode of '…', so it is not valid to specify any rendermode when using this component.`)を投げる。判定はコンポーネント型についての単項述語であり、属性はメタデータにも載るため参照先アセンブリの型でも同じく決まる。宣言形が固定である以上、呼び出しサイトの指定はどう書いても通らないので、書ける形とそうでない形が型ごとに分かれる。`DESIGN.md` §4.1 の基準(検査が依拠する表をこのリポジトリで著述して維持することになるかどうか)を満たす。ここで引くのは1つの属性の有無であり、維持する表は無い |

## 付録B: 検討した代替アーキテクチャと不採用理由

**B.1 Interceptor方式(C# 14)**: `Body` を実行時に評価し、各設計時API呼び出しサイトをInterceptorで静的シーケンス付き実装へ置換する方式。呼び出しサイト置換自体は成立しますが、次の3点により採用しませんでした。(a) 実行時評価を前提とするため、装飾チェーンの合成型に対する統一戻り値型が構成できません(C#に不透明戻り値型が存在せず、`ref struct` はインターフェースへ変換できない)。(b) `[InterceptsLocation]` の位置指定子がソース変更のたびに再計算され、ビルドパイプラインが位置データに敏感になります。(c) 本方式(全体生成)が採用可能である以上、部分置換に固有の利点がありません。

**B.2 ランタイム `ref struct` ツリー方式**: 要素を `readonly ref struct` としてスタック上に構築し、実行時に `Render` を再帰呼び出しする方式。GC回避には有効ですが、次の3点により採用しませんでした。(a) 可変個の子要素を受け取る手段がありません(`ref struct` は配列・`params` に格納不可、ジェネリックオーバーロードはアリティ上限を持つ)。(b) B.1と同じ戻り値型問題があります。(c) 静的サブツリーのキャッシュと両立しません(`ref struct` はフィールド格納不可)。本方式(生成コードによる直接発行)は、同じゼロアロケーション特性を型システム上、無理なく達成します。

**B.3 `ChromeLayoutBase` を `BodyComponentBase` から派生させ `SetParametersAsync` で介入する方式**: レイアウトを通常のBlazorCodeFirstコンポーネントと同じ基底型に載せる方式。Blazorが渡す `Body` パラメータを `SetParametersAsync` で抜き取ってから、残りのパラメータを基底へ転送します。当初はこの案を採る判断をしていましたが、実装して実行した結果、成立しないことが確認されたため撤回しました。残りのパラメータを転送する唯一の公開手段は `ParameterView.FromDictionary` です。ところがその列挙子は `cascading: false` を固定値で返します。そのため、cascading値のみを受け取るプロパティに対して `ComponentProperties.SetProperties` が例外を投げます(*"The property 'X' … cannot be set explicitly because it only accepts cascading values."*)。影響は `[CascadingParameter]` に限りません。この検査は `CascadingParameterAttributeBase` を基準とするため、`[SupplyParameterFromQuery]` も同じ理由で落ちます。認証テンプレートが標準で用いる `[CascadingParameter] Task<AuthenticationState>` も、レイアウトで受け取れなくなります。加えてナビゲーションごとに `RenderTreeFrame[]` を確保します。採用した方式(`ChromeLayoutBase : LayoutComponentBase`)は、Blazorが名前で要求する `Body` を正しい名前のまま継承します。`SetParametersAsync` に付与された `[DynamicDependency]` トリマーヒントもそのまま引き継ぐため、プラットフォームのパラメータ結線と競合しません。教訓として、プラットフォーム側のパラメータ結線に介入する方式は本設計では採りません。

**B.4 `[ViewPart]` メソッドに `〜AsFragment` 兄弟メソッドを併生成する方式**: 各 `[ViewPart]` に対し `RenderFragment` を返す静的メソッドを生成する方式。既存の `.razor` から `@Widgets.StatusBadgeAsFragment(status)` の形で、コードファーストUIの断片を埋め込めるようにします。`DESIGN.md` §6.1 と `CONTRIBUTING.md` の不変条件が当初これを約束していましたが、実装されたことは一度もなく、#144 で撤回しました。理由は4点です。(a) この方式が満たそうとした要求は、コンポーネント粒度ですでに満たされています。`.razor` からBlazorCodeFirstコンポーネントをタグとして名指すことに同一プロジェクト制限はなく、`site/BlazorCodeFirst.Site/App.razor` が現にそうしています。Razorが解決するのは作者が書いたクラス名であり、生成物は `RenderView` の本体だけだからです。(b) 生成される兄弟メソッドは実体を持つため参照元アセンブリから呼べてしまい、「静的展開は宣言のソース構文を要するため同一コンパイル内に限られる」という `[ViewPart]` の境界(§4.3、BCF1002)に例外を作ります。同一の属性が「呼び出しサイトへ展開される同一コンパイル内の仕組み」と「公開APIを生やす宣言」という二つの顔を持ってしまい、`[ViewPart]` と `Component<T>()` の使い分けを説明できなくなります。(c) 実装は次の3つを新たに必要とします。含有型への `partial` 要求(現行の `[ViewPart]` にはなく、`site/BlazorCodeFirst.Site/Pages/NotFoundContent.cs` は非partialの `static class` です)、`〜AsFragment` の名前衝突に対する診断、`private` な `[ViewPart]` に対する無用な兄弟の扱いです。さらに、同一プロジェクトの `.razor` が生成された静的メソッドを呼べるかは未検証です。これはBCF3012を生んだのと同じ「ソースジェネレータは互いの出力が見えない」領域にあり、不成立なら本方式は参照先アセンブリからしか使えず、その場合は(a)のコンポーネント経路が常に優ります。(d) 得られるのはコンポーネントより細かい断片粒度の埋め込みのみで、代替手段は `BodyComponentBase` で包むクラス1つです。教訓として、再利用の単位も相互運用の単位もコンポーネントとし、`[ViewPart]` は同一コンパイル内の分割手段に徹します。

**B.5 同一要素の2つ目の `.Bind` をBCF3021で拒否する方式**: 1つの要素に双方向束縛が2つ以上現れたら、2つの名前がいずれも空いていてもコンパイルエラーとする方式。#71で実装して出荷しましたが、#162で撤回しました。根拠としていたのは「`SetUpdatesAttributeName` の記録先は要素であり、2つ目の束縛が1つ目の再同期先を上書きする」という主張です。この主張は#71自身の最終レビューで誤りと指摘されましたが、指摘は解消されないまま規則だけが出荷されました。#162で実測した結果は次のとおりです。`SetUpdatesAttributeName` が名前を書くのは要素ではなく直前の属性フレームです。生成コードは束縛ごとに属性フレーム・イベントフレーム・`SetUpdatesAttributeName` の順で出します。したがってここでいう直前の属性フレームはその束縛自身のイベントフレームであり、読み戻す `RenderTreeUpdater.UpdateToMatchClientState` が見るのもイベント自身のフレームです。つまり書き込み先と読み出し元は同一のフレームであり、そのフレームが束縛ごとに別であるため、同一要素の2つの束縛は互いの再同期を壊しません(§2.7(A))。残る選択は、別の根拠を立て直して規則を維持するか、規則を落とすかでした。落としたのは `DESIGN.md` §4.1 の原則によります。この表層が検査するのは妥当性ではなく翻訳の破れであり、2つの束縛の背後に破れはありません。Blazorはこの形を通常の差分検知で正しく描き、動機となる形も実在します(双方向のプロパティを2つ以上持つWeb Component、`DESIGN.md` §4.1)。同じ原則が付録Dの計測済みの残余を未検査のまま置いている以上、何も破らない形だけを拒否する位置は取れません。撤回は欠番の解放ではありません。プレビュービルドでこのエラーに当たった読者が番号で検索したとき、別の規則が同じ名前を着ていてはならないためです。`AnalyzerReleases.Shipped.md` が空である以上、`CONTRIBUTING.md` のID再利用禁止はこの番号に届きません。そこで `DiagnosticExpectations.RetiredIds` と `DiagnosticTableTests.RetiredIds_AreNeitherDeclaredNorDocumented` が、BCF3021が記述子にも付録Aにも戻らないことを機械的に固定します。教訓として、プラットフォームの挙動についての主張を根拠に置く診断は、その挙動を実測してから出荷します。根拠への指摘を解消しないまま出せば、指摘のほうは記録に残らず規則だけが残ります。

**B.6 void性を `ElementView` の型で表現する方式**: void要素13タグのcuratedヘルパーが、インデクサを持たない `VoidElementView` を返す方式。`Img["child"]` はBCF3016ではなくCS0021になり、表層はHTMLに居場所のない形を差し出さなくなります。§4.1の系譜のうち3つがこの経路を採っています。Giraffe.ViewEngineの `XmlNode` は `VoidElement` ケースを持ち、`br []` がリストを1つ取るのに対し `div [] []` は2つ取ります。Falco.Markupは `ParentNode` と `SelfClosingNode` に分け、`_hr [ _class_ "divider" ]` と書きます。TyXMLは多相バリアントの内容モデルに符号化しています。#179で検討し、採用しませんでした。理由は4点です。(a) 得られるのは形だけです。どちらも今日すでにコンパイルエラーであり、BCF3016はこの誤りのために書かれた文面を持つのに対し、CS0021は「インデクサを適用できない」としか言いません。表層は読みやすくなり、診断は読みにくくなります。(b) コストは `Decorations` に落ちます。装飾は22個すべてが `ElementView` を受けて `ElementView` を返す形です(`Decorations.cs`)。チェーンを通してvoid性を保つには、void型のために全体を複製するか、自己参照制約を持つビルダーインターフェースで全体をジェネリックにするかのいずれかを要します。どちらも大きく、しかも新しい装飾が必ず触るファイルに払われるため、#156と#178がそれぞれ高くつきます。(c) 検査は消えません。`Element("br")["x"]` は文字列経路で同じタグに達し、そこには変えるべき型が存在しないため、BCF3016はいずれにせよ必要です。型が覆うのはこの検査のcurated側の半分だけになります。§4.1は両経路が単一のタグ文字列に落ちてから同じ表を引くことで構成上一致すると述べており、片方の経路しか覆わない型規則はその逆の配置です。(d) ミラーとしての論拠は見かけより弱いものです。`DESIGN.md` §4.1が引く境界はタグ単独から決定できるかであり、void性はその内側にあります。設計はこれを型の領分から外し、検査の領分として扱っています。本項は付録Dと同じ意味での記録であり、再検討には上の4点が答えていない理由を要します。

**B.7 クラスチャネルの区切りを条項側へ寄せる生成規則**: 各項を `((t) is { } __c ? " " + __c : "")` の形で出し、区切りを項自身に持たせる方式。#177 の設計で採用しましたが、外部レビューの指摘により実装前に撤回しました。アロケーションが増えるためです。非nullな項では余分な空白がそもそも出ないため、`.Class("card").Class(_variant)` のような最頻形で、何も得ずに1回から2回になります。`class` はこの表層で最も多く書かれる属性であり(#177)、その値が非定数であることは普通です。同じレビューは代案も出しました。定数プレフィックスを条件の両腕へ畳む `((t) is { } c ? "card " + c : "card")` です。これは1アロケーションで空白も出ませんが、1つ目の項が条件付きのとき畳めるプレフィックスが存在しないため、`.Class(a ? "card" : null).Class(b ? "active" : null)` の残余には届きません。採用したのは、生成クラスが自身のために持つ `private static` の join が実行時に `null` の項を飛ばす方式です(#236)。両方の残余に届き、非nullな項しか無い綴りでは連結演算子と同じ `string.Concat` 1回に落ちます。3形の実測で、変更前後のアロケーションが一致することを確認しています(`ClassChannelBenchmarks`)。教訓として、値の有無が実行時にしか決まらない規則は、実行時に判定する場所を1つ作るほうが、生成する綴りの側で場合分けするより安く済むことがあります。

その join を生成クラス側に置くか、`BlazorCodeFirst.Runtime` の1メソッド `JoinClasses(params ReadOnlySpan<string?>)` にするかは、#239 で問い直し、2026-08-14に実測しました(`ClassChannelBenchmarks` の join site 行)。周りのフレーム呼び出しは両案で同一であるため、join の式だけを計測しています。1メソッド側の本体は、規則を保つ最も速い綴り、すなわち `null` の項を落としてから残りを繋ぐ形にしました。区切りを織り込んでから連結する綴りは `n` 項に対して `2n-1` スロットを書いて読み直すためどの arity でも遅く、その綴りで測れば生成クラス側の勝ちを計測ではなく記録することになります。#239 のアロケーションについての予測は当たっています。`params ReadOnlySpan` の実引数バッファを呼び出し側がスタックに取るため、5形すべてで両案は同値でした(2項40 B、2項で片方が `null` は0 B、3項48 B、4項64 B、4項で2つが `null` は40 B)。時間は一方的ではなく、速い側が形で入れ替わります。生成クラス側が勝つのは、項が2つの形(7.17 ns 対 12.03 ns)と、項に `null` がある形です。後者の差は大きく、2項で片方が `null` なら 0.15 ns 対 2.46 ns になります。arity 2 のラダーは `null` 判定2つと `string.Concat` 1回であるためインライン展開され、片方が `null` なら残った項を返すだけになるからです。1メソッド側は、項を走査して詰め直す本体をどの形でも通ります。負けるのは `null` を含まない3項(22.58 ns 対 19.33 ns)と4項(26.80 ns 対 21.96 ns)で、差は14〜18%です。生成クラス側を採ったのは、勝つ側の形がこのチャネルで実際に書かれる形だからです。#236 がこの規則を作った動機は条件付きの項、つまり実行時に `null` になる項であり、そこがラダーの最も得意な形です。`site/BlazorCodeFirst.Site` は `.Class` を160箇所で書きますが同じ要素に2つ載せた箇所は無く、生成された12クラスのどれも join を持ちません。代わりに払い続けるのは #239 が数えた3つ、すなわち arity ラダー、`IndentedWriter.WidestClassJoin` を通る arity の受け渡し、ジェネリックコンポーネントでの型引数ごとの実体化です。公開表層の費用は #239 の見積もりより小さいものでした。生成コードが呼ぶ先の `BlazorCodeFirst.CompilerServices.ViewRuntime` は既に `[EditorBrowsable(Never)]` であるため、利用側のIntelliSenseに現れる費用は存在しません。この判断は arity の分布に賭けています。再検討には、`null` を含まない3項以上が普通に書かれることの測定が要ります。

**B.8 HTMLコアの上にオピニオンなレイアウト/コンポーネント層を重ねる方式**: `VStack` / `HStack` / `Card` / `Modal` / `PrimaryButton` といった語彙を、HTMLプリミティブへ展開するopt-inの第二パッケージとして与える方式。実装は事前にクラスを付けた要素を返す関数(`static ElementView VStack(int gap = 4) => Div.Class($"flex flex-col gap-{gap}");`)です。#74で検討し、採用しませんでした。理由は5点です。(a) 別アセンブリであることは `DESIGN.md` §4.1 の却下に答えていません。あの却下が退けているのは「ライブラリがレイアウトに二つの答えを持つこと」であって、二つ目がどのアセンブリで出荷されるかではありません。ファーストパーティのパッケージは存在した瞬間に公式の答えになります。ドキュメントがそれを実演し、新規の読者はHTML表層より先にそれを学びます。しかも `Card` はDOMに `Card` として現れないため、§8がDOMネイティブであることに帰している資産(アクセシビリティツリー、CSSエコシステム、DevToolsで読めること)の側から見た1:1の対応が崩れます。(b) この層はフレームワークに何も要求しません。上の `VStack` は既存表層の上の1行の静的メソッドであり、コンパイラ側にもランタイム側にも新しい機構を要しません。作者が自分のプロジェクトに数行で書けるものをファーストパーティで出荷することは、`PublicAPI.*.txt`・バージョニング・ドキュメント・後方互換義務を持つ第二の公開表層を、何も買わずに抱えることです。(c) CSSフレームワークを選ばされます。上のクラス文字列はTailwindのユーティリティです。選択肢は3つあり、いずれも取れません。Tailwindに依存する(§8がCSSエコシステムをそのまま使えると述べている主張を、作者の代わりに一つ選ぶことで裏切ります)、自前のCSSを積む(MudBlazor / Radzen / Fluent UIと正面から競合するコンポーネントライブラリという別プロダクトになり、そこに本ライブラリの優位はありません)、CSSなしでクラス名だけ出す(役に立ちません)です。(d) 例示のコードはそもそも動きません。`$"gap-{gap}"` は補間であるためTailwindのコンテンツスキャナから見えず、purgeされます。(e) 名前で逃げられません。`BlazorCodeFirst.UI` は「BlazorCodeFirstのUI部分」と読め、コアがUI層ではないことを含意する点で逆立ちしており、より重要なことに、`BlazorCodeFirst.*` という名前そのものが公式の答えとして読ませます。示唆的な先例はElmで、`elm/html` がコアであるのに対し、HTML/CSSの考え方ごと置き換えるレイアウト語彙 `mdgriffith/elm-ui` はサードパーティであり、別の名前を持ち、`elm/html` の上に重なるのではなく併存しています。この方式をいつか作るなら、その形(独自の名前・独自のリポジトリ・BlazorCodeFirstの *上に* 建てる)が誠実な位置取りであり、`DESIGN.md` §9が周辺構想を「本筋の設計とは独立した別プロダクトの検討事項」としている記述とも一致します。#74は当初、この判断を「再利用可能なラッパーにコンテンツを渡せないこと」の解消待ちとしていました。語彙を剥ぎ取った後に残る本当の需要は、ラッパー要素も実行時コストも持たない再利用可能でパラメータ化されたUIの断片であり、その機構は `[ViewPart]` としてすでに存在していたものの、当時は `View` 型のパラメータがBCF1002で拒否されていたためです。これは#176で着地しました(`SlotView` と `Slot`、`DESIGN.md` §4.3)。前提が揃った以上、この判断は保留ではなく不採用です。再検討には、`[ViewPart]` のコンテンツ経路では担えない具体的な事例が要ります。教訓として、第二の語彙が必要に見えるときは、まず一つ目の語彙に欠けている合成手段を疑います。

**B.9 `View` にインデクサを付け、部品の戻り値型を1つに統一する方式**: `View` 自身が `params ReadOnlySpan<View>` のインデクサを持ち、`[ViewPart]` の戻り値型を `View` だけにする方式。コンテンツを取る部品も取らない部品も `View` を返し、`Card("x")[P["本文"]]` は `View` のインデクサで通ります。動機は、作者が部品を宣言するたびに `View` と `SlotView` を選ぶ負担を消すことであり、§4.1の系譜が例外なく戻り値型を1種類しか持たないという観察がその後押しになります。2026-08-11に検討し、採用しませんでした。理由は5点です。(a) 他の系譜が1型で済む理由は戻り値型の側にありません。**組み込み要素の子チャネルがふつうの関数パラメータである**からです。Giraffe.ViewEngine / Falco.Markup / Feliz.ViewEngine は `XmlNode list` 相当の位置引数です。kotlinx.htmlは末尾ラムダ(`fun FlowContent.card(title: String, block: DIV.() -> Unit)`、戻り値は子を取る部品も取らない部品も `Unit`)。Plotは `@ComponentBuilder` クロージャ(`struct NewsArticle: Component { var body: Component }`)。いずれも自作部品は同じ型のパラメータを1つ宣言すれば組み込みと同じ形になり、再現すべき機構が存在しません。この表層の子チャネルはインデクサであり、C#ではメソッドが自分の呼び出し式にインデクサを生やせません。インデクサを持てるのは型だけであるため、部品に組み込み要素と同じ形を与えるには型が要ります。§4.3が角括弧を選んだ時点で2つ目の戻り値型はその帰結であり、独立に選び直せる項目ではありません。(b) 同じ問題を持つ唯一の先例が、同じ区別を引いています。§4.1の系譜のうち子の構文が独自の構成物であるのはOxpecker.ViewEngineだけで、そのCEメンバーは具体型ではなく `HtmlContainer` インターフェースの型拡張として定義されます。すなわち「子を取れるもの / 取れないもの」を型で区別しており、C#に写せば `ElementView` と `SlotView`(取れる)対 `View`(取れない)という現行の配置そのものになります。(c) 型が無償で閉じている規則が診断へ移ります。§4.3が挙げる3つは、角括弧の書き忘れ `Div[Card("x")]`、#176が退けた位置引数の綴り `Card("t", P["本文"])`、そして装飾です。これに加えて `Div["a"]["b"]`・`Fragment(…)["x"]`・`Raw(…)["x"]`・`If(…)["x"]` が新たにコンパイルを通ります。装飾だけは変わりません(装飾は `ElementView` の拡張であり、`View` にインデクサを足してもレシーバの型は動かないためです)。新設が必要な診断はBCF3025の1つから4〜6個になります。付録B.6が退けたのは型を増やして検査を減らす向きであり、本項はその逆向きに同じ交換レートで払う案です。(d) そのうち少なくとも1つは構文検査に届きません。`var v = Card("x"); Div[v];` は、直書きの `Div[Card("x")]` と違って、`v` がスロットを持つ部品の戻り値であることを構文から判定できません。この設計の解析は構文主導であり、SSCの外は諦めてBCF2001を出すと決めています(§5.3)。型は変数に付いて回りますが構文の検査は付いて回らないため、この経路は診断を書いても黙って空のコンテンツを描画します。付録Dが記録している未検査の残余を、いま型で閉じている領域から新たに作ることになります。(e) 交換の両側を同じ人が払います。減るのは部品の宣言ごとに戻り値型を1回選ぶことで、誤れば宣言位置でBCF1002(`View` 戻り値に `View` パラメータ)かBCF3025(`SlotView` 戻り値に `Slot` が無い)が即座に報告します。増えるのは呼び出しを書く側で黙って壊れた描画を受け取る余地です。どちらも作者が払うため、これはライブラリと作者の間の取引ではなく、必ず気づく費用と気づかない費用の交換になります。再検討には、(c)と(d)が挙げた書き方が実際には生じないことの測定を要します。C#の流儀として戻り値型の2択が不自然に見えるという観察はその測定に当たりません(§4.1)。

**B.10 `.Param` のセレクタをより短い綴りへ置き換える方式**: `Component<T>().Param(b => b.Label, label)` が、名前を与えるためだけに毎回ラムダを1つ書かせている点を縮める方式。#170で検討し、2026-08-11に採用しませんでした。まず、どの候補も超えられない壁が1つあります。呼び出しサイトで名指せるのは作者が書いた宣言だけです。ソースジェネレータは自分の出力を観測しないため、コンポーネントごとに生成したメンバー(`StatusBadge.Of(label: "x")`、生成した `.Label(…)` 拡張メソッド、生成したビルダー型)は、生成器が走っている間その呼び出しサイトで束縛しません。しかも失敗の質が悪く、参照先プロジェクトの生成メンバーはメタデータ経由で解決するため、別アセンブリのコンポーネントでは通り同一プロジェクトでは通らないという、BCF3012の非対称がBCF間の合成が主に使う向きで再生します。`Component<T>()` そのものも開いていません。`Div` が括弧を要さないのはプロパティだからであり、C#にジェネリックプロパティは無いため `Component<T>` はプロパティになれません。動かせるのは `.Param` の側だけであり、既存のC#構文に限れば候補は3つです。(a) 文字列名 `.Param("Label", label)`。要素側の `.Attr(name, value)` との一貫性が根拠に見えますが、論拠は逆向きです。`DESIGN.md` §4.1が属性側を文字列にしたのは、属性の語彙がそもそも開いていて閉じた集合として写せる対象が存在しないからでした。コンポーネントのパラメータ集合は宣言済みで閉じており、型も付いています。値の型照合はいまC#が無償で行っており(実測。`.Param(b => b.Compact, 42)` はCS0029/CS1662です)、文字列名にすればこれを新しい診断として書き直すことになります。`DESIGN.md` §4.3がマーカー型の名前付きチャネルを退けた理由 ── 位置引数ならC#のオーバーロード解決が無償で与えるものを作り直す ── と同じ形です。加えてIDEのリネームが追従しなくなり、これは `DESIGN.md` §1.4 がこの設計の実在価値として挙げる2つのうちの一方です。(b) オブジェクト初期化子 `Component(new StatusBadge { Label = label })`。型検査もリネームも保ちますが、設計時式の中に本物のコンポーネント実体を作ります。`DESIGN.md` §5.1は設計時APIの実体がすべて慣性であることを、同 §1.3は実行時のヒープ割り当てが原理的に発生しないことを述べており、どちらも破れます。#68(設計時APIを設計時式の外に書いても何も起きない件)も悪化します。いま何もしない式が、本当に割り当てるようになるためです。(c) 初期化ラムダ `Component<StatusBadge>(b => { b.Label = label; b.Compact = compact; })`。3つのうち唯一、型検査・リネーム・慣性のすべてを保ちます。それでも採らない理由は2点です。第一に `.Param` が消えません。`.Bind` はセレクタから `{名前}Changed` と `{名前}Expression` を導くためにセレクタを要し、`.Template` の文脈を読む形は引数自体がラムダであるため、どちらも代入の形を持てません。したがって代入形は `.Param` を置き換えるのではなくその隣に並び、同じことの二通りの綴りを持たないという `DESIGN.md` §4.1・§4.3 の位置に反します。第二に、`.Param` が束縛を呼び出し1つずつに分けることで構造的に閉じている検査が、診断へ移ります。ブロック本体には、単純代入でない文も `[Parameter]` でないプロパティへの代入も書けます。そのためBCF3005(セレクタが単純なプロパティ選択でない)とBCF3006(対象が settable な `[Parameter]` でない)がいま構文の形で見ているものを、文の集合に対して見直すことになります。そして、この方式が縮めようとしている繰り返しには別の答えがすでにあります。`[ViewPart]` で1回包めば呼び出しサイトから `Component` も `.Param` も消え、名前付き引数と省略可能引数がそのまま使えます(`DESIGN.md` §4.3)。属性は宣言ごとに1つであり、呼び出しサイトごとではありません。再検討には、`[ViewPart]` の包みでは担えない事例が要ります。C#の流儀としてラムダの繰り返しが冗長に見えるという観察はその事例ではありません(`DESIGN.md` §4.1)。

**B.11 `[ViewPart]` を属性なしで自動適用する方式**: `View` を返す静的メソッドを、属性の有無にかかわらず静的展開の対象とする方式。動機は、属性を書く手間と、付け忘れて黙って動的経路(§2.3 Opaque)に落ちる事故を同時に消すことです。2026-08-11に検討し、採用しませんでした。理由は4点です。(a) 展開できる集合は「`View` を返す静的メソッド」より狭いものです。BCF1002が列挙するとおり、1つの `return` へ到達する本体であること・ジェネリックでないこと・`params` や参照渡しや `ElementView` のパラメータを持たないこと・本体が静的にシーケンス可能であること等を要します。自動化とは、この条件を満たさない残りをどう扱うかを決めることであり、分岐は2つしかありません。(b) 満たさない宣言をエラーにすると、`DESIGN.md` §5.3が意図して残している逃げ道の綴りが消えます。解析できないコードを書きたいときは属性を付けずに書けば動的コンテンツとして通る、という経路が無くなり、代わりにopt-outの属性が要ります。注釈すべき側が入れ替わるだけで、属性は減りません。(c) 満たさない宣言を黙って動的経路へ落とすと、必ず気づく費用が気づかない費用に変わります。§2.7のとおり展開はフレーム列が呼び出しサイトへ直書きしたものと一致し、動的経路はリージョンで包んで実行時に `RenderFragment` を描くため、割り当ても静的最適化も違います。いまは作者が属性を書いた時点で「展開されるつもりだ」と表明しており、成立しなければBCF1002が宣言の位置で即座に報告します。自動化すると、本体にネイティブの `foreach` を1つ足しただけで黙って性能が落ちます。付録B.9(e)が退けたのと同じ交換です。(d) 対象メンバーの範囲を別に決めることになります。`Body` と `Chrome` は `View` を返すプロパティであり、インスタンスメソッド・ローカル関数・ラムダも候補になります。属性はこの集合を宣言の側で閉じており、自動化はその規則を作者が覚える側へ移します(`DESIGN.md` §4.1)。なお動機に挙げた付け忘れは実在の事故であり、それには属性を自動にするのではなく呼び出しサイトの診断で答えます。当初はBCF2001(Info)を予定していましたが、実装時に前提が誤っていることが分かりました。`View` に実体を与える綴りは `implicit operator View(RenderFragment?)` だけであり、設計時表層のメンバーはすべて既定値を返します。したがって表層から組まれた `View` はフラグメントを持たず、Opaque経路へ載せても何も描画しません。失われるのは最適化ではなく出力そのものであって、Infoが述べる事実と合いません。答えるのはBCF3030(Error)で、呼び出し先のソース宣言が読める場合にこれが止めます。読めない場合だけがBCF2001の対象として残ります(#260)。この配分でも本項(c)の交換は成立しません。作者は属性を書き忘れた時点で診断を受け取り、黙って落ちる経路はソース宣言の読めない呼び出しに限られます。再検討には、(c)の暗黙の劣化を作らずに済む3つ目の分岐が要ります。

**B.12 コレクションから子を作るための2つの代替**: #172が検討し、いずれも採用しませんでした。同Issueは子リストへコレクションを差し込む綴りを与える作業であり、着地したのは `key: null` と、その糖衣であるスプレッド `Ul[[.. proj]]` の2つです(§2.3 SSC-3)。

1つ目は、`IEnumerable<View>` を取るインデクサのオーバーロードを足して `Ul[proj]` と書けるようにする方式です。理由は3点です。(a) スプレッド形が同じことを書けるうえ、兄弟の子と混ざります。インデクサは1つの引数が子リスト全体であるため混ざりません。(b) `Ul[proj]` は、子を1つ置いたのか子の並びを置いたのかを呼び出しサイトで述べません。`..` は述べます。これは同じIssueがキーの不在を既定値ではなく書かれた `null` にしたときの基準と同じであり、`DESIGN.md` §4.2 が求めているものです。(c) 同じことに2つ目の綴りを足すのは `DESIGN.md` §4.1 が退ける交換であり、しかも子チャネルは4面(`ElementView` / `ComponentView<T>` / `SlotView` / `Fragment`)あるため、1つではなく4つ増えます。実装はランタイムAPIを一切増やしませんでした。再検討には、スプレッド形が実際には書かれない、または読めないことの測定が要ります。

2つ目は、`Select` 以外の `IEnumerable<View>` スプレッドをOpaque(BCF2001)へ縮退させる方式で、#172の起票時点ではこちらを予定していました。理由は2点です。(a) `Div[[.. _views]]` は保存された `View` の読み出しであり、付録AのBCF1003行が「残る形」として名指しているものそのものです。単数形の `Div[_view]` はBCF1003であるため、複数形だけをOpaqueへ通すと、単数より複数のほうが緩いという逆転が起きます。(b) フィールドは呼び出しではないためBCF3030が届きません。表層から組まれた `View` はフラグメントを持たないので、Opaque経路は黙って何も描画しません。付録B.9(e)とB.11(c)が退けているのと同じ、気づかない費用です。したがって `Select` 以外のスプレッドはBCF1003のまま置きました。再検討には、この形がフラグメントを持つ `View` でのみ書かれることの測定が要ります。

**B.13 属性名をメタデータへ載せて装飾を外部から宣言させる方式**: 属性名を属性引数へ載せた静的拡張メソッドを、装飾として認める方式。宣言は `[Decoration("hx-get")] public static ElementView HxGet(this ElementView e, string? value) => e;` の形をとり、呼び出しサイトは `.Attr("hx-get", value)` とまったく同じ形へ降ります。属性引数はメタデータに残るため、`[ViewPart]` と違ってアセンブリを越えます。公開されたHTMXパックが実際に動くということです。降ろした後が `.Attr` と同一であるため、BCF3010・BCF3023・BCF3024・§2.7 (D) はいずれも改造なしで届きます。#242で検討し、採用しませんでした。まず、`DESIGN.md` §4.1 が型付き装飾(`.Padding()`)を退ける理由は、ここには届きません。属性名をそのまま写す限り、覚え直しを強いる語彙にはならないためです。退ける理由は次の3点です。(a) 得られるのは反復の削減だけです。`.Attr("hx-get", url)` が同じ属性を同じフレームで出すため、失われる能力がありません。(b) 費用が属性チャネルに落ちます。ここには BCF3010 / BCF3011 / BCF3023 / BCF3024 が既に載っており、これに宣言形の契約と、契約違反を名指す診断が加わります。契約が要求するのは5つです。静的であること、`ElementView` の拡張であること、`ElementView` を返すこと、名前が非空の定数であること、値が `string?` / `bool` / 値なしの3形に収まることです。さらに BCF3029 が見る慣性集合(`Html` と `Decorations` の全メンバー)へ、参照アセンブリの `[Decoration]` 宣言を集める経路も要ります。その経路が無ければ、パックの利用者が `Body` の外で `e.HxGet("/x")` と書いたとき、出力は生まれず、診断も出ません。#242 はこの項目を数えていません。(c) 拡張点になるのが、規則を持たない列挙です。curatedな要素集合は除外6群という理由で定義されており、標準に要素が追加されればそれは自動的に候補になります(`DESIGN.md` §4.1)。属性ショートカットの7つ(`Href` / `Src` / `Alt` / `Id` / `Type` / `Title` / `Role`)にはその規則がありません。なぜ `Href` があって `For` や `Value` が無いのかは、規則では説明できません。属性の語彙が開いているためです。この集合は閉じたものとして固定されています(2026-08-14決定、#321、`DESIGN.md` §4.1)。閉じた集合を外部への拡張点にすれば、規則を持たない境界がそのまま拡張の仕様になります。Oxpecker.ViewEngine がHTMX / Alpine / ARIAを別パッケージで出荷している事実は、この判断には届きません。あちらは実行時に木を組み立てるため、`attr` を呼ぶ型拡張がエンジンに何も教えないまま成立します。開く決定が存在しない以上、開いた先例にもなりません。この事実が示すのは需要です。無料であれば人は属性パックを書く、ということであり、費用のかかる機構を正当化する事例ではありません。本体をインライン展開する変種は、検討するまでもなく落ちます。ILは本体構文を持たないためアセンブリを越えられず、公開パックが成立しないからです(付録B.4、`DESIGN.md` §4.3 と同じ理由)。再検討には、属性セットをパッケージとして配ることがこの表層の目標に入ることを要します。

**B.14 タグ名と属性名を実行時の値として受ける方式**: 3つの形をまとめて扱います。1つ目はタグを実行時に決める `Element(GetTagName())`、2つ目は属性名を実行時に組み立てる `.Attr($"data-{kind}", value)` です。3つ目が属性を辞書で渡すスプレッド `.Attrs(IReadOnlyDictionary<string, object>)`(Razorの `@attributes`)です。#320と#308で検討し、2026-08-14にいずれも採用しませんでした。まず、両Issueが障害と見ていたものは障害ではありません。#308は「属性の個数が実行時の値であるからスプレッドは静的にシーケンスできない」としていました。実際には `RenderTreeBuilder.AddMultipleAttributes(int sequence, …)` が辞書全体に対してシーケンス番号を1つ取り、それを各属性フレームへ渡します。個数がいくつであっても、シーケンス引数を消費する呼び出しは1回であり、後続ノードの番号も動きません(実測、`AttributeSplatMeasurementTests`)。タグも同じで、`OpenElement(seq, expr)` は静的な番号を1つ取るため、#320が先例として挙げた#17のリージョンすら要りません。ただしそのリージョンは属性には届きません。`OpenRegion` はビルダーの直前の非属性フレーム種別を `Region` にし、続く `AddAttribute` は例外になります(実測、同上)。#17が変えたのは要素とコンテンツの経路の費用であって、属性チャネルの費用ではありません。決めるのはシーケンスではなくクラスチャネルであり、退ける理由は5点です。(a) 畳み込みは検査ではなく翻訳です。見えない検査は黙ればよく、BCF3028 は現に対応表を持たないコンパイルで検査ごと飛ばします。畳み込みにその選択肢はありません。生成器はコンパイル時に値の行き先を選ぶほかなく、どちらを選んでも実行時の値によっては誤ります。自分のフレームへ出す側を選ぶと、名前が実行時に `class` であった要素は `class` フレームを2つ持ちます。スプレッドの無い要素では、これはBCF3024 が拒み、どちらが残るかを規定しないと述べている出力そのものです。スプレッドのある要素では答えが決まっており、その答えのほうが悪い形です。`CloseElement` が重複を解決して後のフレームだけを残すため、畳み込み済みの `class` は延長されるのではなく消えます(実測、同上)。`.Class("card").Class(_variant)` は、辞書が `class` を持った瞬間に両方とも出力から落ちます。チャネルの規則は連結であり(#236、§2.7(A))、その隣に置換という第二の規則が並んで、どちらが働くかをソースに書かれていないキーが決めることになります。(b) 実行時に畳み込む側は選べません。畳み込みはコンパイル時のテキスト連結であり、行き先が変わればフレーム数が変わります(チャネルへ入れば増えず、自分のフレームなら1つ増えます)。フレーム幅は装飾の個数だけで決まり値によって動かないという#234の規則に反し、差を吸収できるリージョンは上のとおり属性の位置に開けません。(c) BCF3010 が、要素の出力についての規則ではなく、名前の綴り方についての規則になります。この診断が拒むのは書いたとおりにならない出力です。スプレッドは重複を例外ではなく常態にし(呼び出し側が既定を上書きできることがこの機構の目的です)、しかもその重複をBlazorが黙って解決します。同じ壊れ方が、2つの名前を書けばコンパイルエラーになり、片方が辞書から来れば黙って後勝ちになります。(d) 出力に現れる名前がソースに現れなくなります。#244 が `.Data(name, value)` を退けたのはこの理由です。属性のショートカットはいずれも、出力する属性名をそのまま綴っています(`DESIGN.md` §4.1)。実行時の名前はその破れを完成させます。`$"data-{kind}"` も辞書も、出力に出る属性名をソースのどこにも書きません。その費用の実物が、#244の引いた `site/` の `data-theme-toggle` です。C#とブラウザ側のJSとPlaywrightのセレクタを、検索だけが結んでいます。(e) タグ側はクラスチャネルと重複検査のどちらにも触れませんが、失うものは同じ種類です。BCF3016 は今日、構成上すべての要素経路を覆っています。curatedヘルパーと `Element` の双方が単一のタグ文字列に落ちてから、同じ表を引くためです(`DESIGN.md` §4.1)。実行時のタグは、その表を引けないタグ文字列を作ります。付録Dが記録しているのは計測して選んだ残余ですが、実行時のタグが作るのは表層が新たに開ける穴です。付録B.6 が「型が覆うのはこの検査のcurated側の半分だけになる」ことを理由に型経路を退けた交換の、向きを変えただけの同じ交換になります。加えて §2.7 (D) の静的畳み込みがその要素で止まります。5点に共通するのは費用の質です。今日この形を書いた作者は、書いた位置でBCF3009 かBCF3011 を受け取り、定数の綴りへ書き直します。経路を開けば、受け取るのは黙って `class` を失った出力か、黙って飛ばされたvoid検査です。付録B.9(e)・B.11(c)・B.12 が退けているのと同じ、気づく費用と気づかない費用の交換です。判断の範囲は要素の属性チャネルです。コンポーネント呼び出しへ属性を渡す経路(#314)は別の問いで、そこにクラスチャネルはありません。再検討には、定数のタグと定数の属性名では担えない事例を要します。属性を辞書で受け取るラッパーを書きたいという観察はそれに当たりません。既知の属性集合は `[ViewPart]` の通常のパラメータで受け取れるため、事例になるのは集合が呼び出し側にしか分からない場合に限られます。

## 付録C: 開発時フォールバック案(解釈モード)

§2.6のツーリング検証で、特定環境においてSource Generatorの再実行がEnCに反映されないと判明した場合に限り、次のDEBUGビルド限定フォールバックを導入する余地を残します。

DEBUG構成では、設計時API群を慣性実装から実働実装(`View` に `RenderFragment` を構築して内包する)へ条件コンパイルで切り替え、`RenderView` の代わりに `Body` を実行時評価します。全体は単一のリージョン内で動的シーケンスを用いて描画されます。Hot Reloadは `Body` プロパティ本体の差し替え(EnC標準サポート)として自然に機能し、SGの再実行に依存しません。RELEASE構成では本仕様の生成コード経路のみが用いられるため、出荷物の性能・サイズ特性に影響しません。

本案は開発時と実行時で描画経路が二重化する複雑性を伴うため、§2.6のツーリング確認で必要性が示されるまで導入しません。

## 付録D: 検査しない翻訳の破れ(計測済み残余)

`DESIGN.md` §4.1 は、検査するのが妥当性ではなく翻訳の破れであることを述べます。そして境界を、検査が依拠する表をこのリポジトリで著述して維持することになるかどうかに置き、単項側の最初の対象としてvoid要素に子を与える形(BCF3016、付録A A.1)を挙げます。この基準は#155で改訂しましたが、本付録の除外はいずれも改訂後の基準でも除外のままです。どれもここで表を著述することになるためです。本付録は、そこで検査の外に置かれた残余の一覧です。§4.1 が列挙を持たないと述べている先がここであり、curatedタグに対する `KnownSymbols.CuratedTags`、void要素に対する付録A A.1 と同じ位置にあります。本付録が載せるのは破れであって、妥当でない出力ではありません。`Div.Href("/x")` のように、書いたとおりに出て両描画経路が一致する形は、検査しない点では同じでもここには載りません(`DESIGN.md` §4.1、#335)。

**これは作業の一覧ではありません。** 各項は計測の結果として選ばれた位置の記録であり、BCF3016を広げるためのto-doではありません。付録Bと同じく、再検討には新しい証拠と本付録の改訂を要します。「診断があったほうが親切だ」は証拠ではありません。

計測はいずれも2026-08-03、net10.0 / ASP.NET Core 10.0.10 / Chromiumです。

### D.1 単項側: BCF3016が覆わない破れ

要素タグ単独から決定できるにもかかわらず、BCF3016の対象ではない形です。BCF3016が覆うのは「その要素が子を持てるか」という一つの問いであり、以下はそれぞれ別の検査と別の直し方を要するため、どれもBCF3016には畳み込めません。

**要素の子を取る `textarea` / `title`**。これらは内容をテキストとして読みます。要素の子はページがパースされた時点で潰れ、`appendChild` でDOMを組んだ場合は残ります。`Textarea[Span["x"]]` の `value` は、prerenderでは `"<span>x</span>"`、interactiveでは `""` です。`Textarea` はcuratedヘルパーであるため、この形は `Element` を経由せずに書けます。

**生テキスト要素にエスケープ済みのテキストが届く形**。`AddContent` はエスケープします。`script` / `style` / `xmp` / `plaintext` / `noembed` / `noframes` / `noscript` / `iframe` は内容を生テキストとして要求するため、エスケープが破壊になります。`Element("script")["if (a < b) alert(1);"]` は `<` / `>` / `&` / `'` がunicodeエスケープに置き換わった本体を出し、それらは演算子位置では不正であるためJSの構文エラーになります。`Element("style")` はHTMLの実体参照を出し、CSSはそれを復号しません。`Element("script")[Raw("…")]` は正しく出るため、除外した要素が能力を失わないという §4.1 の主張は成り立ったままです。破れるのは素の綴りのほうで、無言で破れます。したがってここでの検査は「この要素は子を取れない」ではなく「この要素には `Raw` が要る」と言うことになり、BCF3016とは別の文面を持ちます。

**先頭の改行を落とす `textarea` / `pre`**。パーサは開始タグの直後の改行を1つ捨てます。`appendChild` は捨てません。`Pre["\ntext"]` は prerender で `"text"`、interactive で `"\ntext"` を読みます。`&#xA;` へのエスケープでは回避できません。この規則は文字参照の復号より後に適用されるためです。どちらもcuratedヘルパーです。判定に要るのは子の文字列の先頭1文字であり、形の検査ではなく内容の検査です。コンパイル時に知り得るとも限りません。

**廃止済みのパーサ的void 4タグ**。`param` / `keygen` / `basefont` / `bgsound` は標準の13要素とまったく同じ壊れ方をしますが、意図的にBCF3016の外にあります。§4.1 は検査対象をHTML Living Standardのvoid elementsの一覧として定義しており、集合が誰にも再導出できない列挙ではなく標準に追随するのはそのためです。この4つは除外第6群(標準が取り除いた要素)に含まれ、`Element` 経由でしか到達できません。

### D.2 二項側: (親, 子) の関係を要する破れ

判定に (親, 子) の二項関係を要する形です。§4.1 の境界の外側であり、検査しません。content model表をここで著述して維持することになるためです。二項側には性格の異なる2種類が混ざっており、その混在自体が境界をここに置いた理由の一つです。

**誤った綴りが、パーサに動かされる形。**

| 出力 | パーサが読んだ後 |
| --- | --- |
| `<table><div>x</div></table>` | `<div>x</div><table></table>` |
| `<p><div>x</div></p>` | `<p></p><div>x</div><p></p>`(`<p>` が1個から2個になります) |
| `<table>裸のテキスト</table>` | `裸のテキスト<table></table>` |

`Div[Col]` は子を一つも与えていない状態で食い違います。表の外の `col` は再パースで捨てられるためです。`Element("svg")[Element("b")]` は外来コンテンツの部分木から抜け出します。これらは原理的にはすべて診断で捕まえられます。ただし §4.1 が作らないと述べている (親, 子) のcontent model表を要します。

**正しい綴りが、パーサに正規化される形。** `Table[Tr[Td["x"]]]` は表を書く通常の綴りです。パーサは `tbody` を挿入し、interactive描画は挿入しません。

- prerender: `table > tbody > tr` が一致し、`table > tr` は一致しません
- interactive: `table > tr` が一致し、`table > tbody > tr` は一致しません

どちらかに合わせて書いたスタイルシートは、ハイドレーションで意味が変わります。ここには診断で直せる対象がありません。コードはすでに正しいためです。直す価値があるとすれば、直す場所は発行側かドキュメントです。

### D.3 再現手順

1. `RenderView` が発行するのと同じ `RenderTreeBuilder` のフレームを発行します。
2. `Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRenderer` で描画し、`ToHtmlString()` を読みます。
3. その文字列をブラウザで `innerHTML` に代入し、`appendChild` で組んだツリーと比較します。

描画経路どうしの比較には、Interactive Serverのアプリをホストし、DOMを2回読みます。1回目は `blazor.web.js` を遮断した状態、2回目は `RendererInfo.IsInteractive` が真になった後です。

## 付録E: 畳み込まない値(計測済みの境界)

§2.7 (D) が静的畳み込みから除外する値について、除外の根拠を置きます。いずれも Chromium 上で、畳み込み経路(`AddMarkupContent`)と要素経路(`AddContent` / `setAttribute`)の双方を比較して決めました。§2.7 が定めるのは何を畳むかであり、ここが記録するのはなぜその境界なのかです。

### E.1 マークアップを往復できない4文字

下の2つを見れば、仕様の読解だけでは足りないとわかります。うち1つは仕様上どの段も触れない文字です。

- **復帰(CR)**。パーサはCRLFと単独のCRを、トークン化より前の入力ストリーム前処理でLFへ正規化します。これは `<template>` に対するフラグメントパースでも同じです。一方 `setAttribute` / `createTextNode` は正規化しません。属性値は空白の畳み込みを受けないため `getAttribute` に差が直接現れます(畳み込み経路は `"a\rb"` と `"a\r\nb"` の双方に対し `"a\nb"` を返す)。4つのうち実際に踏みやすいのはこれだけで、CRLFで取得したファイル中の逐語的文字列リテラルはこれを含みます。LFは正規化されないため畳み込み可能です。
- **NUL**。乖離の形が位置によって2つに分かれます。マークアップ経路はテキスト内容ではNULを削除し、属性値では U+FFFD へ置換します。要素経路は双方で保ちます。処理が前処理ではなくトークン化と木構築に分かれて置かれているため、テキスト側と属性側で結果が揃いません。
- **孤立サロゲート**。乖離は無く、保守的な除外です。.NET が描画バッチをUTF-8へエンコードする時点で U+FFFD へ置き換わるため、パーサに届く前に両経路とも U+FFFD になります。仕様上も、入力ストリーム中の孤立サロゲートは parse error であってストリームの書き換えではありません。
- **先頭のU+FEFF**。HTMLのパース段はこれに触れません。ブラウザは描画バッチのフレーム文字列をデコードする際、先頭にあるバイト順マークだけを剥がします。畳み込みは値の位置を動かす操作なので、非畳み込みでは値が自身のフレーム文字列の先頭にあって剥がされ、畳み込みでは `<` で始まるより大きな文字列の内部に入って残ります。したがって除外の条件は文字ではなく位置です。この1つだけはマークアップ経路の方が原文に忠実ですが、畳み込みの契約は「両方の綴りが同じDOMを作る」ことであって、どちらが原文に近いかではありません。

### E.2 非文字列の値と、2つの例外

非文字列を除外する根拠は整形時点のカルチャです。実測では、コンポーネントの `OnInitialized` で `CultureInfo.CurrentCulture` を変えても属性の出力は変わらず、`CultureInfo.DefaultThreadCurrentCulture` を変えると変わります(#158)。

整形そのものは `AddAttribute` の呼び出しの中で終わっており、フレームへ入るのは整形済みの文字列です(2026-08-14に#245で実測)。#158の観察はこれと矛盾しません。`OnInitialized` と `RenderView` が同じスレッドで走るとは限らず、`CultureInfo.CurrentCulture` はスレッドごとだからです。子チャネルも同じです。数値の子を許した場合の解決先は `AddContent(int, object?)` の1つで(`int` / `double` / `decimal` / `DateTime` / enum のいずれもここに決まります)、この呼び出しもその場で `ToString` を呼び、`string?` を渡した場合と同じ `Text` フレームへ整形済みの文字列を積みます。畳み込みへは数値がそもそも到達しません。数値を補間した時点で文字列が定数でなくなるためで、`Span[$"n={3}"]` は畳み込まれず `Span["n=3"]` は畳み込まれます。この2つが意味するのは、本項の除外根拠が数値の子と補間文字列を分けないということです。数値の子の綴りを設けない根拠は別にあります(`DESIGN.md` §4.1、#245)。以上は `NonStringValueFormattingTests` と `ChildValueSpellingTests` が固定します。

**定数 `null`** の一致は #171 で実測しました。要素経路のフレーム層・静的SSR・prerender・interactive初回・両方向の再描画のすべてで属性ごと不在に一致し、`""` とは全段で区別されます。対照としてコンポーネントのパラメータ経路は `null` でもフレームを積むため、省略は要素経路だけの性質です。ただし非畳み込み経路が定数 `null` を書くときは `(global::System.String?)` のキャストを伴います。`AddAttribute` の値位置が多重定義されており、裸の `null` は `string?` と `MulticastDelegate?` のどちらにも決まらずCS0121になるためです(#234で実測)。要素経路もフレームを出さない形にすれば markup と完全に一致しますが、シーケンス番号は発行した呼び出しに対して割り当てられるため、発行する定数 `false` の `bool` と扱いが割れます。

**定数 `bool`** の `true` が `name=""` になることはDOM等価として実測しました。prerender 出力は `=""` の無い裸の `name` を書き、これも同じDOMへパースされます。`false` に対して要素経路はフレームを1つも発行しないため、フレーム数も一致します。

**束縛値はこの節の対象外です**(2026-08-14実測、#307)。カルチャを伴う `.Bind` は属性側を `BindConverter.FormatValue(値, culture:)` で包むため、フレームへ入るのは呼び出しサイトに書かれたカルチャの下で整形済みの文字列です。上の実測はいずれも「整形は呼び出したスレッドのカルチャに従う」ことに帰着しますが、この経路では従いません。スレッドが `de-DE` を持ったまま `CultureInfo.InvariantCulture` を書いた束縛が `1234.5` を積むことを実測しました(`NonStringValueFormattingTests`)。包むかどうかは解決されたオーバーロードがカルチャを取るかだけで決まり、束縛値の型は見ないため、`string` と `bool` の既存経路の出力は1バイトも動きません。

この経路の費用は整形時点ではなくトリムに出ます(2026-08-14実測)。値型を1つでも束縛すると `BindConverter` が丸ごと保持されます。`TrimTestApp` を値型束縛の有無で publish した比較では、`BindConverter` の残存メソッドが28から53へ、`Microsoft.AspNetCore.Components.dll` が71,680から81,408バイトへ増えました(osx-arm64、self-contained、`TrimMode=full`)。残る中にはアプリが束縛しない型の変換器も含まれます(`ConvertToGuidCore` など)。`FormatterDelegateCache` と `ParserDelegateCache` が全変換器を一箇所から参照し、そこに `[DynamicallyAccessedMembers(All)]` と `UnconditionalSuppressMessage` が付いているためです。費用はenum固有ではなく、`string` と `bool` だけを束縛するアプリには生じません。`TrimmedOutputTests` が固定します。

### E.3 掃き出した文字クラス

#150 は E.1 以外の文字クラスを掃き、いずれも両経路で一致することを実測しました(C0制御文字、DEL、NEL、NBSP、U+2028/U+2029、BMPおよび追加面の非文字、内部のBOM、U+FFFD自身、タブ、LF、連続空白、正しい対のサロゲート)。入力ストリーム前処理で書き換えが起きるのは改行正規化だけであり、サロゲート・非文字・制御文字は parse error に分類されるだけでストリームを書き換えません。
