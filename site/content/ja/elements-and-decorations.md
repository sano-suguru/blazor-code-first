---
title: 要素と装飾
order: 20
source-hash: 2eb7120f
---

BlazorCodeFirst は HTML を直に写します。`Body` の式に書いた要素の名前が、そのまま出力される要素
の名前です。あいだに挟まるウィジェットの語彙を覚える必要はなく、実行時の UI ツリーもありません。

## 要素

HTML の要素にはヘルパーがあります。名前はタグの先頭1文字だけを大文字にした綴りで、`FigCaption`
ではなく `Figcaption`、同じく `Colgroup` と `Textarea` です。属性は要素に繋げ、子は角括弧に入れ
ます。

```csharp
protected override View Body =>
    Figure[
        Img.Src("/diagram.png").Alt("Architecture"),
        Figcaption["The compilation pipeline"]];
```

```html
<figure>
    <img src="/diagram.png" alt="Architecture">
    <figcaption>The compilation pipeline</figcaption>
</figure>
```

HTML Living Standard が適合と認める要素には、すべて専用のヘルパーがあります。

`Element` は、そこから漏れたものを受け持ちます。届くのは2種類です。ひとつはカスタム要素と Web
Components で、そのタグ名にヘルパーはありません。もうひとつは、ヘルパーが用意されていない数少
ない標準要素です。

- 文書そのものと `<head>` の中だけに現れる要素。
  `html`、`head`、`body`、`title`、`base`、`meta`、`link`
- 生テキスト要素。`script`、`style`、`noscript`
- レンダーツリーが意味を与えられない要素。`template`、`slot`
- `object`。C# のキーワードと綴りがぶつかります
- 別の語彙である `svg` と `math`。その配下すべて

```csharp
private const string Widget = "my-widget";

Element(Widget).Attr("value", "42")        // カスタム要素
Element("svg")[Element("circle")]          // 別の語彙
Element(_kind + "-widget")                 // BCF3009: 定数ではない
Element("my widget")                       // BCF3009: タグ名の綴りではない
```

タグは、タグ名として綴られたコンパイル時定数である必要があります。でなければ
[BCF3009](./diagnostics.md#bcf3009) を報告します。

## 子に置けるもの

文字列をそのまま書くと、テキストノードになります。だから `Text()` のような構文は別に用意して
いません。子の無い要素は角括弧ごと省きます。

```csharp
protected override View Body =>
    Div[
        "plain text, then ",
        A.Href("/docs")["a link"],
        Br,
        Img.Src("/logo.png").Alt("Logo")];
```

```html
<div>plain text, then <a href="/docs">a link</a><br><img src="/logo.png" alt="Logo"></div>
```

Blazor の `RenderFragment` は、ほかの子と同じように子の並びへ置けます。

```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }

protected override View Body => Div["before", ChildContent];
```

ジェネレーターは、子のひとつひとつを独立した式として読めなければ、シーケンス番号を振れません。
子をコレクション式で入れ子に書くのは受け付けます。C# がそれを同じ呼び出しへ展開するからです。

```csharp
Div[["a", "b"]]     // Div["a", "b"] と同じ。書くならこちら
```

ジェネレーターが中を見通せない子の並びを渡すと、[BCF1003](./diagnostics.md#bcf1003) を報告します。
繰り返しには [`ForEach`](./control-flow.md#foreach-とそのキー) を使ってください。

## 空要素は子を取らない

HTML 標準の空要素は13個あり、どれも閉じタグを持ちません。だから子を書くと
[BCF3016](./diagnostics.md#bcf3016) を報告します。

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016。同じ規則
Img.Src("/logo.png").Alt("Logo") // こう書く
```

空要素は装飾で設定して、中身はその隣に置いてください。この API が HTML について検査するのは、
ここまでです。この線引きは意図したものです。`Table[Div["x"]]` もハイドレーションの後で表示が
変わりますが受け付けますし、要素が定義していない属性（`Div.Href("/x")`）も同じです。立場の全体は
`DESIGN.md` §4.1 にあります。

## 名前がぶつかったとき

`using static BlazorCodeFirst.Html;` は、適合する HTML 要素の名前をすべて取り込みます。そして
単純名の解決では、自分で宣言した名前が取り込まれた名前に勝ちます。Blazor のパラメーターを
`Label`、`Data`、`Summary`、`Source` と名付けるのはありふれているので、これは起こります。

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027
Div[Html.Data["Heading"]]                     // こう書く
```

自分の型、名前空間、メソッドも同じように名前を奪います。どれも
[BCF3027](./diagnostics.md#bcf3027) で、見つけたものを名指しします。

## 装飾

装飾は、それが属する要素に、子より前に繋げて書きます。HTML が属性をタグの中に書くのと同じ並び
です。装飾はラッパーのノードを作らず、持ち主の要素の属性へ畳み込まれます。`class` は畳み込まれ
るので、`.Class` を2回以上繋げると値は1つの属性にまとまります。

```csharp
protected override View Body =>
    Button
        .Class("btn")
        .Class("btn-primary")
        .Title("Save the current document")["Save"];
```

```html
<button class="btn btn-primary" title="Save the current document">Save</button>
```

使える装飾は `.Class`、`.Id`、`.Href`、`.Src`、`.Alt`、`.Type`、`.Title`、`.Role`、`.OnClick`。
それに、汎用の逃げ道として `.Attr(name, value)` と `.On(eventName, handler)` があります。

`.On` には `on` を含んだ属性名をそのまま渡します（`.On("onmouseenter", …)`）。接頭辞をこちらで
補うことはなく、`on` の無い名前は [BCF3019](./diagnostics.md#bcf3019) です。`.Attr` や `.On` に
渡す名前は、空でないコンパイル時定数である必要があります（[BCF3011](./diagnostics.md#bcf3011)）。

`.Attr` は `string?` か `bool` を取ります。`bool` は Blazor の条件付き属性です。`true` なら値の
空な属性として出力します。HTML が `disabled`、`checked`、`hidden` を有効と読むのは、この形です。
`false` なら属性ごと出しません。いつも付く属性は、HTML と同じように値なしで書いてください。
`bool` は、付いたり付かなかったりする場合のためにあります。

```csharp
Input.Type("checkbox").Attr("checked")                    // <input type="checkbox" checked>
Button.Attr("disabled", _submitting)["Save"]              // 条件付き
```

文字列の `null` も属性を出しません。だから、値が付くこともあれば付かないこともある属性のために、
要素を分岐で囲む必要はありません。

```csharp
Span.Attr("title", _hasTip ? _tip : null)["Hover me"]
```

`null` と `""` は別の値です。フレームでも、プリレンダリングされた HTML でも、再レンダリングでも
変わりません。`""` は `title=""` になり、`null` では `title` そのものが出ません。再レンダリング
で値が null になったとき、Blazor は要素を差し替えるのではなく、すでに DOM にある要素から属性
を取り除きます。値を1つ取る装飾は、どれもこの意味で `null` を受け付けます。

`object` のオーバーロードは、あえて用意していません。ほかの型の値は、レンダリングの時点で、書式
化を行うスレッドのカルチャーによって文字列になります。コンポーネントが動いたときのカルチャーで
はありません。だから自分で書き出してください。そうすれば、どのカルチャーを選んだのかが目に
見えます。

```csharp
Div.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))
```

### ハンドラー

`Action` や `Func<Task>` として書いたハンドラーは、何も受け取りません。イベントを読むには、ラムダ
の引数に型を書きます。すると `.On` が型付きのオーバーロードを選びます。

```csharp
Input.Type("text").Attr("value", _name)
     .On("oninput", (ChangeEventArgs e) => _name = e.Value?.ToString() ?? "")
```

Razor と違って、引数の型はイベント名から推論されません。オーバーロードを選ぶのは、引数に書いた型
です。`ChangeEventArgs` は `Microsoft.AspNetCore.Components` にあります。`MouseEventArgs`、
`KeyboardEventArgs`、`FocusEventArgs` は `Microsoft.AspNetCore.Components.Web` です。
`.Web` のほうも、Blazor のアプリがすでに参照している名前空間です。

型は推論されませんが、検査はされます。そのイベントが渡さない型を書くと
[BCF3028](./diagnostics.md#bcf3028) を報告します。判断のもとは、Razor が使うのと同じ
`[EventHandler]` のメタデータです。渡される型の基底クラスは受け付けます。ハンドラーが実際に
受け取れるのがそれだからです。

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // 渡される型
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // その基底。これでよい
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

`[EventHandler]` の登録が無いイベントには、照合する対応表がありません。だから登録していない
カスタムイベントには何も言いません。登録は Blazor のふつうの仕組みで、自分のプロジェクトでの
登録も読みます。

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

属性を出して、イベントを受け取る。この対をひとつの装飾で書くのが
[`.Bind`](./two-way-binding.md)です。

### class のチャネル

class のチャネルは、値をテキストとして繋ぎます。だから `class` は、文字列しか取らない唯一の
名前です。`.Attr("class", flag)` は [BCF3023](./diagnostics.md#bcf3023) を報告します。
`.Attr("class")` も同じです。値を書かない形は、属性があることを表すだけで、繋ぐ文字列を持ち
ません。条件付きのクラスは文字列で書き、消したい項には `null` を渡してください。

```csharp
Div.Class("card").Class(_selected ? "is-selected" : "")
```

`null` の項は連結から外れます。だから class の装飾を1つだけ持つ要素は、その項が null のとき属性
ごと消えます。繋ぐ相手がもう1つあるときは、区切りだけが残ります。
`Div.Class("card").Class(_selected ? "is-selected" : null)` を例にします。`_selected` が false
のあいだ、出力は `class="card "` です。ブラウザーはこれを `card` 1つのクラスとして読みます。

ほかの属性とイベントは、どれも1回しかバインドできません。同じ要素に2度書くと
[BCF3010](./diagnostics.md#bcf3010) を報告します。`style` もそのひとつで、`.Attr("style", …)` と
書きます。`.Bind("class", …)` は、この名前を書く3つ目の方法で、畳み込まれない唯一の方法です。
だから `.Class` と併せて書いた要素は [BCF3024](./diagnostics.md#bcf3024) を報告します。

### 装飾を書ける場所

装飾は、単一の要素を相手にしなければなりません。`If`、`ForEach`、`Fragment`、`Raw`、コンポーネント
の結果に付けると [BCF3008](./diagnostics.md#bcf3008) を報告します。子の後ろに繋げて書いた場合
（`Div["text"].Class("card")`）も同じです。角括弧は、もう `View` を作り終えています。

装飾は、このライブラリが宣言しているものである必要もあります。綴り違い（`Div.Clas("card")`）や、
要素を取って要素を返す自作の拡張メソッドは [BCF3026](./diagnostics.md#bcf3026) を報告します。

## 次に読むもの

- [はじめに](./getting-started.md#導入)に戻る。
- 条件分岐とリストは[制御構文](./control-flow.md)へ。
