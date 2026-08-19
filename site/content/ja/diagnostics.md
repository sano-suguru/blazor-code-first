---
title: 診断
description: このコンパイラが報告する全診断と、それぞれの意味、代わりに書くべきコード。ビルドが出力した ID でページ内を検索する。
order: 100
group: reference
source-hash: 2fb99f58
---

このコンパイラが報告する診断のすべてと、その意味と、代わりに書くものです。

ビルドは ID を表示します。このページをその ID で検索してください。

## クラスとそのゲッター

### BCF1001

Error. `Body` または `Chrome` を宣言するクラスが `partial` ではないため、生成される `RenderView`
を書き込む先がありません。

```csharp
public class Home : BodyComponentBase          // BCF1001
public partial class Home : BodyComponentBase  // 代わりにこう書く
```

修飾子が必要なのは、オーバーライドを宣言するクラスだけです。BlazorCodeFirst の基底を継承している
だけのクラスには何も生成されません。間に挟まる抽象基底、基底が既にオーバーライドを宣言している
末端のクラス、再抽象化したクラスは、いずれも対象外です。

この診断がなければ、修飾子の欠落は抽象メンバー `RenderView` に対する CS0534 としてしか現れま
せん。CS0534 が示すのは足りないメンバーで、修飾子ではありません。

### BCF1002

Error. 生成ファイルから見えないものを式が参照しているか、`[ViewPart]` メソッドが静的展開の契約
を満たしていません。

```csharp
protected override View Body =>
    Div.Attr("data-found", _rows.TryGetValue(_key, out var row))
       .Attr("title", row.Name);                               // BCF1002: row はそこに存在できない
```

ジェネレーターは要素の各部を書かれた順に出力しないので、式のある部分で宣言したローカル変数は、
別の部分の参照へ届きません。`return` の手前の文で宣言してください。

宣言を運べる位置は2つあります。どちらも生成コードでは、それを読む側を包むヘッダーになるからです。
`If` の条件は両方の分岐を包み、`ForEach` のソースはコンテンツとキーを包みます。

BCF1003 との違いはここにあります。BCF1003 は、式を順序付けできなかったという意味です。BCF1002
は、順序付けはできたものの、生成ファイルから見えないものを参照したという意味です。

`[ViewPart]` の本体にも、ゲッターについて [BCF1004](#bcf1004) が述べる予約名の規則が
当てはまります。

### BCF1003

Error. 式はモデルの段階まで届きましたが、ジェネレーターが解析しない構文を使っているため翻訳
できませんでした。

```csharp
Div[_kids]                    // BCF1003: 子のリストを丸ごと渡している
Div[new View[] { … }]         // BCF1003: 明示的な配列
Div[[..items]]                // BCF1003: スプレッド
```

繰り返しには [`ForEach`](./control-flow.md) を使ってください。ジェネレーターが読むものは
[要素と装飾](./elements-and-decorations.md)と[制御構文](./control-flow.md)に挙げてあります。
要素ヘルパー、`Component<T>()`、`Fragment`、`Raw`、その場に書いた式のラムダ、そして `[ViewPart]`
を付けたメソッドの呼び出しです。

自分の `View` を返すメソッドに `[ViewPart]` を付ければ、そのマークアップを呼び出し側へ戻さずに
分けたまま保てます。

`If` の分岐と、スプライスされた射影(`.. source.Select(item => …)`)は、BCF1004 のゲッターと
同じように、本体を作者自身の名前のまま移植します。そのため [BCF1004](#bcf1004) が述べる予約名
の規則が、ここにも届きます。

### BCF1004

Error. ゲッターが、返り値の式1つに収まっていません。

```csharp
protected override View Body => Div[H1["Hello"]];              // これでよい
protected override View Body { get => Div[H1["Hello"]]; }      // これでもよい
protected override View Body { get { return Div[H1["Hello"]]; } }   // これでもよい

protected override View Body { get; } = default;               // BCF1004
```

その `return` の手前には、ローカル変数の宣言と式文を置けます。これらのステートメントは、生成
される `RenderView` の中で、レンダーツリーのフレームを発行する呼び出しより前にそのまま出力され
ます。

```csharp
protected override View Body
{
    get
    {
        var greeting = $"Hello, {_name}";
        return Div[H1[greeting]];
    }
}
```

2つ目の `return` と C# 本来の制御構文は、それぞれ専用のシーケンス空間を必要とします。自動プロパ
ティには、そもそもゲッターの本体がありません。`__builder` という名前のローカル、または `__bcf_`
で始まるローカルも、ここには宣言できません。この2つの綴りは、移植したローカルが着地するどの位置
でもジェネレーターが予約しています。名前を変えてください。本体がどうしてもこの形にならないなら、
`RenderView` を手で書いてください。そのとき設計時の式は使われなくなり、何も報告されません。

BCF1004 が報告するのは宣言そのものです。1つのクラスが `partial` の欠落とゲッターの問題を同時に
持つことはあり、報告されるのは一度に1つです。`partial` の検査が先に実行されるので、まず BCF1001
だけが出ます。

### BCF1005

Error. 入れ子のクラスが設計時の式を宣言しています。

```csharp
public partial class Page
{
    public partial class Row : BodyComponentBase   // BCF1005
    {
    }
}
```

コンポーネントをトップレベルの型へ移してください。入れ子のクラスを生成ファイル側で開き直すには、
外側の型の宣言を型引数まで含めて書き直す必要があり、ジェネレーターはそれを行いません。この診断が
なければ、入れ子であることは CS0534 としてしか現れません。

## この API が読まれる場所

### BCF2001

Info. その呼び出しは静的に展開できないので、この領域は実行時のフラグメントを通して描画され、
静的な差分の最適化を失います。

ジェネレーターが展開するのは、読めるものだけです。設計時の API と、このコンパイルの中で宣言
された `[ViewPart]` メソッドです。読めない呼び出しも誤りではなく、描画も正しく行われます。その
領域のフレームが、静的なテンプレートと差分を取る代わりに、作り直されるというだけです。

参照プロジェクトや NuGet パッケージの `[ViewPart]` がこれに該当します。定義は現コンパイルの構文から
集められ、IL は本体の構文を持たないからです。アセンブリを越えた再利用はコンポーネントで行います。

### BCF3029

Error. 誰も読まない場所に設計時の構文が書かれているので、何も描画されず、ハンドラーも繋がりま
せん。

```csharp
private void OnSomething()
{
    // BCF3029: 何も描画されず、DoThing も呼ばれない
    var card = Div.Class("card").OnClick(DoThing)[Span["hello"]];
}
```

`Html.Div`、`.Class(...)`、`.OnClick(...)` をはじめ、要素のファクトリと装飾は、それ自体では何も
しません。`View` は空の構造体で、要素ヘルパーは何も返さず、装飾はレシーバーをそのまま返すだけ
です。ジェネレーターが読むのは書かれた *構文* であって、値ではありません。読む場所も3つしか
ありません。コンポーネントの `Body`、レイアウトの `Chrome`、そして `[ViewPart]` メソッドの本体
です。

同じ API はどこからでも呼べますが、この3か所の外では誰も読みません。コンパイルは通りますが、
レンダーツリーのフレームを1つも出さないので、何も描画されず、イベントハンドラーも登録されません。

設計時型の値をフィールドやプロパティに取っておくのは報告しません。報告するのは、ローカル変数、
破棄、引数の場合だけです。

### BCF3030

Error. 呼び出し先は設計時の API から `View` を組み立てているのに `[ViewPart]` を持たないので、
この呼び出しは何も描画しません。

```csharp
private static View Card(string title) => Div.Class("card")[H2[title]];

protected override View Body => Div[Card("Hello")];   // BCF3030
```

そのメソッドが静的なら `[ViewPart]` を付けてください。そうでなければコンポーネントにします。
属性がなければ、メソッドの結果はフラグメントを持たず、呼び出しはフレームを1つも出しません。

これは BCF3029 を呼び出しの反対側から見たものです。BCF3029 は、誰も読まない場所に書かれた設計時
の構文を報告します。こちらは、設計時の構文を書いたのに誰にも読まれなかったメソッドへの呼び出しを
報告します。

### BCF3001

Error. 設計時の式の中で状態を書き換えています。

```csharp
protected override View Body
{
    get
    {
        _renderCount++;               // BCF3001
        return Div[Span[$"{_renderCount}"]];
    }
}
```

```csharp
private void OnShown() => _renderCount++;                      // 代わりにこう書く
protected override View Body => Div[Span[$"{_renderCount}"]];
```

ゲッターは状態を UI へ映す射影であり、実行ではなく翻訳されます。書き換えはイベントハンドラーへ
移してください。

`return` の手前の文も翻訳されるので、そこに書いた書き換えも BCF3001 です。

### BCF3015

Error. 設計時の値の式にある型名が解決できず、その名前は元ファイルの `using` や名前空間があって
初めて解決する書き方です。

値は `using` を一切持たない生成ファイルへ写されます。解決済みの型名は `global::` 修飾へ書き換え
られますが、解決できない文脈依存の名前は安全に正規化できません。

名前を直すか、完全修飾するか、ソース生成された型を参照プロジェクトへ移すか、手書きの C# の型に
置き換えてください。既に `global::` から始まっている参照はそのまま残し、通常の C# の解決に任せ
ます。ジェネリックの型引数は、それぞれ独立に検査します。

## 要素

### BCF3009

Error. `Element` のタグが、タグ名の形をしたコンパイル時定数ではありません。

```csharp
private const string Widget = "my-widget";

Element(Widget)                  // これでよい
Element(_kind + "-widget")       // BCF3009: 定数ではない
Element("")                      // BCF3009: 空
Element("my widget")             // BCF3009: タグ名の形ではない
```

タグ名とは、ASCII の英字で始まり、以降は ASCII の英数字と `-`、`_`、`.` が続くものです。

定数であることを求めるのは、要素を宣言的に保つためです。計算されたタグは注入の危険でも順序付けの
問題でもありませんが、書いた場所で要素のタグ名が読み取れなくなります。

タグ名の形を求める理由は違います。ここが破れると、翻訳そのものが壊れます。どの要素にも使えない
タグは、二通りに描かれます。プリレンダリングはマークアップに書き、HTML パーサーがそれを解釈し直します。
対話的レンダリングは `createElement` に渡し、それが拒んでサーキットが終了します。

### BCF3016

Error. 空要素に子を書いています。

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016、同じ規則
Img.Src("/logo.png").Alt("Logo") // 代わりにこう書く
```

空要素は13個あります。`area`、`base`、`br`、`col`、`embed`、`hr`、`img`、`input`、`link`、`meta`、
`source`、`track`、`wbr` です。

子は HTML を往復すると失われます。プリレンダリングはパーサーが受け付けない終了タグを書き出し、
パーサーは子を要素の外へ移して兄弟にします。孤立した `</br>` は開始タグとして読み直されるので、
`Br["x"]` は `<br>` 2つになります。

対話的レンダリングにはパーサーが挟まらず、同じ子を要素の中に置きます。1つの式から2つの DOM が
でき、ハイドレーションでページの形が変わります。

空要素は装飾で設定し、内容はその隣に置いてください。

ヘルパーと、空要素のタグを渡した `Element` の両方を検査します。カスタム要素と未知のタグは検査
しません。内容モデルを読み取れる標準がないからです。

この API が HTML について検査するのは、ここまでです。この線引きは意図したものです。BCF3016 は
要素のタグだけから決定できます。ある子がある親の中に置けるかは決定できないので、
`Table[Div["x"]]` も、要素が定義しない属性（`Div.Href("/x")`）も受け付けます。

### BCF3027

Error. 要素ヘルパーが使うはずの単純名を、自分の宣言が奪っています。

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027、メンバー

public sealed class Table;                    // Table["x"]   — BCF3027、型
namespace MyApp.Article { }                   // Article["x"] — BCF3027、名前空間
private string Summary() => "";               // Summary["x"] — BCF3027、メソッド

Div[Html.Data["Heading"]]                     // 代わりにこう書く
```

`using static BlazorCodeFirst.Html;` は準拠する要素名をすべて取り込みますが、自分の宣言のほうが
単純名の解決に勝ちます。`Label`、`Data`、`Summary`、`Source` という Blazor のパラメーター名は
ありふれているので、この衝突は実際に起きます。

C# にはそれぞれ専用のエラーがあります。添字の引数に対する CS1503、CS0119、CS0118、CS0021 です。
そのどれも報告されません。本体が翻訳されない限りコンポーネントには生成された `RenderView` がなく、
コンパイラはメソッド本体を解決する前に止まります。4つのエラーは、どれもその解決の段階ではじめて
見つかるものです。

## 装飾

### BCF3008

Error. 要素を開かないものに装飾を付けています。

```csharp
If(_open, () => Div["x"]).Class("card")   // BCF3008
Div["text"].Class("card")                 // BCF3008: 角括弧が既に View を作っている
Div.Class("card")["text"]                 // 代わりにこう書く
```

装飾は所有する要素の属性へ畳まれるので、付く先の要素が必要です。`If`、`ForEach`、`Fragment`、
`Raw`、`[ViewPart]` やコンポーネントの結果は、どれも要素を開きません。

### BCF3026

Error. 装飾の位置に書かれた名前を、このライブラリは宣言していません。

```csharp
Div.Clas("card")     // BCF3026
Div.Class("card")    // 代わりにこう書く
```

綴り間違いも、要素を取って要素を返す自作の拡張メソッドも、これに該当します。綴り間違いには C# の
エラーがありますが、同じ宣言段階の停止によって報告されません。

### BCF3010

Error. 1つの要素で、同じ属性またはイベントを2回以上バインドしています。

```csharp
Input.Type("text").Attr("value", _a).Attr("value", _b)   // BCF3010
Input.Type("text").Attr("value", _b)                     // 代わりにこう書く
Div.Class("card").Class("is-open")                       // これでよい: class は畳まれる
```

属性チャネルで2回バインドすると、後の書き込みが勝ち、先のものは捨てられます。1つの名前を属性チャネル
とイベントチャネルで1回ずつバインドすると、両方が残ります。インラインのハンドラーと C# の
ハンドラーが、イベントごとに両方実行されます。どちらも書いたとおりではありません。

例外は `class` だけです。`.Class` と `.Attr("class", …)` は空白で連結された1つの属性へ畳まれます。
`style` は例外ではないので、1つの要素に2つ書けば BCF3010 です。

### BCF3011

Error. `.Attr` の名前または `.On` のイベント名が、空でないコンパイル時定数の文字列ではありません。

```csharp
Div.Attr(_name, "x")          // BCF3011
Div.Attr("data-kind", "x")    // 代わりにこう書く
```

名前はリテラルとして出力されます。定数であることは、class の畳み込みと二重バインドの検出の前提でも
あります。

### BCF3023

Error. `class` に書いた装飾が、class チャネルがテキストとして連結できない値を持っています。

```csharp
Div.Attr("class", _selected)                     // BCF3023
Div.Attr("class")                                // BCF3023: 存在の表明にテキストはない
Div.Class(_selected ? "is-selected" : null)      // 代わりにこう書く
```

class チャネルは、書かれた装飾を1つの値へテキストとして連結します。そのため `class` が取るのは
文字列だけです。`.Attr` の `bool` オーバーロードは Blazor の条件付き属性の形で、引数なしの
`.Attr(name)` は存在の表明です。どちらもテキストではありません。

さもなければ、値は二通りに翻訳されます。class の装飾が1つだけならチャネルは値をそのまま出すので、
`true` は `class=""` になり class の一覧を空にします。2つ以上ならチャネルは `+` で連結するので、
同じ `true` が `class="a True"` になります。同じ書き方が、チェーンの別の場所に書かれた個数によって
二通りの意味を持つ、というのが問題です。

### BCF3024

Error. 1つの要素が、class チャネルの装飾と `class` への `.Bind` の両方を持っています。

```csharp
Div.Class("card").Bind("class", "onchange", () => _classes)   // BCF3024
```

`.Class` と `.Attr("class", …)` は1つの属性へ畳まれます。同じ名前への `.Bind` はその畳み込みに
加わらず、自分のフレームを出すので、要素は `class` を2つ持って出力されます。

どちらが残るかは一通りに決まりません。プリレンダリングされたマークアップは HTML パーサーが解決
して先勝ちになり、対話的レンダリングは DOM へ適用するので後勝ちになります。

class の値は1か所から与えてください。バインドのゲッターにすべてを持たせるか、バインドをやめて
装飾だけにします。

### BCF3033

Error. 属性ではない同じ装飾を、1つのノードに2回書いています。

```csharp
Div.Key(row.Id).Key(row.Slug)["x"]   // BCF3033
Div.Key(row.Id)["x"]                 // 代わりにこう書く
```

`.Key` と同種の装飾は、値を1つだけ持つチャネルを占めます。3つとも壊れ方が違い、どれも目に見える
形では壊れません。

- `SetKey` は開いているフレームへ書き込むので、2回目が1回目を上書きします。
- `AddComponentRenderMode` は追記され、レンダラーは最初に見つけたフレームを読むので、そこでは
  2つ目が無視されます。
- 参照の捕捉も追記され、両方の動作が実行されます。

装飾は1回だけ書き、そのノードが持つべき値を渡してください。候補が2つあるということは、同一性が
まだ決まっていないということです。決められる場所はソースだけです。

### BCF3034

Error. コンポーネント自身の宣言がレンダーモードを固定しているので、呼び出し側からは設定
できません。

```csharp
Component<Counter>().RenderMode(RenderMode.InteractiveWebAssembly)   // Counter が宣言していれば BCF3034
```

フレームワークはこの組み合わせを拒みます。`RenderModeAttribute` を持つ型が呼び出し側からもモード
を受け取ると、`ComponentFactory` が例外を投げます。呼び出し側で指定する形は、自分ではモードを
宣言しないコンポーネントのためにあります。同じコンポーネントを、あるページからは対話的に、別の
ページからは静的に描く場合です。

呼び出し側の `.RenderMode` を外し、コンポーネント自身の属性に任せてください。呼び出し側ごとに
モードを変える必要が本当にあるなら、コンポーネントから属性を外します。その場合は、すべての
呼び出し側がモードを書くことになります。

### BCF3039

Error. `.FormName` の引数がリテラルの空文字列または `null` です。

```csharp
Form.FormName("")["submit"]      // BCF3039
Form.FormName("save")["submit"]  // 代わりにこう書く
```

`.FormName` は `AddNamedEvent("onsubmit", name)` へ下がり、フレームワークはどちらの形も実行時に
例外を投げます。空文字列なら `ArgumentException`、`null` なら `ArgumentNullException` です。実行
時の式はコンパイル時定数である必要はありません。ここで拒むのは、常に例外を投げるとあらかじめ
分かるリテラルだけです。

### BCF3040

Error. `.FormName` がタグ `form` ではない要素に書かれています。

```csharp
Div.FormName("save")["submit"]    // BCF3040
Form.FormName("save")["submit"]   // 代わりにこう書く
```

`.FormName` は `AddNamedEvent("onsubmit", …)` へ下がり、`onsubmit` はブラウザネイティブの
イベントで `<form>` 要素でしか発火しません。他のタグへの登録は届きません。

## イベント

### BCF3019

Error. イベント名が `on` で始まっていません。

```csharp
Input.On("input", (ChangeEventArgs e) => …)     // BCF3019
Input.On("oninput", (ChangeEventArgs e) => …)   // 代わりにこう書く
```

Blazor のイベント属性名は必ず `on` で始まり、接頭辞が補われることはありません。`on` のない名前は
通常の属性として `AddAttribute` に届くので、ハンドラーは呼ばれず、実行時にも何も報告されません。

`.Bind` では、この検査がもう1つの役割を持ちます。属性名とイベント名は隣り合う文字列引数で、
入れ替えてもコンパイルは通ります。入れ替えを止めるのがこの検査です。

### BCF3028

Error. ハンドラーの引数型が、指定したイベントの渡す型ではありません。

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // 渡される型
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // その基底: これでよい
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

Blazor はイベントの引数オブジェクトをハンドラーの引数型へキャストして渡すので、渡される型の基底は
受け取れて、兄弟の型は受け取れません。`EventArgs` ですらない型も同じ診断です。

対応表はフレームワークが提供する `[EventHandler]` のメタデータと、ビルド中のコンパイルにある登録
から読みます。登録のないイベントには対応表がないので検査しません。

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

### BCF3035

Error. イベント修飾子の手前に、その要素のイベントがありません。

```csharp
Form.PreventDefault().On("onsubmit", () => Save())   // BCF3035
Form.On("onsubmit", () => Save()).PreventDefault()   // 代わりにこう書く
```

`.PreventDefault` と `.StopPropagation` は、手前に書いたイベントへ付きます。チェーンが示す読み方は
それだけです。装飾自身はイベント名を持ちません。

### BCF3036

Error. 1つのイベントに、同じイベント修飾子を2回書いています。

```csharp
Form.On("onsubmit", () => Save()).PreventDefault().PreventDefault()   // BCF3036
Form.On("onsubmit", () => Save()).PreventDefault()                    // 代わりにこう書く
```

2つのうち一方は、モデルがどちらを採っても何も変えません。どちらが無駄になるかは、呼び出し側から
は分かりません。

修飾子は1回だけ書いてください。値ではなくフラグなので、2つ目には1つ目に足すものがありません。

### BCF3038

Error. そのイベント自身の `[EventHandler]` 登録が、その修飾子を無効にしています。

Blazor は修飾子をイベントごとに制御し、登録が無効にした属性をレンダラーは無視します。修飾子を
外してください。イベント自体は正しく、そのまま残ります。

## 制御構文

### BCF3002

Warning. `ForEach` のキーセレクターが自分の要素に触れていないので、要素を見分けられません。

```csharp
ForEach(rows, key: r => 0, content: r => Li[r.Name])          // BCF3002
ForEach(rows, key: r => r.Id, content: r => Li[r.Name])       // 代わりにこう書く
```

要素から作られたキーがあってはじめて、Blazor は挿入・削除・並び替えを越えて行ごとの状態を保てます。
定数、外側のインデックス、別のリストの要素は、全体の再描画を招きます。

検査はあえて控えめです。要素から作ってはいるものの実際には位置に近い、というキーは検出しません。

### BCF3003

Error. `ForEach` の内容のルートは、単一の要素かコンポーネントである必要があります。そうでないと、
キーの付く先がありません。

```csharp
ForEach(rows, key: r => r.Id, content: r => If(r.Visible, () => Li[r.Name]))   // BCF3003
ForEach(rows, key: r => r.Id, content: r => Li[If(r.Visible, () => Span[r.Name])])   // 代わりにこう書く
```

キーは内容のルートのフレームに付き、`SetKey` は現在開いている要素かコンポーネントのフレームに
付きます。単独の `If`、入れ子の `ForEach`、テキストだけ、`Fragment`、`Raw`、外から渡された
`RenderFragment` は、いずれもキーの付く単一のフレームを開きません。

内容を要素で包んでください。`key: null` でキーを使わない `ForEach` は `SetKey` を出さないので、
それらのルートも置けます。

### BCF3004

Error. `ForEach` のキーか内容が、ジェネレーターの順序付けできない形です。

キーの本体は `SetKey` の呼び出しへそのまま写されるので、式である必要があります。内容には静的な
シーケンス空間が1つ与えられ、それを毎回の繰り返しが使い回します。2つ目の `return` や C# 本来の
制御文は、それぞれ自分用の複製を必要とします。

内容が受け付けるのは、式のラムダ、末尾に `return` を1つ持つブロック、引数1つの `View` を返す
メソッドグループです。

キーの本体と内容の本体にも、ゲッターについて [BCF1004](#bcf1004) が述べる予約名の規則が
当てはまります。

### BCF3032

Error. `ForEach` がキーを付ける内容のルートが、自分でも `.Key` を書いています。

```csharp
ForEach(rows, key: r => r.Id, content: r => Li.Key(r.Id)[r.Name])   // BCF3032
```

1つのフレームに `SetKey` が2回届き、後のほうが勝ちます。そのためどちらのキーが有効かは、呼び出し
サイトではなくフレームの発行順で決まります。ルートに付けるか、ループに付けるか、どちらかにして
ください。

## コンポーネント

### BCF3005

Error. パラメーターのセレクターが、自分のラムダ引数に対する素直なプロパティ選択ではありません。

```csharp
Component<Card>().Param(c => (string)c.Label, "x")   // BCF3005
Component<Card>().Param(c => c.Label, "x")           // 代わりにこう書く
```

ジェネレーターはセレクターに書かれた名前をそのまま読むので、キャスト、メソッド呼び出し、
null 条件アクセス、捕捉した変数のメンバーには読む名前がありません。`.Param`、`.Template`、
`.Bind` はどれも同じセレクターを取ります。

### BCF3006

Error. 選んだプロパティが、設定できる `[Parameter]` ではありません。

```csharp
public string Label { get; set; }              // BCF3006: [Parameter] が無い
[Parameter] public string Label { get; }       // BCF3006: セッターが無い

[Parameter] public string Label { get; set; }  // 代わりにこう書く
```

バインドできるのは、`[Parameter]` が付いていてアクセス可能なセッターを持つプロパティだけです。
それ以外を設定すると、Blazor がパラメーターを適用する時点で例外がスローされます。

プロパティに `[Parameter]` を付け、アクセス可能なセッターを与えてください。呼び出し側から受け
取らない値は、パラメーターではなくフィールドに置きます。

### BCF3007

Error. 1つのチェーンが、同じパラメーターを2回以上バインドしています。

```csharp
Component<Card>().Param(c => c.Label, "a").Param(c => c.Label, "b")   // BCF3007
Component<Card>().Param(c => c.Label, "b")                            // 代わりにこう書く
```

数えるのはすべてのチャネルです。`.Param`、`.Template`、`.Bind`、そして角括弧に書いた子の内容。
Blazor は最後の書き込みを適用するので、先の値は捨てられます。

パラメーターは1回だけバインドし、コンポーネントに届いてほしい値を渡してください。状態で変わる
値は、渡す式の中で決めます。2つ目の `.Param` で決めるものではありません。

### BCF3012

Error. ジェネレーターの実行時点で型引数が解決できませんでした。

よくある原因は、同じプロジェクトで宣言された `.razor` コンポーネントです。Razor コンパイラ自身が
ソースジェネレーターであり、ソースジェネレーターは互いの出力を観測できません。そのため最終的な
コンパイルに存在していても、ここでは解決できません。

参照プロジェクトへ移すか、手書きの C# コンポーネントにするか、名前を直してください。参照プロ
ジェクトや NuGet パッケージの同じコンポーネントは通常どおり解決します。

綴り間違い、アクセスできない型、あいまいな名前、`using` の不足もこれに該当します。その場合は同じ位置
に C# の解決エラーも出ます。

### BCF3013

Error. 子の内容を受け取れないコンポーネントに、角括弧で内容を書いています。

```csharp
Component<Panel>()["body"]                                  // Panel に ChildContent がなければ BCF3013
Component<Panel>().Param(c => c.Content, Span["body"])      // 代わりにこう書く
```

角括弧は `ChildContent` という名前のパラメーターにバインドされます。Razor が入れ子の内容を割り当てる
先と同じです。そのパラメーターは、設定できる `[Parameter]` である必要があります。型は
`RenderFragment` か `RenderFragment<TContext>` に限ります。ジェネリックのほうは、コンテキストを
捨てて子を受け取ります。

### BCF3014

Error. 実体のない設計時の値を、ジェネリックの `Param` に渡しています。

```csharp
Component<Card>().Param(c => c.Body, Div["x"])              // BCF3014
Component<Card>().Param(c => c.Body, () => Div["x"])        // 代わりにこう書く
```

`View` と `ComponentView<T>` は、ジェネレーターが読む空の目印であって、実行時の値ではありません。
ジェネリックの `Param` は値の式をそのまま出すので、これをバインドすると目印が代入されます。`object`
型のパラメーターは例外を出さずに受け取って誤った出力を描き、型の付いたパラメーターは Blazor が
パラメーターを適用するときに無効なキャストを投げます。

### BCF3022

Error. コンテキストを取る `.Template` の内容が、その場に書いた式のラムダではありません。

```csharp
Component<Grid<Row>>().Template(c => c.RowTemplate, RenderRow)          // BCF3022
Component<Grid<Row>>().Template(c => c.RowTemplate, row => Td[row.Name]) // 代わりにこう書く
```

メソッドグループ、匿名メソッド、本体が文のラムダは、いずれも内容を呼び出しの内側に隠します。
順序付ける式も、生成したコンテキスト変数を差し替える引数のシンボルも残りません。

内容にも、ゲッターについて [BCF1004](#bcf1004) が述べる予約名の規則が当てはまります。

### BCF3025

Error. 内容を取る `[ViewPart]` の本体の外に `Slot` があるか、ちょうど1回以外の回数で書かれて
います。

```csharp
[ViewPart]
public static SlotView Panel(View heading) =>
    Section[heading, Div.Class("body")[Slot]];   // これでよい

protected override View Body => Div[Slot];       // BCF3025: Body は角括弧を受け取らない
```

`Slot` は、呼び出し側が角括弧で渡した内容をパーツが置く位置を示します。そのため置く内容がない
場所では意味を持ちません。コンポーネントの `Body` や `Chrome` は角括弧を受け取らず、`View` を
返すパーツは角括弧なしで呼ばれます。

内容を取るパーツは、返り値の型に `SlotView` を宣言し、`Slot` をちょうど1回書きます。2回書けば
呼び出し側の内容が2回出力され、1回も書かなければ、渡すことを義務づけた内容を捨てることになります。

### BCF3042

Error. コンポーネント呼び出しに書いた `.Class`/`.Attr` の名前が、大文字小文字を区別せずに、その
コンポーネントが宣言するパラメーターと一致しています。

```csharp
Component<Card>().Attr("label", "hi")           // BCF3042: Card は [Parameter] Label を宣言
Component<Card>().Param(c => c.Label, "hi")     // 代わりにこう書く
```

Blazor は属性名をコンポーネントの宣言したパラメーターへ、大文字小文字を区別せずに突き合わせます。
そのため `"label"` は、`AdditionalAttributes` へ届く代わりに実行時に `Label` を書き換えてしまい、
`.Param` の型チェックを素通りしたまま、何も起きなかったかのように見えます。

パラメーターは `.Param` でバインドしてください。宣言した型でチェックされます。

## 双方向バインディング

### BCF3017

Error. `.Bind` のゲッターが、その場に書いた式本体のラムダではありません。

```csharp
Input.Bind("value", "oninput", GetName)          // BCF3017
Input.Bind("value", "oninput", () => _name)      // 代わりにこう書く
```

ゲッターの本体は2か所へ写されます。バインドされる属性の値として1回、バインダーの現在値として1回
です。そのため式として取り出せる必要があります。本体が文のラムダとメソッドグループは、どちらも
それを呼び出しの内側に隠します。

セッターの引数にこの制限はありません。`EventCallback` へ丸ごと渡され、分解されないからです。

### BCF3018

Error. ゲッターのみの `.Bind` で、ゲッターの本体に代入できないため、セッターを導けません。

```csharp
Input.Bind("value", "oninput", () => Name.Trim())                          // BCF3018
Input.Bind("value", "oninput", () => Name, v => Name = v.Trim())           // 代わりにこう書く
```

ゲッターのみの形は、ゲッターの本体を代入の左辺に置いてセッターを導きます。そのためその本体は、
フィールド、設定できるプロパティ、セッターを持つインデクサーへの要素アクセスのいずれかである
必要があります。

セッターがあることと、導いた代入がそれを呼べることは別の問いです。導いたセッターはラムダなので、
`init` アクセサには届きません。C# が `init` への代入を許すのは、オブジェクト初期化子、
コンストラクター、別の `init` アクセサの中だけです。プロパティ自身より狭く宣言したセッターにも
届きません。こちらは代入がどこへ出力されるかで決まります。コンポーネント自身が宣言した
`{ get; private set; }` は受け付けます。生成される `RenderView` が同じクラスの partial へ出力
されるからです。別の型が宣言した同じ形は拒否します。

ローカル変数、引数、`ForEach` の反復変数は、C# なら代入できても拒否します。設計時の式はプロパティ
のゲッターなので、それらは描画ごとに消え、書き戻しは次の描画まで残らないからです。反復変数の
メンバーは受け付けます。元のリストの要素へ書き通るからです。

### BCF3020

Error. コンポーネントに対応する変更コールバックがないので、双方向バインディングに書き戻す先が
ありません。

```csharp
Component<Field>().Bind(c => c.Value, () => _query)   // ValueChanged がなければ BCF3020
Component<Field>().Param(c => c.Value, _query)        // 代わりに片方向でこう書く
```

コンポーネントのバインドは、パラメーター名を書かせるのではなく導きます。要素側とは逆です。それ
が成り立つのは、導出を検査できるからです。コンポーネントの型は分かっているので、ジェネレーター
は導いた `{名前}Changed` を引き、無いか型が違えばこれを報告します。

要素のバインドにはこの検査がありません。そのため名前を2つとも書かせます。

### BCF3031

Error. `.Bind` が、フレームワークが書式を取る変換器を宣言していない型に書式を書いています。

```csharp
Input.Bind("value", "oninput", () => _count, format: "N0")   // BCF3031
```

書式を取るオーバーロードを宣言しているのは、`BindConverter.FormatValue` と `CreateBinder` の
2つです。対象の型は `DateTime`、`DateTimeOffset`、`DateOnly`、`TimeOnly` と、それらの null 許容
形だけです。それ以外に書式を書くと、どのオーバーロードにも解決しない呼び出しが生成ファイルに
残ります。その C# のエラーは、自分で書いたソースではなく生成コードの中で起きます。

書式を外すか、ゲッターで整形してセッターで解析してください。受け付ける集合はフレームワーク自身の
メタデータから読んでおり、このコンパイラが列挙しているのではありません。カルチャーは問題になりま
せん。この API がバインドするどの型も、カルチャーを持てます。

## スコープ付きCSS

### BCF3041

Error. `Foo.cs.css` に対応する `Foo.cs` がありません。`Foo.cs` はコンポーネントも `[ViewPart]`
メソッドも宣言していません。

```
Counter.cs.css   // 何もスコープしない: このプロジェクトに Counter.cs がない   // BCF3041
```

`.cs.css` ファイルのスコープは、`.cs` ファイルとの名前の一致だけで決まります。Razor の
`ScopedCssInput` のような明示的な対応付けの手段はありません。対応の取れないファイルは常に
誤りです。多くはファイル名の打ち間違いなので、黙って捨てずに報告します。

`.cs.css` ファイルの名前を、スコープしたいコンポーネント(または `[ViewPart]` メソッドを宣言する
ファイル)に合わせてください。そのファイルがまだ無ければ追加してください。

## 次に読むもの

[はじめに](./getting-started.md)へ戻る、あるいは[要素と装飾](./elements-and-decorations.md)で
書ける要素の一覧を読む。
