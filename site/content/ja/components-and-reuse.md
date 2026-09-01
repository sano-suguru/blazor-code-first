---
title: コンポーネントと再利用
description: コンポーネントから別のコンポーネントを呼ぶ方法、Razor コンポーネントとの双方向の相互運用、ViewPart の用途。
order: 60
group: write
source-hash: 417b557e
---

再利用の単位はコンポーネントです。BlazorCodeFirst のコンポーネントから別のコンポーネントを呼ぶ
には `Component<T>()` を使います。既存の Razor コンポーネントも、サードパーティのコンポーネント
も、まったく同じ呼び方です。逆に `.razor` のファイルからは、BlazorCodeFirst のコンポーネントを
通常のタグとして書けます。`[ViewPart]` は用途の違う別の仕組みで、このページの終わりで扱います。

## BlazorCodeFirst のコンポーネントを呼ぶ

`Component<T>()` は、コンポーネントをツリーに置きます。パラメーターは `.Param` でバインドし、
対象のプロパティはラムダで指定します。

```csharp
protected override View Body =>
    Div.Class("dashboard")[
        Component<StatusBadge>()
            .Param(b => b.Status, _status)
            .Param(b => b.Compact, true)];
```

ジェネレーターは `.Param` のひとつひとつを静的なパラメーターの設定に変え、
`AddComponentParameter` の呼び出しとして書き出します。リフレクションは使わず、実行時に式ツリーを
コンパイルすることもありません。この経路がトリミングと AOT に対して安全なのは、そのためです。

書ける形を診断で縛っているのも、それが理由です。セレクターのラムダでパラメーターを指定する
チャネルは、どれもこの診断で検査します。`.Param` だけでなく、`.Template` も、コンポーネントに対する
`.Bind` も同じです。

- セレクターは、プロパティを選ぶだけの式である必要があります（[BCF3005](./diagnostics.md#bcf3005)）。
- 対象は、設定できる `[Parameter]` のプロパティである必要があります（[BCF3006](./diagnostics.md#bcf3006)）。
- 1つのチェーンで、各プロパティをバインドできるのは1回までです。チャネルはすべて数えます
  （[BCF3007](./diagnostics.md#bcf3007)）。

## 子の内容を渡す

入れ子に書いた子は `ChildContent` にバインドされます。入れ子の内容は `ChildContent` にしか
ならない、という Razor の規則をそのまま写しています。

```csharp
protected override View Body =>
    Component<Card>()[
        H2["Heading"],
        P["Body text"]];
```

これには `Card` の側に、フラグメント型で設定できる `[Parameter] ChildContent` が必要です。無ければ
[BCF3013](./diagnostics.md#bcf3013) を報告します。`RenderFragment<TContext>` も対象です。角括弧は
コンテキストを捨ててバインドします。角括弧の中には、コンテキストを読むための名前が無いからです。
`ChildContent` 以外の名前を持つジェネリックなフラグメントは `.Template` で指定します。下の
[ジェネリックなフラグメントのパラメーター](#ジェネリックなフラグメントのパラメーター)を見て
ください。

ジェネリックでないほかの `RenderFragment` パラメーターは、`Footer` や `Header` などです。これらは
`.Param(c => c.Footer, content)` と書き、パラメーターを明示してバインドします。

```csharp
protected override View Body =>
    Component<Card>()
        .Param(c => c.Title, "Card title")
        .Param(c => c.Footer, Span["Footer note"])[
            H2["Heading"],
            P["Body text"]];
```

`ChildContent` を `.Param` で指定するのも正しい書き方です。冗長ですが、Razor の属性の形
（`<Card><ChildContent>...</ChildContent></Card>`）に対応します。同じパラメーターを両方の
チャネルからバインドすると [BCF3007](./diagnostics.md#bcf3007) です。

本物の `RenderFragment` の値は、BlazorCodeFirst の `View` の式とは違います。ジェネリックな
`.Param<TValue>` のオーバーロードでバインドし、そのまま書き出されます。

どちらのオーバーロードが使われるかは、対象のパラメーターの型で決まります。`RenderFragment?` の
パラメーターなら内容のオーバーロード、それ以外ならジェネリックなほうです。そのため
`RenderFragment` でないパラメーターに向けた内容は、ジェネリックなオーバーロードに落ち、値が
そのまま書き出されます。ただし設計時の式は、実行時には空の目印でしかありません。これが
[BCF3014](./diagnostics.md#bcf3014) です。

```csharp
[Parameter] public object? Payload { get; set; }

Component<Card>().Param(c => c.Payload, Div["x"])   // BCF3014
```

`View`、`ElementView`、`ComponentView<T>`、`SlotView` は、どれも同じように報告します。内容を
渡したいなら、受け取る側のコンポーネントに `RenderFragment` のパラメーターを持たせてください。

パラメーターの値の中で型名が解決できない場合は、[BCF3015](./diagnostics.md#bcf3015)を見てください。

## ジェネリックなフラグメントのパラメーター

`RenderFragment<TContext>` のパラメーターが取るのは *テンプレート* です。コンポーネントは、描き
たいコンテキストの値ごとに、それを1回ずつ呼び出します。よくある例が `EditForm.ChildContent` で、
これは `RenderFragment<EditContext>` です。

こうしたパラメーターを指定するのが `.Template` です。書き方は2通りあり、どちらを使うかは、
内容がコンテキストを読むかどうかだけで決まります。グリッドの `RowTemplate` のように
`ChildContent` 以外の名前を持つものには、常に `.Template` が必要です。角括弧では指定できません。

コンテキストを読まない `ChildContent` は角括弧で書きます。上に出した形であり、
`.Template(form => form.ChildContent, content)` と同じものを発行します。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

使うなら、コンテキストから内容へのラムダで指定します。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)
        .Template(form => form.ChildContent, editContext =>
            Fragment(
                Span[editContext.IsModified() ? "Unsaved changes" : "No changes"],
                Component<NameFields>().Param(fields => fields.Value, _model)));
```

2つ目の例には、注意点が1つあります。これを踏まえないと、バッジは一度も変わりません。
`IsModified()` はテンプレートの実行時に読まれます。しかし `EditForm` と `CascadingValue` の
連なりには、`OnFieldChanged` で再レンダリングするものがありません。フィールドに入力すれば
`EditContext` に通知は届きます。ただ、その通知を誰も購読していないので、テンプレートを持つ
コンポーネントは再レンダリングされず、バッジは最初に描かれた文字のままです。これは Blazor の
レンダリングの伝わり方に起因するもので、テンプレートが受け取るコンテキストの制限ではありません。
テンプレートには、実行のたびに最新の `EditContext` が渡されます。

テンプレートが変化するコンテキストの状態を読むなら、再レンダリングはフォームを持つコンポーネント
自身が行います。`Model` に作らせるのではなく、`EditContext` を自分で組み立てます。そして
`OnFieldChanged` を購読し、`StateHasChanged` を呼び、`Dispose` で購読を解きます。

```csharp
public ContextReadingForm()
{
    _editContext = new EditContext(_model);
    _editContext.OnFieldChanged += OnFieldChanged;
}

private void OnFieldChanged(object? sender, FieldChangedEventArgs e) => StateHasChanged();

public void Dispose() => _editContext.OnFieldChanged -= OnFieldChanged;

// ...そのうえで .Param(form => form.Model, …) ではなく .Param(form => form.EditContext, _editContext) を渡す
```

コンテキストを使わないテンプレートや、フォームが開いているあいだ変わらない状態しか読まない
テンプレートに、この準備は不要です。

`RenderFragment<TContext>` のラムダはジェネレーターが書くので、その内容は通常の
BlazorCodeFirst で、シーケンス番号も周りから続きます。コンテキストの引数の名前は自由に決められ
ます。生成コードは自前の名前を使い、参照していた箇所を書き換えるので、たまたま同じ名前の
フィールドがあっても壊れません。

2つ目の引数は、その場に書いたラムダである必要があります。メソッドグループや、変数やフィールド
に持っているデリゲートは [BCF3022](./diagnostics.md#bcf3022) を報告します。生成コードに写される
のはラムダの本体の構文で、宣言が別の場所にあるデリゲートには写す本体がないからです。

すでに `RenderFragment<TContext>` の *値* を持っているなら、スカラーの `.Param` で渡してくだ
さい。どちらのチャネルも同じパラメーターに届きますが、デリゲートの同一性が違い、その違いは動作に
現れます。

```csharp
// コンストラクターで一度だけ組み立てる。パラメーターの参照はレンダリングをまたいで変わらない。
private readonly RenderFragment<EditContext> _fields;
```

状態を読む `.Template` の内容は、その状態を捕捉するので、ラムダはレンダリングのたびに新しい
デリゲートへ変わります。受け取る側のコンポーネントはパラメーターが変わったと見て、テンプレート
を描き直します。`.Param` で渡したキャッシュ済みのデリゲートは変わらないので、描き直しません。
キャッシュする形を使うのは、その安定が欲しいときだけにしてください。ほかの場面では `.Template`
のほうが短く、安全です。キャッシュのように忘れることがないからです。

## 既存の Razor コンポーネントやサードパーティのコンポーネントを呼ぶ

書き方は変わりません。`.razor` で書いたコンポーネントも、MudBlazor や QuickGrid のような
パッケージのコンポーネントも、同じ `Component<T>()` で置きます。

```csharp
protected override View Body =>
    Div[
        Span["Data Grid"],
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)];
```

制限が1つあり、多くの場合これが最初の障害になります。型引数は `OpenComponent<T>` のリテラルと
してそのまま生成コードへ出るので、ジェネレーターの実行時点で解決できなければなりません。Razor の
コンパイラ自身がソースジェネレーターで、ソースジェネレーターは互いの出力を見られません。その
ため *同じプロジェクト* で宣言した `.razor` のコンポーネントは、BlazorCodeFirst のジェネレーター
の実行時点ではまだ存在しません。型引数に書くと [BCF3012](./diagnostics.md#bcf3012) を報告します。

回避する方法は2つです。

- `.razor` のコンポーネントを、参照しているプロジェクトかパッケージへ移す。型はメタデータから
  来るようになり、通常どおり解決します。
- コンポーネントを C# で手書きする。手書きのコンポーネントは通常のソースなので、同じ
  プロジェクトの中でも必ず解決します。

綴り違いや `using` の書き忘れも同じ BCF3012 になり、同じ位置に CS0246 が並びます。

## 値をカスケードする

`CascadingValue<T>` もその既存のコンポーネントの1つです。カスケードのために、この API が足すもの
はありません。`Component<T>()` で置き、`Value` を `.Param` で渡し、それを読む部分木を角括弧に
入れます。

```csharp
protected override View Body =>
    Component<CascadingValue<ThemeInfo>>()
        .Param(c => c.Value, _theme)[
            Component<Toolbar>(),
            Component<Editor>()];
```

`Name` と `IsFixed` も、ほかと変わらない `.Param` の対象です。名前つきのカスケードなら、渡す側
は `.Param(c => c.Name, "locale")` です。受け取る側は `[CascadingParameter(Name = "locale")]` に
なります。

その受け取る側は通常の Blazor で、ジェネレーターはそこを見ません。`[CascadingParameter]` は
クラスのプロパティで、`.razor` のコンポーネントとまったく同じです。

```csharp
public partial class Toolbar : BodyComponentBase
{
    [CascadingParameter]
    public ThemeInfo? Theme { get; set; }

    protected override View Body =>
        Div.Class("toolbar")[Span[Theme?.Name ?? "default"]];
}
```

値を差し替えると、それを読む子孫はすべて再レンダリングされます。自分のフレームが変わらなかった
子孫も含みます。購読しているのは Blazor 自身です。上の呼び出しからジェネレーターが出すフレーム
は、`<CascadingValue Value="@_theme">` が出すものと同じです。

## Razor から BlazorCodeFirst のコンポーネントを使う

逆向きには、この制限がありません。BlazorCodeFirst のコンポーネントは、ただの Blazor の
コンポーネントです。`BodyComponentBase` は `ComponentBase` から派生しているので、`.razor` の
ファイルは、これを通常のタグとして書けます。

```razor
@* ExistingPage.razor *@
<div class="legacy-layout">
    <StatusBadge Status="@currentStatus" />
</div>
```

```csharp
public partial class StatusBadge : BodyComponentBase
{
    [Parameter] public Status Status { get; set; } = default!;

    protected override View Body =>
        Span.Class(Status.IsHealthy ? "badge badge-ok" : "badge badge-alert")[Status.Label];
}
```

これは同じプロジェクトでも動作します。BCF3012 との非対称がなぜあるのかは、知っておくと役に
立ちます。ここで Razor が解決しなければならないのは *クラス名* で、そのクラスの宣言は手で書いた
ソースです。ジェネレーターがその中に書き入れるのは `RenderView` だけで、Razor はそれを見る必要が
ありません。BCF3012 の向きでは、型そのものが生成された出力です。これは別の問題です。

このサイトがそうしています。`App.razor` が `NotFoundPage` を参照しています。これは同じ
プロジェクトの通常の `.cs` ファイルで宣言した、BlazorCodeFirst のコンポーネントです。

## コンポーネントを作らずに分ける: `[ViewPart]`

`Body` の式のどの部分にも、コンポーネントが必要なわけではありません。`[ViewPart]` のメソッドは
UI の断片で、ジェネレーターはこれを、コンポーネントの境界越しに描くのではなく、*呼び出した側の
中へ* 展開します。

```csharp
protected override View Body =>
    Div[
        AppHeader("My Application"),
        BodyContent()];

[ViewPart]
private static View AppHeader(string title) =>
    Div.Class("app-header")[
        Span[title]];
```

呼び出し側に生成される `RenderView` は、ヘッダーのフレームを直接持ちます。コンポーネントの
実体も、パラメーターも、ライフサイクルも、差分の境界もありません。その場にマークアップを書いた
のと同じです。

### コンポーネントの呼び出しに名前を付ける

パーツの本体は通常の設計時の構文なので、要素と同じようにコンポーネントの呼び出しも書けます。
そうするとコンポーネントの呼び出しに固有の名前が付き、呼び出し側からは `Component<T>()` と
`.Param` が消えます。

```csharp
public static class Widgets
{
    [ViewPart]
    public static View Badge(string label, bool compact = false) =>
        Component<StatusBadge>()
            .Param(b => b.Label, label)
            .Param(b => b.Compact, compact);
}

public partial class Dashboard : BodyComponentBase
{
    protected override View Body =>
        Div[
            Widgets.Badge("hello"),
            Widgets.Badge("x", compact: true)];
}
```

呼び出し側は通常の C# の呼び出しなので、名前付き引数と省略可能な引数が使えます。呼び出し側に
生成される `RenderView` は、呼び出しのたびに `StatusBadge` を直接開きます。描かれるツリーは、
`Component<StatusBadge>()` を2回書いたときと同じものです。
`.Param` の規則を検査するのは、セレクターを書いた場所です。[BCF3006](./diagnostics.md#bcf3006)
と BCF3007 は、呼び出す箇所がいくつあっても、パーツの宣言で1回だけ報告されます。

名前を付ける対象のコンポーネントは、パッケージのものを含めてどこにあってもかまいません。
`MudDataGrid<Order>` も、自分で書いたコンポーネントと同じ条件で名前を持てます。パーツのほうは
違います。宣言のソース構文から展開するので、呼び出すプロジェクトの中に置く必要があります
（後述の [BCF1002](./diagnostics.md#bcf1002)）。そのためコンポーネントのライブラリが配れるのは
コンポーネントまでで、名前を付けるパーツは配れません。使う側のプロジェクトがそれぞれ書きます。
BCF3012 の非対称を裏返した形です。

### 内容を包む

呼び出し側が渡す内容を包むパーツは、`View` ではなく `SlotView` を返し、その内容が入る場所に
`Slot` を書きます。呼び出し側は、要素の子を渡すのとまったく同じように、角括弧で渡します。

```csharp
protected override View Body =>
    Div[
        Card("Profile")[P["Body text"]],
        Section.Class("body")[P["…"]]];

[ViewPart]
private static SlotView Card(string title) =>
    Div.Class("card")[
        H2[title],
        Slot];
```

この書き方の狙いはそこにあります。切り出したパーツが、はじめからある要素と同じ形で読めることです。
`Card("Profile")[…]` と `Section.Class("body")[…]` は同じように読め、どちらが自作かは表れません。

角括弧は省けません。そしてそれを強制しているのは C# だけです。`SlotView` から `View` への変換は
ないので、角括弧を忘れた `Div[Card("Profile")]` は、黙って空のカードを描くのではなくコンパイル
エラーになります。同じ性質が、2つの書き方を排除します。装飾（`Card("t").Class("x")`。該当する
拡張メソッドがない）と、引数で渡す書き方（`Card("t", P["x"])`。バインドする引数がない）です。

2つ目のスロットは、通常の `View` の引数です。

```csharp
protected override View Body =>
    Panel(H2["Title"])[
        P["Body text"]];

[ViewPart]
private static SlotView Panel(View header) =>
    Div.Class("panel")[
        Div.Class("panel-head")[header],
        Div.Class("panel-body")[Slot]];
```

名前の付いたチャネルが先、主な内容は角括弧の中です。`Div.Class("card")[…]` や
`Component<T>().Template(…)[…]` が、この API で既に取っている形です。

スロットは、要素の角括弧と同じようにコンポーネントの角括弧にも置けます。名前を付けたコンポー
ネントの呼び出しが内容を受け取るのは、この形です。

```csharp
[ViewPart]
private static SlotView Framed(string title) =>
    Component<Card>().Param(c => c.Title, title)[Slot];
```

呼び出し側の内容は、[子の内容を渡す](#子の内容を渡す)の規則どおり `Card.ChildContent` に届き
ます。

規則が2つあります。`SlotView` のパーツは、`Slot` を **ちょうど1回** 書く必要があります。2回書けば、
1つの角括弧から呼び出し側の内容を2度出すことになります。一度も書かなければ、呼び出し側が渡すよう
義務づけられた内容を捨てることになります。どちらも [BCF3025](./diagnostics.md#bcf3025) です。
呼び出し側の内容が来ない場所に書いた `Slot` も同じで、コンポーネント自身の `Body` や、`View` を
返すパーツがこれにあたります。

対して `View` の引数は、何度参照してもかまいません。通常の引数だからです。何も捕捉せず、
共有もしません。参照するたびに呼び出し側の式を展開し直すので、副作用のある引数は参照の数だけ
実行されます。Blazor の `RenderFragment` を2回呼んだときと同じ振る舞いです。

ただし、どちらも内容であって、内容に値はありません。式ではなくフレームになるからです。そのため
スロットは、子として *置く* ことしかできません。値が必要な場所で読むと **BCF1002** です。`ForEach`
のキーや、属性の値の中がそれにあたります。

トレードオフの全体を、観点ごとに並べます。

| | `[ViewPart]` | コンポーネント |
| --- | --- | --- |
| 状態とライフサイクル | 持たない。メソッドだから | Blazor のコンポーネントとして自前で持つ |
| 再レンダリング | 自分の境界がないので、呼び出し側と一緒に | 自分の差分境界で、自分だけで |
| 呼び出し側のフレームが持つもの | パーツのフレームが、その場に展開される | コンポーネントを開くフレーム 1 つ |
| 引数 | 値渡しの引数。名前付きも省略可能も使える | `[Parameter]` のプロパティを `.Param` で渡す |
| 別のアセンブリから | 使えない (BCF1002) | 使える |

`[ViewPart]` は、ジェネレーターが展開できる宣言の契約を満たす必要があります。満たさなければ
**BCF1002** です。メソッドは次の形でなければなりません。

- 静的である
- ジェネリックでなく、ジェネリックでない型で宣言されている
- 返り値の式1つに収まる。その手前にはローカル宣言と式文を置ける。`Body` のゲッターと同じ形。
  または、繰り返し1回につき子を1つ yield する `foreach` で終わる形
  （[`[ViewPart]` でイテレートする](./control-flow.md#viewpart-でイテレートする)を参照）
- `View` を返す。内容を取るなら `SlotView`。その `foreach` で終わる形なら `IEnumerable<View>`

引数は、通常の値渡しの引数である必要があります。型は、生成コードから名前を書けるものに限り
ます。`params`、参照渡しの引数、`ElementView` の引数は、どれも拒否します。子を持たない要素を
内容として渡すには、`Div[…]` か `Fragment(Div)` と書きます。どちらも `View` です。`View` の引数
は内容のスロットなので、返り値の型は `SlotView` でなければなりません。`View` を返すパーツに書けば
BCF1002 で、省略可能にはできません。

拡張メンバーであってもいけません。`this` 引数も、`extension` ブロックのメンバーも同じです。
呼び出しは、通常の呼び出しとして書きます（`AppHeader("My Application")`）。この API が、要素
への装飾でない呼び出しを、すべてそう書いているからです。メソッドチェーンで書くと、装飾でない
ものが、この API が装飾のために空けている位置へ入ります。そのレシーバーは、必ずほかの型の値に
なります。それでは `[ViewPart]` は、`Body` を分ける手段ではなく、*その型* の API を増やす手段に
なってしまいます。

BCF1002 は *呼び出し側* でも出ます。その条件の1つは、はっきり書いておく価値があります。

**`[ViewPart]` はアセンブリの境界を越えられません。** 呼び出しを展開するには宣言のソースの構文が
必要で、ジェネレーターは自身が動作しているコンパイルから宣言を集めます。IL は本体の構文を持たないの
で、参照しているプロジェクトやパッケージにある `[ViewPart]` は、呼び出した場所で必ず BCF1002 に
なります。展開が再帰して循環する場合と、展開する場所から見えない `private` や `protected` の
メンバーに本体が触れている場合も、同じ診断です。

そのパーツを別のプロジェクトで使いたいなら、コンポーネントにして `Component<T>()` から使って
ください。

BCF1002 は `[ViewPart]` だけの診断ではありません。コンポーネント自身の `Body` とレイアウトの
`Chrome` も同じ検査を通ります。そちらから出た報告は、メソッドではなく式を指します。
[BCF1002](./diagnostics.md#bcf1002)を見てください。

## 次に読むもの

- `If` とキー付き `ForEach` は[制御構文](./control-flow.md)へ。
- ルーティングされたページを共通の外枠で包む方法は[レイアウト](./layouts.md)へ。
- パラメーターを渡すもう1つの方法である `.Bind` は、
  [双方向バインディング](./two-way-binding.md#コンポーネントのパラメーターをバインドする)へ。
