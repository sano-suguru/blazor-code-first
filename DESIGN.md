# BlazorCompose: A Code-First Declarative UI Library for Blazor

**Design Overview — 背景・目的・設計方針**

対象プラットフォーム: .NET 10 (LTS) ベースライン / .NET 11 マルチターゲット

> 内部の仕組みの形式的定義は `ARCHITECTURE.md` を参照。

---

## 1. 概要

### 1.1 背景

現在、モバイルおよびデスクトップUI開発の主流は、SwiftUI、Jetpack Compose、Flutterに代表されるコードファーストの宣言的UI(Code-Driven Declarative UI)へとシフトしています。これらはHTML/XMLといった外部マークアップ言語を排除し、プログラミング言語自体の言語機能(型安全性、オートコンプリート、リファクタリング、ロジックのインライン記述)を活かしてUIを構築します。

一方、Microsoft Blazorは優れた宣言的フレームワークであるものの、基本的にはRazor構文(マークアップファースト)に依存しています。C#単体でUIを記述するAPI(`RenderTreeBuilder`)は低レイヤーかつフレームワーク内部向けに設計されており、人間が読み書きするには冗長です。Microsoft自身も `RenderTreeBuilder` の手書きを推奨していません。これは主に、シーケンス番号の手動管理が差分検知(Diffing)の破綻を招きやすいためです。

### 1.2 アプローチ

BlazorCompose は、Blazor 上に型安全なコードファースト UI 構築を導入するライブラリです。

BlazorComposeの中核は、Razorコンパイラと同型のコンパイル戦略です。Razorコンパイラが `.razor` マークアップからC#のレンダリングメソッドを生成するのと同じように、BlazorComposeのSource Generatorは、開発者がC#で記述した宣言的な `Body` 式からレンダリングメソッドを生成します。マークアップの代わりにC#の式を「ソース・オブ・トゥルース」とすることで、Razorが実証済みの静的シーケンス割当・差分検知性能をそのまま継承しながら、SwiftUIやJetpack Composeと同等の記述体験を実現します。

### 1.3 技術的判断

UI定義(`Body`)は設計時のソース・オブ・トゥルースであり、実行時には評価されません。Source Generatorが `Body` の式ツリーを解析し、静的シーケンス番号を定数として埋め込んだレンダリングメソッドを部分クラスに生成します。これはRazorコンパイラが採る方式と同型であり、Blazorの差分検知が要求する「シーケンス番号のコンパイル時確定」を構造的に満たします(§5)。

この方式により、コードファーストUIで従来問題となる2つの型システム上の障害が同時に解消されます。ひとつは、C#には SwiftUI の `some View` に相当する不透明戻り値型が存在しないため、装飾チェーンの合成型(SwiftUIでの `Padded<VStack<...>>` 型のような)を統一的に返す手段がありませんが、実行時評価を行わない本方式では全APIが軽量なマーカー型 `View` を返すだけで済みます。もうひとつは、実行時ツリー構築方式では避けられないヒープ割り当てが、本方式では原理的に発生しません(生成コードは `RenderTreeBuilder` へ直接命令を発行します)。

解析の適用範囲は明示的に仕様化します。静的解析可能な構文サブセット(SSC)の内側では完全な静的シーケンス割当を行い、外側(解析不能なヘルパー呼び出し等)は動的リージョンとして正確性を保ったまま縮退させます(§5.3)。

プラットフォーム戦略はLTS優先です。net10.0をベースラインとし、net11.0はオプトインのマルチターゲットとします(§3)。性能に関する数値はすべて予測値として明記し、第1フェーズのPoCベンチマークで実測・更新します(§7)。

### 1.4 Razorとの関係と本ライブラリの立ち位置

なぜ既存の Razor ではなく本ライブラリを設けるのか。前提として、DOM を出力先とする宣言的 UI では、既製の HTML タグ集合(`div` / `span` / `ul` 等)へ構文を寄せる Razor が、構文的に最も素直な既定解である。本ライブラリはこの事実を認めた上で、あえて純 C# のコードファーストという別の設計点を狙う。

その差が生じる根拠は言語機能にある。SwiftUI や Jetpack Compose が `if` / `for` を UI の中へそのまま書けるのは、Swift/Kotlin が result builder(`@ViewBuilder`)や trailing lambda を言語機能として備えるためである。C# にはこれがない。正確には result builder「まるごと」ではなく、**文(`if` / `foreach`)をそのまま子生成の式へ変換する仕組み**が欠けている(フラットな子並びは `params` やコレクション初期化子で表現できる)。本ライブラリで条件分岐やリストが当面 `If()` / `ForEach()` ファクトリ経由になる(§4.2)のは、この欠落を Source Generator という言語外の手段で補うための表面であり、素の `if` / `foreach` を扱う経路は段階的に実装する(§5.3、§9)。

構文的な素直さで一歩譲るこの設計を選ぶ理由は、見た目ではなく次の実在する価値にある。

- **単一言語:** UI とロジックが同じ C# で閉じ、マークアップとコードの文脈切替が消える。
- **型安全とリファクタリング:** UI が素の C# 式であるため、IDE のリネームや抽出リファクタリングがそのまま適用でき、型の不整合はビルド時に検出される。マークアップとコードの境界をまたぐ Razor で生じがちなツール精度の劣化がない。
- **プログラム的合成:** UI を通常の関数・ジェネリクス・コレクション操作で組み立てられる。

本ライブラリは、これらの価値を、Razor が実証済みの静的シーケンス割当・差分検知性能を捨てずに得ることを目標とする(§5)。したがって本ライブラリの位置づけは「Razor の置き換え」でも「Razor より優れる」でもなく、単一言語・型安全・プログラム的合成を優先する利用者に向けた、性能特性を共有する別の選択肢である。

---

## 2. コアコンセプト

### 2.1 HTMLを排した純粋なC#

HTMLのタグ記述(マークアップファイルでのタグ列挙)を廃止し、すべてをC#のメソッド、型安全な列挙型、構造体で表現します。CSSクラスの指定自体は `.Class(string)` が受け付ける文字列がそのまま `class` 属性へ流れる、意図的な生文字列エスケープハッチです(§4.1)。IDEのインテリセンスが機能し、レイアウトやスタイルのエラーはビルド時に検知されます。

### 2.2 既存Blazorとのシームレスな統合

既存のBlazorエコシステムと相互運用できます。BlazorComposeで構築したUIは標準の `RenderFragment` として公開できるため、既存の `.razor` コンポーネントの中から呼び出すことができ、逆もまた可能です(§6)。

### 2.3 Razorと同等のコンパイル

`Body` に記述された宣言的な式は、Source Generatorによってビルド時にレンダリングメソッドへコンパイルされます。生成コードはRazorコンパイラの出力と同じ形式(静的シーケンス番号付きの `RenderTreeBuilder` 命令列)であるため、Blazorエンジンから見ればBlazorComposeコンポーネントと通常のRazorコンポーネントは区別がつきません。開発者が書くコードと実行される命令列の間に、ランタイムの動的解釈や中間ツリーは存在しません。

> 設計上の要件: コンポーネントクラスは `partial` として宣言する必要があります(Source Generatorがレンダリングメソッドを同一クラスへ生成するため)。非partialクラスはビルドエラー(BC1001)となります。

---

## 3. 対応プラットフォーム戦略

| ターゲット | 位置付け           | 提供機能                                                                                                 |
| ---------- | ------------------ | -------------------------------------------------------------------------------------------------------- |
| net10.0    | ベースライン(必須) | コアエンジン全機能。LTS(3年サポート)であり企業ユーザーの採用障壁が低い                                   |
| net11.0    | オプトイン(推奨)   | C# 15のUnion型・`closed` 階層による閉世界 `ViewNode` 定義、Runtime Asyncによるイベントパイプライン軽量化 |

本ライブラリのコア技術(Source Generatorによる部分クラスへのメンバー生成)は成熟した標準機能であり、特定の最新言語機能に依存しません。net11.0(2026年11月GA予定、STS・24ヶ月サポート)では、C# 15のUnion型と `closed` 階層を用いてUIノードの集合を閉じた判別共用体として定義でき、ビジターの網羅性がコンパイル時に検証されます。該当APIは `#if NET11_0_OR_GREATER` で条件提供します。

> 注記: Union型は執筆時点(.NET 11 Preview 5)で一部機能が未実装のため、net11.0向けAPIの正式化はGA後とします(§9 ロードマップ参照)。

---

## 4. APIデザインと構文仕様

### 4.1 基本コンポーネントの構造

開発者は `ComposeComponentBase` を継承した `partial` クラスで、`Body` プロパティをオーバーライドしてUI構造を定義します。語彙はHTML要素をそのまま写す「HTMLミラー表層」を採ります。この表層への転回の背景・比較検討・到達像は方向設計文書 `docs/superpowers/specs/2026-07-25-html-mirror-surface-direction-design.md` に詳細を記載しており、本節はその改訂結果です。

```csharp
using BlazorCompose;
using static BlazorCompose.Html;

public partial class CounterPage : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        Div(
            Span($"Count: {_count}"),
            Button("Increment").OnClick(() => _count++),
            Button("Reset").OnClick(() => _count = 0)
        )
        .Class("bc-counter");
}
```

- ファクトリは静的クラス `Html` に集約します。推奨形は `using static BlazorCompose.Html;` を導入した上で `Div(...)` のように**非修飾で呼び出す**ことです。`Component` や `Element` のようにBlazor周辺で頻出する型名・識別子とインポートしたファクトリ名が衝突する場合に限り、衝突する呼び出しだけを `Html.Component<T>()` のように修飾するエスケープハッチとして残します。RM2で `Nav` / `Header` / `Article` / `Section` 等の短いタグ名ヘルパーが加わったことで、ドメイン型やジェネリック引数との衝突可能性は今後さらに広がりえます。
- `Html.Div` / `Span` / `Button` は常用タグの名前付きヘルパーで、いずれも任意タグ用の `Html.Element(string tag, ...)` の名前付き別名として実装され、同一の統合ノードに落ちます(`tag` はコンパイル時定数が必須で、非定数はBC3009で診断されます)。RM2でcuratedヘルパーは `Div` / `Span` / `Button` / `Nav` / `Header` / `Main` / `Aside` / `Footer` / `Section` / `Article` / `P` / `H1`–`H6` / `Ul` / `Ol` / `Li` / `A` / `Img` まで拡充されました。この集合にない任意タグは引き続き `Element(tag, ...)` で表現します。
- 要素は文字列と `View` を**混在**して子に取ります(`params ReadOnlySpan<View>`)。生の文字列引数は暗黙変換(`implicit operator View(string)`)によりテキストノードになるため、専用の `Text()` ファクトリは持ちません。テキストのみを明示的に囲みたい場合は `Span("...")` を使います。
- 属性・イベントは要素本体への引数ではなく、**装飾チェーン**(postfix fluent)で与えます。名前付き属性ショートカット(`.Href` / `.Src` / `.Alt` / `.Id` / `.Type` / `.Title` / `.Role`、値はいずれも `string`)が主要な書き方で、これ以外の属性は汎用 `.Attr(name, value)` で与えます。イベントは `.OnClick(Action)` / `.OnClick(Func<Task>)` に加え、汎用 `.On(fullEventName, Action)` / `.On(fullEventName, Func<Task>)` を持ちます。`.On` は `"onclick"` / `"onmouseenter"` のように**`on` プレフィックスを含むフルの属性名**を受け取り、暗黙のプレフィックス付与は行いません。`.Attr` の属性名と `.On` のイベント名はいずれもコンパイル時定数が必須で、非定数はBC3011で診断されます。`class` は唯一の畳み込み属性で、`.Class(string)`(または `.Attr("class", …)`)をチェーンで複数回指定すると単一の `class` 属性へ畳み込まれます。それ以外の属性・イベントはすべて単一バインディングで、同一属性/イベントの重複指定はBC3010で診断されます。`style` にショートカットはなく、外部CSSと `.Class` の併用を推奨します(明示的に `.Attr("style", …)` を書くことは可能です)。HTML自体の妥当性 — void要素(`Img` 等)が子を持てないこと、特定の属性が特定の要素にのみ許可されることなど、いわゆるcontent modelの検査は行いません。これはkotlinx.html流ではなくhiccup/ScalaTags流の型安全観(§4.1後段)どおりで、コンパイル時に検出されるのはC#レベルの名前・型の誤りに限られます。RM3で `Html.Fragment` と `Html.Raw` を追加しました(下記)。将来にはフォーム関連ヘルパー、型付きイベント引数、`bool`/`object` 値属性、辞書から一括指定する `.Attrs(...)` 等の追加を検討していますが、現時点では未実装です。
- Blazorの `RenderFragment?` はそのままコンテンツになります(`View` への暗黙変換)。専用のファクトリは不要で、`Div(fragment)` のように文字列や他の `View` と同じ位置に書けます。用途は主に2つです — `[Parameter] public RenderFragment? ChildContent` を持つコンポーネントがRazor側から渡された子孫を描画する場合と、レイアウト(後述)が `LayoutComponentBase.Body` を配置する場合です。変換は非ジェネリックの `RenderFragment` に限られ(`RenderFragment<T>` は変換されずCS1503)、`Fragment`/`Raw` と同様に単一の要素フレームを開かないため非キー可能で、`ForEach` の `content` の根には使えず(BC3003)、装飾もできません(BC3008)。
- `If` / `ForEach`(§4.2)・`Component<T>()`(§6.2)と同様、`Html.Fragment(params ReadOnlySpan<View> children)` と `Html.Raw(string rawHtml)` はいずれもHTML要素にマップしない構文です。`Fragment` はラッパー要素を持たないグルーピング(React `<>…</>` 相当)で、子は0個以上の文字列/`View` の混在を受け取ります。単一の要素フレームを開かないため非キー可能で、`ForEach` の `content` の根には使えず(BC3003)、同じ理由で装飾もできません(BC3008)。`Raw` は信頼済みHTML文字列を `RenderTreeBuilder.AddMarkupContent` へ直接注入する構文で(`MarkupString` と同じ信頼境界)、**信頼済みコンテンツ専用**です — ユーザー入力や外部レスポンスなど非信頼な文字列を通すとXSSベクタになります。値は文字列リテラルでもフィールド/const参照でも構いません(配信経路に依存しない値スロットのため、`Html.Element` のタグ引数(BC3009)や `.Attr`/`.On` の名前引数(BC3011)のようなコンパイル時定数の制約はありません)。`Raw` も要素を開かないため非キー可能で、`ForEach` の `content` 根には使えず(BC3003)、装飾もできません(BC3008)。
- 装飾チェーンがpostfix fluentである点は、同系譜(kotlinx.html / ScalaTags / Elm html / hiccup / F# Feliz)のattrs-first形式(`div [attrs] [children]`)とは異なります。これは系譜への準拠を意図したものではなく、既存の `.Class` 機構の継続とC#のfluentイディオムを優先した意図的な選択です。
- 型安全の位置付けは、要素別の型・content model・属性適用可否をコンパイル時検査するkotlinx.html流ではなく、統一ノード+文字列タグを採るhiccup / ScalaTags流です。したがって本方式が言う「型安全」はC#レベル(`Body` 全体が型付きC#式であり、合成・リファクタリングが型を通じて伝わる)を指し、HTML妥当性レベル(void要素が子を持てない、属性が当該要素に適用可能か等)の検査は含みません。
- `View` はすべてのファクトリ・装飾メソッドが返す軽量なマーカー型(空の `readonly struct`)です。式は通常のC#として型検査されますが、実行時に評価されることはなく、Source Generatorが式ツリーを直接レンダリングコードへ変換します。
- 状態(`_count`)への参照や補間文字列、イベントラムダは、生成コードへ構文ごと移植されます(同一partialクラス内のため、privateメンバーへのアクセスも保たれます)。
- **casingの限界**: C#のメソッド名はPascalCase、HTMLタグ名は小文字であるため、`Div`(修飾形では `Html.Div`)は `<div>` と文字面では一致しません。「ミラー」はcasingの点で構造的に破れており、これはC#の言語制約による既知の割り切りです。

かつての設計案では、SwiftUI/Jetpack Compose流のレイアウトコンテナ(`VStack` / `HStack` / `Grid`)と型付き装飾(`.Padding()` / `.FontSize()` / `.Bold()` 等)を本節の想定APIとしていましたが、これらは採用を見送り、新たな根拠を伴う本文書またはARCHITECTURE.mdの明示的な改訂なしには復活させません。理由は、出力先が実HTML/CSSであるBlazorComposeにおいて、独自のレイアウト語彙は「既に完成した下層(HTML/CSS)の上へ、覚え直しの語彙と暗黙挙動を重ねるだけ」になるためです(根拠の詳細は前掲の方向設計文書)。横並びが必要な場合は `Div(...).Class("row")` と外部CSS(`.row { display: flex }`)で表現し、暗黙のflex注入は行いません。汎用 `.Attr(name, value)` を使えば `.Attr("style", "display:flex")` の明示指定も選択肢になりますが、推奨は外部CSSと `.Class` の組み合わせであり、`style` に専用のショートカットは設けません。`Text()` ファクトリの廃止も同じ理由によるもので、mixed contentがその役割を引き受けます。この置き換えは§8(実DOMゆえのSEO/a11y/CSSエコシステムという差別化)および§2.1(HTMLを排した純粋なC#という立場)と矛盾しません。HTML要素の語彙をC#メソッドとして写すだけであり、外部マークアップファイルや生文字列テンプレートを導入するものではないためです。

### 4.2 リストと条件分岐の表現

分岐とループは、専用コンビネータ `If` / `ForEach` で宣言的に記述します。

```csharp
public partial class TaskListPage : ComposeComponentBase
{
    private readonly List<TaskItem> _items = [];

    protected override View Body =>
        Div(
            Span("Tasks"),

            If(_items.Count == 0,
                then: () => Span("No tasks yet").Class("empty"),
                otherwise: () => ForEach(_items,
                    key: t => t.Id,
                    content: item =>
                        Div(
                            Span(item.Title)
                        )
                        .Class(item.Done ? "task done" : "task")
                )
            ),

            Button("Add Task").OnClick(AddItem)
        );

    private void AddItem() => _items.Add(new TaskItem("New task"));
}
```

- `If` はネイティブの `if` 文へ、`ForEach` は `foreach` + `SetKey` へと展開されます。分岐の各パスには互いに素な静的シーケンス空間が割り当てられ、状態の誤った引き継ぎを防ぎます(`ARCHITECTURE.md` §2.4)。
- `ForEach` の `key` セレクタは必須です。シーケンス番号が「テンプレート内の構文位置」を、キーが「データの同一性」をそれぞれ担うことで、並べ替え・挿入・削除時の状態保持が保証されます。
- `Body` 内でネイティブの制御構文(ブロック本体の `if` / `foreach` 等)を直接使うことも可能です。Source Generatorは該当構文を生成コードへそのまま移植し、動的リージョンで包みます(§5.3)。

### 4.3 コンポーネントの分割と再利用

UIの部分は `[Composable]` 属性を付与した静的メソッドに抽出できます。Jetpack Composeの `@Composable` に対応する概念で、Source Generatorはこれらを解析対象に含め、呼び出しサイトへ静的に展開します。

```csharp
protected override View Body =>
    Div(
        Header("My Application"),   // [Composable] メソッド — 静的展開の対象
        BodyContent()
    );

[Composable]
private static View Header(string title) =>
    Div(
        Span(title)
    )
    .Class("app-header");
```

`[Composable]` の付かないメソッドが `View` を返す場合、Source Generatorはその内部を解析できないため、当該メソッドは実行時に評価される動的コンテンツとして扱われます(戻り値の `View` に `RenderFragment` を内包させる形式。§5.3)。

本方式の要となる3つの変換 — 装飾チェーンの畳み込み、`ForEach` のキー整合、`[Composable]` の静的インライン展開 — について、それぞれ「どの入力を、どの生成コードに変えるか」を `ARCHITECTURE.md` §2.7 に入出力例として定義しています。

---

## 5. アーキテクチャと内部実装

### 5.1 コンパイルモデル: Bodyからレンダリングメソッドへ

Source Generatorは各コンポーネントの `Body`(および到達可能な `[Composable]` メソッド)の式ツリーを解析し、静的シーケンス番号を定数として埋め込んだレンダリングメソッドを同一partialクラスへ生成します。

§4.1の `CounterPage` から生成されるコードの概念形:

```csharp
// <auto-generated/> CounterPage.g.cs
public partial class CounterPage
{
    protected override void RenderView(RenderTreeBuilder __b)
    {
        __b.OpenElement(0, "div");                                    // Div + .Class
        __b.AddAttribute(1, "class", "bc-counter");
        __b.OpenElement(2, "span");                                   // Span (mixed content)
        __b.AddContent(3, $"Count: {_count}");                        // 状態参照は構文ごと移植
        __b.CloseElement();
        __b.OpenElement(4, "button");                                 // Button + .OnClick
        __b.AddAttribute(5, "onclick",
            EventCallback.Factory.Create(this, () => _count++));      // ラムダも移植
        __b.AddContent(6, "Increment");
        __b.CloseElement();
        __b.OpenElement(7, "button");
        __b.AddAttribute(8, "onclick",
            EventCallback.Factory.Create(this, () => _count = 0));
        __b.AddContent(9, "Reset");
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
    protected abstract void RenderView(RenderTreeBuilder builder);   // SGが実装を生成

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder)
        => RenderView(builder);
}
```

`Body` は実行時に一度も呼び出されません。ファクトリ・装飾メソッドの実体はすべて `default(View)` を返す慣性(inert)実装であり、万一評価されても副作用はなく、AOTビルドではILトリマーにより除去されます。除去は `System.Reflection.Metadata` によるMethodDef不在をもって確認できる設計であり、その確認手段はトリムテストが担います。

### 5.2 シーケンス番号の静的確定

Blazorの差分検知は、シーケンス番号がコンパイル時に静的確定していることを前提とします。ランタイムでの動的インクリメントは、要素の挿入・削除時にDiffingアルゴリズムを誤認させ、サブツリーの不要な破棄・再生成とコンポーネント状態の消失を引き起こします。

本方式では、Source Generatorが式ツリーを深さ優先で走査し、各ノードに一意のシーケンス区間を割り当てて生成コードへ定数として埋め込むため、この前提は構造的に満たされます。Razorコンパイラがマークアップに対して行っていることを、C#式に対して行うだけです。割当アルゴリズムの形式定義は `ARCHITECTURE.md` §2を参照してください。

### 5.3 静的解析可能サブセット(SSC)と動的リージョン

任意のC#コードに対して静的シーケンス割当は成立しないため、解析の適用範囲を明示的に定義します。

`Body` および `[Composable]` メソッド内のファクトリ/装飾/コンビネータの直接呼び出し(インラインラムダを含む)がSSCの内側であり、完全な静的割当の対象です。SSCの外側は次の2通りに扱われます。

移植可能な構文(ネイティブ `if` / `foreach` / `switch` 等)は、生成コードへそのまま移植された上で、境界に静的シーケンスを持つリージョン(`OpenRegion` / `CloseRegion`)で包まれます。リージョンはシーケンス空間を分離するため、内部の動的性が外部のDiffingへ波及することはありません。

解析不能な呼び出し(`[Composable]` の付かない `View` 返却メソッド等)は、実行時に評価され、戻り値の `View` に内包された `RenderFragment` がリージョン内で描画されます。この経路のみ通常のヒープ割り当てが発生します。

いずれの場合も正確性は保たれ、失われるのは該当領域の静的最適化のみです。アナライザーは情報診断BC2001で最適化機会の喪失を通知します。

アナライザーは現行実装では `Body` 本体での状態変更(インスタンスフィールド/プロパティへの代入、複合代入、インクリメント/デクリメント等の直接書き込み)をエラー診断BC3001で検出します。`Body` は純粋な状態→UIの射影でなければならず、状態遷移はイベントハンドラに委ねる必要があります。なお、`Button` のonClickラムダ(遅延イベントハンドラ)はレンダリング後に実行されるため除外されます。メソッド呼び出し経由の副作用など任意の解析不能パスの完全な検出は保証しません。`[Composable]` 本体への適用は将来拡張の候補であり、この初期実装の保証範囲には含めません。

### 5.4 Hot Reload戦略

`Body` 式の編集はSource Generatorが再生成する `RenderView` のメソッド本体の変更として現れますが、メソッド本体の差し替えは.NET Hot Reload(Edit and Continue)が安定してサポートする編集クラスです。`[Composable]` メソッドの追加も「既存型へのメンバー追加」であり、サポート範囲内です。さらにBlazorには既にRazor用の `MetadataUpdateHandler` によるコード更新後の再レンダリング経路が存在し、BlazorComposeのコンポーネントは通常の `ComponentBase` 派生+通常の生成メソッドであるため、この既存経路にそのまま乗ります。独自のリロード機構は必要ありません。

挙動は次のように仕様化します。要素を `Body` の途中へ挿入する編集では後続ノードのシーケンス番号が再割当されるため、リロード直後の初回レンダリングで当該コンポーネントのDOMサブツリーが再構築されます(コンポーネントのフィールド状態は保持され、入力中のフォーカス等のDOMローカル状態は失われえます)。これはRazorファイル編集時と同じ意味論です。

本設計が依存する唯一のBlazor標準外の前提は、編集セッション中にサードパーティSource Generatorが再実行され、生成コードの更新がEnCへ渡ることです。ここはVisual Studio / `dotnet watch` / Riderで挙動差が生じうるツーリング領域であり、環境ごとの確認を要します。特定環境で再実行が反映されない場合に備えた開発時フォールバック(DEBUGビルド限定の解釈モード)は `ARCHITECTURE.md` 付録Cに代替案として記載しています。

---

## 6. 既存Blazorエコシステムとの双方向互換性

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
        Span(status.Label)
            .Class(status.IsHealthy ? "badge badge-ok" : "badge badge-alert");
}
```

### 6.2 BlazorComposeの中で既存のRazorコンポーネントを使う

`Component<T>()` ファクトリで、サードパーティ製を含む任意のBlazorコンポーネントをコードファーストツリーへ組み込めます。パラメータはSource Generatorが生成する静的セッターでバインドされるため(式木のランタイムコンパイルなし)、AOT環境でも安全です。

```csharp
protected override View Body =>
    Div(
        Span("Data Grid"),
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)
    );
```

---

## 7. パフォーマンス特性と予測

本章の数値はすべて設計に基づく予測値であり、第1フェーズのPoCベンチマークで実測・更新されます。

### 7.1 レンダリングコストとGCアロケーション

生成コードはRazorコンパイラの出力と同形式であるため、SSC内側のレンダリングコスト・アロケーション特性は等価なRazorコンポーネントと同等(予測値)です。実行時の中間ツリーやビルダーオブジェクトは存在しないため、コードファースト方式に一般的に伴う追加のGC負荷はゼロです。追加コストが生じるのは動的コンテンツ経路(§5.3)のみで、これは `RenderFragment` を手書きした場合と同等です。

### 7.2 差分検知性能

静的シーケンス割当により、Diffing計算量は理論上の最小値 O(|r_t| + |r_{t+1}|) を維持します(`ARCHITECTURE.md` §1.2)。これは、動的インクリメント方式が要素挿入・削除・並べ替え時にサブツリー全体の破棄・再生成と状態消失を招くのに対し、本方式が構文位置に固定されたシーケンス番号によりこれを回避するためです(比較の形式的根拠は `ARCHITECTURE.md` §1.2)。

### 7.3 Wasmバイナリサイズ

パラメータバインディングを含む全機構がリフレクション・フリー(`System.Reflection` / `System.Linq.Expressions` へのランタイム依存ゼロ)であるため、ILトリマーが未使用コードを削除できます。`TrimMode=full`・`ILLinkTreatWarningsAsErrors=true` の下で、`Body` ゲッター・未到達ファクトリメソッドのMethodDefがメタデータレベルで除去される設計です。リフレクションベースのバインディングを持つ同等ライブラリ比で、AOTコンパイル後のWasmペイロードを約20〜30%削減(予測値)と見込みます。素のRazor構成との比較ではほぼ同等です。

---

## 8. 関連プロジェクトとの比較

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

この対比から、本ライブラリの特徴は次の3点に整理できます。

第一に、DOMネイティブであること。Avaloniaはブラウザ上でも動作しますが、それはSkiaによるcanvasへの描画であり、DOMを持ちません。SEO、アクセシビリティツリー、CSSエコシステム、SSR/プリレンダリングはcanvas描画には適用できません。BlazorComposeは実DOM/HTMLへ宣言的UIを射影するため、これらのWeb標準の資産をそのまま利用できます。

第二に、Blazor差分検知との構造的整合。retained-mode系(Avalonia.Markup.Declarative、MAUI.Markup)はコントロール実体を保持・変異させるため、シーケンス番号問題自体を持ちません。一方、Blazor上でコードファーストを試みる場合この問題は不可避であり、本ライブラリはこれをRazorコンパイラと同型の方式で解決します。手書き `RenderTreeBuilder` や実行時ツリー方式では、この問題が正しさと性能の両面で破綻要因となります。

第三に、宣言的セマンティクスとゼロ中間表現の両立。毎レンダリングでUI全体を再評価する宣言的な書き味は、実行時ツリー構築方式(Cometがこの型)ではGCプレッシャーという恒常的コストを伴います。コンパイル時生成方式では、同じ書き味を実行時の中間オブジェクトなしに得られます。

---

## 9. ロードマップ

### 第1フェーズ: コアAPIとPoC

コアAPIはHTMLミラー表層として実装します。常用タグの名前付きヘルパー(`Html.Div` / `Span` / `Button`)と任意タグ用の `Html.Element` を統一 `Element` ノードへ落とし、属性・イベントは装飾チェーン(`.Class` / `.OnClick`)で与えます(§4.1、方向設計文書 `docs/superpowers/specs/2026-07-25-html-mirror-surface-direction-design.md`)。かつて本節が第1フェーズの語彙としていたSwiftUI/Compose風の `VStack` / `HStack` / `Grid` と型付き装飾(`.Padding()` / `.FontSize()` 等)は本方針により置き換えられ、新たな設計文書の改訂なしに復活しません。実装はマイルストーンRM1–RM3へ分割します。RM1は統合 `Element` ノード・mixed content・装飾チェーンの一般化を対象とし、M1で先行実装した `.Class` を土台に `.OnClick` を確立します。RM2ではcuratedタグ集合を拡充し(`Nav` / `Header` / `Main` / `Aside` / `Footer` / `Section` / `Article` / `P` / `H1`–`H6` / `Ul` / `Ol` / `Li` / `A` / `Img`)、名前付き属性ショートカット(`.Href` / `.Src` / `.Alt` / `.Id` / `.Type` / `.Title` / `.Role`)と汎用 `.Attr` / `.On` を確立しました。`class` は引き続き唯一の畳み込み属性で、それ以外の属性・イベントは単一バインディングです(重複はBC3010、非定数の名前はBC3011で診断)。RM3では `Html.Fragment`(ラッパーレスなグルーピング)と `Html.Raw`(信頼済み生HTML注入)を実装しました。フォーム関連ヘルパーや型付きイベント引数などの追加は次段階で検討します。RM3ではさらに、`ComposeLayoutBase` によりレイアウトもコード化しました。Blazorのレイアウトが要求する `Body` パラメータ(`LayoutComponentBase.Body`、型は `RenderFragment?`)は暗黙変換で `View` になるため、専用のファクトリなしに `Main(Body)` のようにそのまま要素の子として書けます。レイアウト自身が描く design-time 式は `Body` と名乗れない(Blazorが `Body` という名前を要求するため)ので `Chrome` と命名し、`Chrome` もコンポーネントの `Body` 同様に読み取り専用(state mutationはBC3001)です。Source Generatorによる `Body` 解析→レンダリングメソッド生成パイプラインの実証は各マイルストーンを通じて継続します。検証ベンチマークとして、動的インクリメント方式とのDiffing挙動・状態保持比較、および素のRazorとのアロケーション比較を実測し、§7の予測値を実測値に置換します。受け入れ条件には、Visual Studio / `dotnet watch` / Riderの3環境におけるHot Reload動作の実測(§5.4)を含めます。

### 第2フェーズ: 解析範囲の拡張と .NET 11 対応

`[Composable]` 解析の拡張(ジェネリックヘルパー、ローカル関数対応)と動的リージョンの安定化を行います。.NET 11 GA(2026年11月)後、Union型・`closed` 階層ベースの `ViewNode` APIをnet11.0ターゲットで正式化します。

いずれのフェーズも本ライブラリの本筋(コンパイル時の静的シーケンス割当)に直結する範囲に限定します。デザインツール連携やCSSフレームワーク向けアダプター等の周辺構想は、本筋の設計とは独立した別プロダクトの検討事項であり、本設計図の対象外とします。