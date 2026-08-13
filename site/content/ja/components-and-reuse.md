---
title: コンポーネントと再利用
order: 40
source-hash: 79289534
---

再利用の単位はコンポーネントです。BlazorCodeFirst のコンポーネントから別のコンポーネントを呼ぶ
には `Component<T>()` を使います。既存の Razor コンポーネントも、サードパーティのコンポーネント
も、まったく同じ呼び方です。逆に `.razor` のファイルからは、BlazorCodeFirst のコンポーネントを
ふつうのタグとして名指しできます。`[ViewPart]` は別の仕事のための別の道具で、このページの終わり
で扱います。

## BlazorCodeFirst のコンポーネントを呼ぶ

`Component<T>()` は、コンポーネントをツリーに置きます。パラメーターは `.Param` で束縛し、相手の
プロパティはラムダで名指しします。

```csharp
protected override View Body =>
    Div.Class("dashboard")[
        Component<StatusBadge>()
            .Param(b => b.Status, _status)
            .Param(b => b.Compact, true)];
```

ジェネレーターは `.Param` のひとつひとつを静的なパラメーターの設定に変え、
`AddComponentParameter` の呼び出しとして書き出します。リフレクションは使わず、実行時に式ツリーを
コンパイルすることもありません。これが、この経路をトリミングと AOT に対して安全に保っています。

書ける形が診断で囲ってあるのも、それが理由です。セレクターのラムダでパラメーターを名指しする
経路は、どれもこの診断に従います。`.Param` だけではありません。`.Template` も、コンポーネントに
対する `.Bind` も同じです。

- セレクターは、プロパティを選ぶだけの式である必要があります。キャスト、メソッド呼び出し、捕捉
  した変数のメンバーは BCF3005 を報告します。どれも、ジェネレーターが設定を書き出せるプロパティ
  を名指ししていないからです。
- 相手は、設定できる `[Parameter]` のプロパティである必要があります。でなければ BCF3006 を報告
  します。そうでなければ Blazor が実行時に例外を投げるので、拒否をコンパイル時へ前倒ししています。
- 同じプロパティを2度束縛すると BCF3007 を報告します。2つの束縛がどの経路から来たかは問いません。
  Blazor は最後の値しか適用しないので、先の束縛は黙って死にます。

## 子の内容を渡す

入れ子に書いた子は `ChildContent` に束縛されます。入れ子の内容は `ChildContent` にしかならない、
という Razor の規則をそのまま写しています。

```csharp
protected override View Body =>
    Component<Card>()[
        H2["Heading"],
        P["Body text"]];
```

これには `Card` の側に、フラグメント型で設定できる `[Parameter] ChildContent` が必要です。無ければ
BCF3013 を報告します。`RenderFragment<TContext>` も対象です。角括弧はコンテキストを捨てて束縛し
ます。角括弧の中には、コンテキストを読むための名前が無いからです。`ChildContent` 以外の名前を持つ
ジェネリックなフラグメントは `.Template` で名指しします。下の
[ジェネリックなフラグメントのパラメーター](#ジェネリックなフラグメントのパラメーター)を見て
ください。

ジェネリックでないほかの `RenderFragment` パラメーターは、`Footer` や `Header` などです。これらは
`.Param(c => c.Footer, content)` と書き、パラメーターを明示して束縛します。

```csharp
protected override View Body =>
    Component<Card>()
        .Param(c => c.Title, "Card title")
        .Param(c => c.Footer, Span["Footer note"])[
            H2["Heading"],
            P["Body text"]];
```

`ChildContent` を `.Param` で名指しするのも正しい書き方です。冗長ですが、Razor の属性の形
（`<Card><ChildContent>...</ChildContent></Card>`）に対応します。同じパラメーターを両方の経路
から束縛すると BCF3007 です。

本物の `RenderFragment` の値は、BlazorCodeFirst の `View` の式とは違います。ジェネリックな
`.Param<TValue>` のオーバーロードで束縛し、そのまま書き出されます。

どちらのオーバーロードが走るかは、相手のパラメーターの型で決まります。`RenderFragment?` の
パラメーターなら内容のオーバーロード、それ以外ならジェネリックなほうです。だから
`RenderFragment` でないパラメーターに向けた内容は、ジェネリックなオーバーロードに落ちます。
そこでは値がそのまま書き出されますが、設計時の式の実行時の値は空の目印にすぎません。これが
BCF3014 です。

```csharp
[Parameter] public object? Payload { get; set; }

Component<Card>().Param(c => c.Payload, Div["x"])   // BCF3014
```

この診断が無ければ、失敗は見えないか、遅れて出てきます。`object` 型のパラメーターは目印を例外
も出さずに受け取り、間違った出力を描きます。型の付いたパラメーターは、Blazor がパラメーターを
適用している最中に不正なキャストで例外を投げます。`View`、`ElementView`、`ComponentView<T>`、
`SlotView` は、どれも同じように報告します。内容を渡したいなら、受け取る側のコンポーネントに
`RenderFragment` のパラメーターを持たせてください。

パラメーターの値の中で型名が解決できない場合は、
[生成コードに写される値](./getting-started.md#生成コードに写される値)を見てください。

## ジェネリックなフラグメントのパラメーター

`RenderFragment<TContext>` のパラメーターが取るのは *テンプレート* です。コンポーネントは、描き
たいコンテキストの値ごとに、それを1回ずつ呼び出します。最初に出会うのはたいてい
`EditForm.ChildContent` でしょう。これは `RenderFragment<EditContext>` です。

こうしたパラメーターを名指しするのが `.Template` です。書き方は2通りあり、どちらを使うかは、
内容がコンテキストを読むかどうかだけで決まります。グリッドの `RowTemplate` のように
`ChildContent` 以外の名前を持つものには、常に `.Template` が要ります。角括弧はそこへ届きません。

コンテキストを読まない `ChildContent` は角括弧で書きます。上に出した形であり、
`.Template(form => form.ChildContent, content)` と同じものを発行します。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

使うなら、コンテキストから内容へのラムダで名指しします。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)
        .Template(form => form.ChildContent, editContext =>
            Fragment(
                Span[editContext.IsModified() ? "Unsaved changes" : "No changes"],
                Component<NameFields>().Param(fields => fields.Value, _model)));
```

2つ目の例には、注意が1つ要ります。でないとバッジは一度も変わりません。`IsModified()` は
テンプレートが走るときに読まれます。しかし `EditForm` と `CascadingValue` の連なりには、
`OnFieldChanged` で再レンダリングするものがありません。フィールドに入力すれば `EditContext` に
通知は飛びます。ただ、その通知を誰も購読していないので、テンプレートを持つコンポーネントは
再レンダリングされず、バッジは最初に描かれた文字のままです。これは Blazor のレンダリングの
伝わり方の話であって、テンプレートが受け取るコンテキストの制限ではありません。テンプレートには、
走るたびに生きた `EditContext` が渡されます。

だからテンプレートが変化するコンテキストの状態を読むなら、再レンダリングはフォームを持つ
コンポーネント自身の仕事になります。`Model` に作らせるのではなく、`EditContext` を自分で組み立て
ます。そして `OnFieldChanged` を購読し、`StateHasChanged` を呼び、`Dispose` で購読を解きます。

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
テンプレートに、この用意は要りません。

`RenderFragment<TContext>` のラムダはジェネレーターが書くので、その中身はふつうの
BlazorCodeFirst で、シーケンス番号も周りから続きます。コンテキストの引数の名前は自由に決められ
ます。生成コードは自前の名前を使い、参照していた箇所を書き換えるので、たまたま同じ名前の
フィールドがあっても壊れません。

2つ目の引数は、その場に書いたラムダである必要があります。メソッドグループや、変数やフィールド
に持っているデリゲートは **BCF3022** を報告します。生成コードに写されるのはラムダの本体の構文で、
宣言が別の場所にあるデリゲートには写す本体がないからです。

すでに `RenderFragment<TContext>` の *値* を持っているなら、スカラーの `.Param` で渡してくだ
さい。どちらの経路も同じパラメーターに届きますが、デリゲートの同一性が違い、その違いは見える
ところに出ます。

```csharp
// コンストラクターで一度だけ組み立てる。パラメーターの参照はレンダリングをまたいで変わらない。
private readonly RenderFragment<EditContext> _fields;
```

状態を読む `.Template` の内容は、その状態を捕捉します。だからラムダは、レンダリングのたびに
新しいデリゲートへ変わります。受け取る側のコンポーネントはパラメーターが変わったと見て、テンプ
レートを描き直します。`.Param` で渡したキャッシュ済みのデリゲートは変わらないので、描き直しま
せん。キャッシュする形に手を伸ばすのは、その安定が欲しいときだけにしてください。ほかの場面では
`.Template` のほうが短く、安全です。キャッシュのように忘れることがないからです。

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

制限が1つあり、たいていの人が最初にぶつかる壁がこれです。型引数は `OpenComponent<T>` として
そのまま生成コードに落ちるので、ジェネレーターが走る時点で解決できなければなりません。Razor の
コンパイラー自身がソースジェネレーターで、ソースジェネレーターは互いの出力を見られません。だから
*同じプロジェクト* で宣言した `.razor` のコンポーネントは、BlazorCodeFirst のジェネレーターが
走る時点ではまだ存在しません。名指しすると **BCF3012** を報告します。

回避の道は2つです。

- `.razor` のコンポーネントを、参照しているプロジェクトかパッケージへ移す。型はメタデータから
  来るようになり、ふつうに解決します。
- コンポーネントを C# で手書きする。手書きのコンポーネントはふつうのソースなので、同じ
  プロジェクトの中でも必ず解決します。

綴り違いや `using` の書き忘れも同じ BCF3012 になり、同じ位置に CS0246 が並びます。

## Razor から BlazorCodeFirst のコンポーネントを使う

逆向きには、この制限がありません。BlazorCodeFirst のコンポーネントは、ただの Blazor の
コンポーネントです。`BodyComponentBase` は `ComponentBase` から派生しています。だから `.razor`
のファイルは、これをふつうのタグとして名指しできます。

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

これは同じプロジェクトでも動きます。BCF3012 との非対称がどこから来るのかは、知っておく価値が
あります。ここで Razor が解決しなければならないのは *クラス名* で、そのクラスの宣言は手で書いた
ソースです。ジェネレーターがその中に書き入れるのは `RenderView` だけで、Razor はそれを見る必要が
ありません。BCF3012 の向きでは、型そのものが生成された出力です。これは別の問題です。

このサイトがそうしています。`App.razor` が `NotFoundPage` を名指ししています。これは同じ
プロジェクトのふつうの `.cs` ファイルで宣言した、BlazorCodeFirst のコンポーネントです。

## コンポーネントを作らずに分ける: `[ViewPart]`

`Body` の式のどの部分にも、コンポーネントが要るわけではありません。`[ViewPart]` のメソッドは
UI の一片で、ジェネレーターはこれを、コンポーネントの境界越しに描くのではなく、*呼び出した側の
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

呼び出した側の生成された `RenderView` は、ヘッダーのフレームを直接持ちます。コンポーネントの
実体も、パラメーターも、ライフサイクルも、差分の境界もありません。その場にマークアップを書いた
のと同じです。

### コンポーネントの呼び出しに名前を付ける

パーツの本体はふつうの設計時の構文なので、要素と同じようにコンポーネントの呼び出しも書けます。
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

protected override View Body =>
    Div[
        Widgets.Badge("hello"),
        Widgets.Badge("x", compact: true)];
```

呼び出し側はふつうの C# の呼び出しなので、名前付き引数と省略可能な引数が使えます。展開はあく
まで展開です。呼び出した側の生成された `RenderView` は、呼び出しのたびに `StatusBadge` を直接
開きます。だから描かれるツリーは、`Component<StatusBadge>()` を2回書いたときと同じものです。

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

この書き方の狙いはそこにあります。切り出したパーツが、はじめからある要素と同じ見え方で読める
こと。`Card("Profile")[…]` は `Section.Class("body")[…]` の隣に並んでも、どちらが自分のものかを
主張しません。

角括弧は省けません。そしてそれを強制しているのは C# だけです。`SlotView` から `View` への変換は
ないので、角括弧を忘れた `Div[Card("Profile")]` は、黙って空のカードを描くのではなくコンパイル
エラーになります。同じ性質が、2つの書き方を締め出します。装飾（`Card("t").Class("x")`。該当する
拡張メソッドがない）と、引数で渡す書き方（`Card("t", P["x"])`。束縛する引数がない）です。

2つ目のスロットは、ふつうの `View` の引数です。

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

名前の付いた経路が先、主な内容は角括弧の中。`Div.Class("card")[…]` や
`Component<T>().Template(…)[…]` が、この API で既に取っている形です。

知っておきたい規則が2つあります。`SlotView` のパーツは、`Slot` を **ちょうど1回** 名指しする
必要があります。2回名指しすれば、1つの角括弧から呼び出し側の内容を2度出すことになります。一度も
名指ししなければ、呼び出し側が渡すよう義務づけられた内容を捨てることになります。どちらも
**BCF3025** です。呼び出し側の内容が来ない場所に書いた `Slot` も同じで、コンポーネント自身の
`Body` や、`View` を返すパーツがこれにあたります。

対して `View` の引数は、何度参照してもかまいません。ふつうの引数だからです。捕捉も共有もしま
せん。参照するたびに呼び出し側の式を展開し直すので、副作用のある引数は参照の数だけ走ります。
Blazor の `RenderFragment` を2回呼んだときと同じ振る舞いです。

ただし、どちらも内容であって、内容に値はありません。式ではなくフレームになるからです。だから
スロットは、子として *置く* ことしかできません。値が要る場所で読むと **BCF1002** です。`ForEach`
のキーや、属性の値の中がそれにあたります。

取捨はこれで全部です。観点ごとに並べます。

| | `[ViewPart]` | コンポーネント |
| --- | --- | --- |
| 状態とライフサイクル | 持たない。メソッドだから | Blazor のコンポーネントとして自前で持つ |
| 再レンダリング | 自分の境界がないので、呼び出し側と一緒に | 自分の差分境界で、自分だけで |
| 呼び出し側のフレームの中身 | パーツのフレームが、その場に展開される | コンポーネントを開くフレーム 1 つ |
| 引数 | 値渡しの引数。名前付きも省略可能も使える | `[Parameter]` のプロパティを `.Param` で渡す |
| 別のアセンブリから | 使えない (BCF1002) | 使える |

`[ViewPart]` は、ジェネレーターが展開できる宣言の約束を満たす必要があります。でなければ
**BCF1002** です。メソッドは次の形でなければなりません。

- 静的である
- ジェネリックでなく、ジェネリックでない型で宣言されている
- 1 つの `return` へ到達する。その手前にはローカル宣言と式文を置ける。`Body` のゲッターと同じ形
- `View` を返す。内容を取るなら `SlotView`

引数は、生成コードから名前を書ける型の、ふつうの値渡しの引数である必要があります。`params`、
参照渡しの引数、`ElementView` の引数は、どれも拒否します。子を持たない要素を内容として渡すには、
`Div[…]` か `Fragment(Div)` と書きます。どちらも `View` です。`View` の引数は内容のスロットな
ので、返り値の型は `SlotView` でなければなりません。`View` を返すパーツに書けば BCF1002 で、
省略可能にはできません。

拡張メンバーであってもいけません。`this` 引数も、`extension` ブロックのメンバーも同じです。
呼び出しは、ふつうの呼び出しとして書きます（`AppHeader("My Application")`）。この API が、要素
への装飾でない呼び出しを、すべてそう書いているからです。流れるような書き方をすると、装飾でない
ものが、この API が装飾のために空けている位置へ入ります。しかもそのレシーバーは、どう
やってもほかの型の値にしかなりません。それでは `[ViewPart]` は、`Body` を分ける道具ではなく、
*その型* の API を増やす道具になってしまいます。

BCF1002 は *呼び出し側* でも出ます。その条件の1つは、はっきり書いておく価値があります。

**`[ViewPart]` はアセンブリの境界を越えられません。** 呼び出しを展開するには宣言のソースの構文が
要り、ジェネレーターは自分が走っているコンパイルから宣言を集めます。IL は本体の構文を持たないの
で、参照しているプロジェクトやパッケージにある `[ViewPart]` は、呼び出した場所で必ず BCF1002 に
なります。展開が再帰して循環する場合と、展開する場所から見えない `private` や `protected` の
メンバーに本体が触れている場合も、同じ診断です。

そのパーツを別のプロジェクトで使いたいなら、コンポーネントにして `Component<T>()` から使って
ください。

## 次に読むもの

- `If` とキー付き `ForEach` は[制御構文](./control-flow.md)へ。
- ルーティングされたページを共通の外枠で包む方法は[レイアウト](./layouts.md)へ。
- パラメーターを渡すもう1つの方法である `.Bind` は、
  [双方向の束縛](./two-way-binding.md#コンポーネントのパラメーターを束縛する)へ。
