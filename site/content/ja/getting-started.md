---
title: はじめに
order: 10
source-hash: 96eed0c4
---

BlazorCodeFirst を使うと、Blazor の UI をふつうの C# として書けます。このページ自体も Markdown
から作られていて、ビルド時に HTML へ変換したものを `Html.Raw` で流し込んでいます。

## 導入

ランタイムとソースジェネレーターをプロジェクトに追加し、コンポーネントを `BodyComponentBase`
から派生させます。

```
dotnet add package BlazorCodeFirst
```

## 最初のコンポーネント

コンポーネントは、プロパティを1つオーバーライドした `partial` クラスです。`partial` が要るのは、
ジェネレーターがそのクラスの中に描画を書き込むためです。トップレベルである必要があるのは、生成
コードが外側の型宣言の連なりを再現できないためです。

```csharp
using Microsoft.AspNetCore.Components;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

[Route("/")]
public partial class Home : BodyComponentBase
{
    protected override View Body =>
        Div[
            H1["Hello"],
            Span["Welcome to BlazorCodeFirst."]];
}
```

この式は、生成される HTML をそのまま名指しします。属性は要素に連ね、子は角括弧に入れ、裸の
文字列はテキストノードになります。

```csharp
protected override View Body =>
    Div[
        H1["Hello"],
        Span["Welcome to BlazorCodeFirst."]];
```

```html
<div>
    <h1>Hello</h1>
    <span>Welcome to BlazorCodeFirst.</span>
</div>
```

## ゲッターは1つの式へ到達する

書き方は3通りあり、どれも同じものに変換されます。

```csharp
protected override View Body => Div[H1["Hello"]];                    // これでよい
protected override View Body { get => Div[H1["Hello"]]; }            // これでもよい
protected override View Body { get { return Div[H1["Hello"]]; } }    // これでもよい
```

その `return` の手前には、ローカル変数の宣言と式文を置けます。書いた文は、生成された描画の
フレーム発行の手前へ移植されます。`ForEach` のコンテンツブロックに書いた文も、同じ場所へ移り
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

2つ目の `return` とネイティブの制御構文は、それぞれ専用のシーケンス空間を要します。だから
どちらも受け付けません（[BCF1004](./diagnostics.md#bcf1004)）。本体がどうしてもこの形にならない
なら、`RenderView` を手で書いてください。そのとき設計時の式は使われなくなり、何も報告されません。

## この API が意味を持つ場所

`Html.Div`、`.Class(...)`、`.OnClick(...)` をはじめ、要素のファクトリと装飾は、それ自体では
何もしません。`View` は空の構造体で、要素ヘルパーは何も返さず、装飾はレシーバーをそのまま返す
だけです。ジェネレーターが読むのは書かれた *構文* であって、値ではありません。読む場所も3つ
しかありません。コンポーネントの `Body`、レイアウトの `Chrome`、そして `[ViewPart]` メソッドの
本体です。

同じ API はどこからでも呼べて、その3つ以外では何の意味も持ちません。イベントハンドラーの中でも、
サービスの中でも、ヘルパーメソッドの中でも書けます。コンパイルは通り、何かを組み立てたように
見えます。そして何も起きません。出力もされず、ハンドラーも繋がりません。これが
[BCF3029](./diagnostics.md#bcf3029) で、呼び出し側から見た同じ誤りが
[BCF3030](./diagnostics.md#bcf3030) です。

## ビルドが止まるとき

コンパイラは、変換できないものを報告します。書いたものと違う描画になるコードを出すより、そこで
止めるためです。診断はすべて[リファレンス](./diagnostics.md)に項があり、最初に出会うのは次の5つ
です。

| | |
| --- | --- |
| [BCF1001](./diagnostics.md#bcf1001) | クラスが `partial` でない |
| [BCF1005](./diagnostics.md#bcf1005) | クラスが入れ子になっている |
| [BCF1004](./diagnostics.md#bcf1004) | ゲッターが1つの式へ到達しない |
| [BCF1002](./diagnostics.md#bcf1002) | 生成ファイルから見えないローカル変数を式が参照している |
| [BCF1003](./diagnostics.md#bcf1003) | ジェネレーターが読まない構文を式が使っている |

1つのクラスが2つの誤りを同時に抱えることはあります。`partial` が抜けていて、かつゲッターが
変換できない、という具合に。それでも知らされるのは一度に1つです。`partial` の検査が先に走るので、
まず BCF1001 だけが出ます。修飾子を足して、はじめて BCF1004 が現れます。

## 次に読むもの

- [カウンターの実例](/counter)で、イベント、`If`、キー付き `ForEach` の動きを見る。
- [要素と装飾](./elements-and-decorations.md)で要素の語彙を、[制御構文](./control-flow.md#if)で
  条件分岐とリストを学ぶ。
