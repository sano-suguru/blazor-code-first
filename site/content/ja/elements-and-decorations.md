---
title: 要素と装飾
order: 20
source-hash: a54e521a
---

BlazorCodeFirst は HTML を直に写します。`Body` の式に書いた要素の名前が、そのまま出力される要素
の名前です。あいだに挟まるウィジェットの語彙を覚える必要はなく、実行時の UI ツリーもありません。
ソースジェネレーターが、これらの呼び出しをコンパイル時に `RenderTreeBuilder` の命令へ変換します。

## 要素

要素のヘルパーは、文字列と要素を混ぜた子を角括弧で受け取ります。

```csharp
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

protected override View Body =>
    Article[
        H2["要素"],
        P["テキストと ", A.Href("/docs")["リンク"], " は1回の呼び出しで並びます。"],
        Ul[
            Li["HTML の要素にはヘルパーがあります。名前はタグの先頭1文字だけを大文字にした綴りで、FigCaption ではなく Figcaption、同じく Colgroup と Textarea です。"],
            Li["ヘルパーの無いものを Element が受け持ちます。カスタム要素、Web Components、そして下に挙げるいくつかの語彙です。"]]];
```

HTML Living Standard が適合と認める要素には、すべて専用のヘルパーがあります。だから `<figure>`
も呼び出しひとつです。属性は子より前、ほかの要素と同じです。

```csharp
Figure[
    Img.Src("/diagram.png").Alt("Architecture"),
    Figcaption["The compilation pipeline"]]
```

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
Element("my-widget").Attr("value", "42")   // カスタム要素
Element("svg")[Element("circle")]          // 別の語彙
```

タグは空でないコンパイル時定数、つまりリテラルか `const` である必要があります。でなければ
BCF3009 を報告します。`Element` はタグをリテラルの `OpenElement` に落とします。定数に縛ることで、
その呼び出しはヘルパーと同じくらい読めるものになります。この規則が守っているのは安全性ではなく、
宣言的であることです。計算されたタグは、インジェクションの危険でも順序付けの問題でもありません。
ただ、書いた場所で要素が自分の名前を名乗らなくなるだけです。

定数の綴りも、タグ名の形である必要があります。先頭がASCII英字、以降はASCII英数字・`-`・`_`・`.`
です。要素が名乗れない綴りは、2つの経路が別のものを描画し、そのどちらも書いたとおりになりません。
プリレンダはマークアップへ書き出すため、HTMLパーサが別のものとして読み直します。interactive描画は
`createElement` に渡すため、拒否されて回路ごと落ちます。

```csharp
private const string Widget = "my-widget";

Element(Widget)                  // これでよい
Element(_kind + "-widget")       // BCF3009
Element("")                      // BCF3009。タグは空にできない
Element("my widget")             // BCF3009。タグ名の綴りでない
```

## 子に置けるもの

文字列をそのまま書くと、テキストノードになります。だから `Text()` のような構文は別に用意して
いません。子の無い要素は角括弧ごと省きます。Blazor の `RenderFragment` は、ほかの子と同じように
子の並びへ置けます。

```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }

protected override View Body =>
    Div[
        "plain text",
        Img.Src("/logo.png").Alt("Logo"),
        ChildContent];
```

ジェネレーターは、子のひとつひとつを独立した式として見られないと、シーケンス番号を振れません。
子をコレクション式で入れ子に書くのは受け付けます。C# がそれを同じ呼び出しへ展開するからです。

```csharp
Div[["a", "b"]]     // Div["a", "b"] と同じ。書くならこちら
```

ジェネレーターが中を見通せない子の並びを渡すと、BCF1003 を報告します。当てはまるのは3つです。

- 変数やメソッドの結果をまるごと渡す（`Div[_kids]`）
- 明示的な配列（`Div[new View[] { … }]`）
- あらゆるスプレッド（`Div[[..items]]`）

繰り返しには [`ForEach`](./control-flow.md#キー付き-foreach) を使ってください。

## 空要素は子を取らない

HTML 標準の空要素は13個あります。`area`、`base`、`br`、`col`、`embed`、`hr`、`img`、`input`、
`link`、`meta`、`source`、`track`、`wbr`。どれも閉じタグを持たないので、子を書くと BCF3016 を
報告します。

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016。同じ規則
Img.Src("/logo.png").Alt("Logo") // こう書く
```

理由は、その子が HTML を往復して生き残らないからです。プリレンダリングは、HTML パーサーが受け
付けない閉じタグを書き出します。するとパーサーは子を要素の外へ押し出し、子は兄弟として現れます。
行き場のない `</br>` は開始タグとして読み直されるので、`Br["x"]` は `<br>` 2つとしてプリレンダ
リングされます。対話的なレンダリングには間にパーサーがいないので、同じ子は要素の中に入ります。
式は1つ、DOM ツリーは2通り。ハイドレーションが引き継ぐところで、ページの形が変わります。空
要素は装飾で設定して、中身はその隣に置いてください。

検査するのは両方の書き方です。ヘルパーと、空要素のタグを渡した `Element`。カスタム要素と未知の
タグは検査しません。`Element("img-viewer")["child"]` は受け付けます。その内容モデルを読み取れる
標準がないからです。

この API が HTML について検査するのは、ここまでです。この線引きは意図したものです。BCF3016 は
要素のタグだけで判定できます。ある子がある親の中に置けるかどうかは、そうはいきません。
`Table[Div["x"]]` もハイドレーションの後で表示が変わりますが、受け付けます。要素が定義していない
属性（`Div.Href("/x")`）も同じです。立場の全体は `DESIGN.md` §4.1 にあります。

## 名前がぶつかったとき

`using static BlazorCodeFirst.Html;` は、適合する HTML 要素の名前をすべて取り込みます。そして
単純名の解決では、自分で宣言した名前が取り込まれた名前に勝ちます。Blazor のパラメーターを
`Label`、`Data`、`Summary`、`Source` と名付けるのはありふれているので、これは起こります。

型がインデックス可能なメンバーだと、これは正しい C# になってしまいます。要素の式が、黙ってその
メンバーへのインデクサー呼び出しに変わるからです。ジェネレーターはそれを BCF3027 で名指しします。

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027
```

自分の型、名前空間、メソッドも同じように名前を奪います。報告はどれも同じで、見つけたものを名指し
します。

```csharp
public sealed class Table;                    // Table["x"]   — BCF3027、型
namespace MyApp.Article { }                   // Article["x"] — BCF3027、名前空間
private string Summary() => "";               // Summary["x"] — BCF3027、メソッド
```

C# には、どれにもエラーがあります。インデックス引数の CS1503、そして CS0119、CS0118、CS0021。
そのどれも目に入りません。本体が変換できないあいだ、コンポーネントには生成された `RenderView`
がありません。だからコンパイラーはメソッド本体を束縛する前で止まります。4つのエラーは、その
束縛の段で見つかります。

直し方はどれも同じで、要素を修飾します。

```csharp
Div[Html.Data["Heading"]]
```

## 装飾

装飾は、それが属する要素に、子より前に繋げて書きます。HTML が属性をタグの中に書くのと同じ並び
です。装飾はラッパーのノードを作らず、持ち主の要素の属性へ畳み込まれます。

```csharp
Button
    .Class("btn btn-primary")
    .Title("Save the current document")
    .OnClick(() => Save())["Save"];
```

使える装飾は `.Class`、`.Id`、`.Href`、`.Src`、`.Alt`、`.Type`、`.Title`、`.Role`、`.OnClick`。
それに、汎用の逃げ道として `.Attr(name, value)` と `.On(eventName, handler)` があります。

`.On` には `on` を含んだ属性名をそのまま渡します（`.On("onmouseenter", …)`）。こちらで前に何かを
付けたりはしません。`.Attr` や `.On` に渡す名前は、空でないコンパイル時定数である必要があります。
でなければ BCF3011 を報告します。

`.Attr` は `string?` か `bool` を取ります。`bool` は Blazor の条件付き属性です。`true` なら値の
空な属性として出力します。HTML が `disabled`、`checked`、`hidden` を立っていると読むのは、この形
です。`false` なら属性ごと出しません。いつも付く属性は、HTML と同じように値なしで書いてください。
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

`null` と `""` は、どの段階でも別の値です。フレームでも、プリレンダリングされた HTML でも、再
レンダリングでも。`""` は `title=""` になり、`null` では `title` そのものが出ません。再レンダリ
ングで値が null になったとき、Blazor は要素を差し替えるのではなく、すでに DOM にある要素から属性
を取り除きます。値を1つ取る装飾は、どれもこの意味で `null` を受け付けます。`.Class`、`.Href`、
`.Src`、`.Alt`、`.Id`、`.Type`、`.Title`、`.Role`、そして `.Attr` です。

`object` のオーバーロードは、あえて用意していません。ほかの型の値は、レンダリングの時点で、書式
化を行うスレッドのカルチャーによって文字列になります。コンポーネントが動いたときのカルチャーで
はありません。だから自分で書き出してください。そうすれば、どのカルチャーを選んだのかが目に
見えます。

```csharp
Div.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))
```

`Action` や `Func<Task>` として書いたハンドラーは、何も受け取りません。イベントを読むには、ラムダ
の引数に型を書きます。すると `.On` が型付きのオーバーロードを選びます。

```csharp
Input.Type("text").Attr("value", _name)
     .On("oninput", (ChangeEventArgs e) => _name = e.Value?.ToString() ?? "")
```

Razor と違って、引数の型はイベント名から推論されません。オーバーロードを選ぶのは、引数に書いた型
です。`ChangeEventArgs` は `Microsoft.AspNetCore.Components` にあります。`MouseEventArgs`、
`KeyboardEventArgs`、`FocusEventArgs` は `Microsoft.AspNetCore.Components.Web` にあります。
こちらも、Blazor のアプリがすでに参照している名前空間です。

型は推論されませんが、検査はされます。そのイベントが渡さない型を書くと BCF3028 を報告します。
判断のもとは、Razor が使うのと同じ `[EventHandler]` のメタデータです。おかげで
`.On("onclick", (KeyboardEventArgs e) => …)` は、ボタンが押されてから失敗するのではなく、
コンパイル時に止まります。渡される型の基底クラスは受け付けます。ハンドラーが実際に受け取れるのが
それだからです。

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // 渡される型
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // その基底。これでよい
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

そもそも `EventArgs` ですらない型（`.On("onclick", (int x) => …)`）も、同じ診断です。C# はその
呼び出しを端から拒みますが、理由を名指しするのが BCF3028 です。`[EventHandler]` の登録が無い
イベントには、照合する対応表がありません。だから登録していないカスタムイベントには何も言いません。
登録は Blazor のふつうの仕組みで、自分のプロジェクトでの登録も読みます。

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

属性を出して、イベントを受け取る。この対をひとつの装飾で書くのが
[`.Bind`](./two-way-binding.md)です。

`class` は、畳み込まれる唯一の属性です。`.Class` を2回以上繋げると、値は1つの `class` 属性に
まとまります。`.Attr("class", …)` も同じところへ合流します。ほかの属性とイベントはすべて単一の
束縛で、同じ要素に2度束縛すると BCF3010 を報告します。`style` もそのひとつです。`.Attr("style", …)`
と書き、同じ要素に2つ置くと、畳み込まれずに BCF3010 になります。

この合流は値を文字列として繋ぐので、`class` は文字列しか取らない唯一の名前です。
`.Attr("class", flag)` は BCF3023 を報告します。`.Attr("class")` も同じです。値を書かない形は
属性が立っていることを表すだけで、繋ぐ文字列を持ちません。条件付きのクラスは文字列で書き、消し
たい項には `null` を渡してください。

```csharp
Div.Class("card").Class(_selected ? "is-selected" : "")
```

`null` の項は合流から落ちます。だから class の装飾を1つだけ持つ要素は、その項が null のとき属性
ごと消えます。繋ぐ相手がもう1つあるときは、区切りだけが残ります。`_selected` が false のあいだ、
`Div.Class("card").Class(_selected ? "is-selected" : null)` は `class="card "` と出力されます。
ブラウザーはこれを `card` 1つのクラスとして読みます。

`.Bind("class", …)` は、この名前を書く3つ目の方法で、畳み込まれない唯一の方法です。だから
`.Class` と併せて書いた要素は、`class` を2つ持って出力されることになります。これは BCF3024 を
報告します。書いた順序は問いません。束縛だけを書いて、値の全体はゲッターに作らせてください。

装飾は、単一の要素を相手にしなければなりません。`If`、`ForEach`、`Fragment`、`Raw`、コンポーネント
の結果に付けると BCF3008 を報告します。それらは、付ける先の要素を開かないからです。子の後ろに
繋げて書いた場合（`Div["text"].Class("card")`）も、同じ理由で BCF3008 です。角括弧は、もう
`View` を作り終えています。

装飾は、このライブラリが宣言しているものである必要もあります。綴り違い（`Div.Clas("card")`）や、
要素を取って要素を返す自作の拡張メソッドは BCF3026 を報告します。綴り違いには C# のエラーが
ありますが、さきほどと同じ停止によって、それも目に入りません。

## 次に読むもの

- [はじめに](./getting-started.md#導入)に戻る。
- 条件分岐とリストは[制御構文](./control-flow.md)へ。
