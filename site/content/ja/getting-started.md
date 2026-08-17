---
title: はじめに
order: 10
group: start
source-hash: 06cbf180
---

BlazorCodeFirst を使うと、Blazor の UI を通常の C# として書けます。

## 導入

ランタイムとソースジェネレーターをプロジェクトに追加し、コンポーネントを `BodyComponentBase`
から派生させます。

```
dotnet add package BlazorCodeFirst --prerelease
```

公開しているバージョンには prerelease の接尾辞が付いています。`--prerelease` を外すと最新の安定版
を探しに行き、それは存在しないので何も解決しません。

## 最初のコンポーネント

コンポーネントは、プロパティを1つオーバーライドした `partial` クラスです。`partial` が必要なのは、
ジェネレーターがそのクラスの中に描画を書き込むためです。トップレベルである必要があるのは、入れ子
のクラスを生成ファイル側で開き直すには、外側の型の宣言を型引数まで含めて書き直さねばならないため
です。

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

この式は、生成される HTML をそのまま表します。属性は要素に繋げ、子ノードは角括弧に入れます。
文字列をそのまま書くとテキストノードになります。

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

書き方は3通りあり、どれも同じものへ翻訳されます。

```csharp
protected override View Body => Div[H1["Hello"]];                    // これでよい
protected override View Body { get => Div[H1["Hello"]]; }            // これでもよい
protected override View Body { get { return Div[H1["Hello"]]; } }    // これでもよい
```

その `return` の手前には、ローカル変数の宣言と式文を置けます。これらのステートメントは、生成
される `RenderView` の中で、レンダーツリーのフレームを発行する呼び出しより前にそのまま出力され
ます。`ForEach` の `content` に書いたステートメントも、同じ位置に出力されます。

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

2つ目の `return` とネイティブの制御構文は、それぞれ専用のシーケンス空間を要するため、どちらも
受け付けません（[BCF1004](./diagnostics.md#bcf1004)）。本体がどうしてもこの形にならないなら、
`RenderView` を手で書いてください。そのとき設計時の式は使われなくなり、何も報告されません。

## この API が読まれる場所

`Html.Div`、`.Class(...)`、`.OnClick(...)` をはじめ、要素のファクトリと装飾は、それ自体では
何もしません。`View` は空の構造体で、要素ヘルパーは何も返さず、装飾はレシーバーをそのまま返す
だけです。ジェネレーターが読むのは書かれた *構文* であって、値ではありません。読む場所も3つ
しかありません。コンポーネントの `Body`、レイアウトの `Chrome`、そして `[ViewPart]` メソッドの
本体です。

同じ API はどこからでも呼べますが、この3か所の外では誰も読みません。イベントハンドラー、
サービス、ヘルパーメソッドのどこに書いてもコンパイルは通ります。ただしレンダーツリーのフレームを
1つも出さないので、何も描画されず、イベントハンドラーも登録されません。これが
[BCF3029](./diagnostics.md#bcf3029) で、呼び出し側から見た同じ誤りが
[BCF3030](./diagnostics.md#bcf3030) です。

## ビルドが止まる理由

コンパイラは、`RenderTreeBuilder` の呼び出しへ翻訳できない式を報告します。書いたものと違う描画に
なるコードを出すより、そこで止めるためです。診断はすべて[リファレンス](./diagnostics.md)に項が
あり、よく出るのは次の5つです。

| | |
| --- | --- |
| [BCF1001](./diagnostics.md#bcf1001) | クラスが `partial` でない |
| [BCF1005](./diagnostics.md#bcf1005) | クラスが入れ子になっている |
| [BCF1004](./diagnostics.md#bcf1004) | ゲッターが1つの式へ到達しない |
| [BCF1002](./diagnostics.md#bcf1002) | 生成ファイルから見えないローカル変数を式が参照している |
| [BCF1003](./diagnostics.md#bcf1003) | ジェネレーターが読まない構文を式が使っている |

1つのクラスが `partial` の欠落とゲッターの問題を同時に持つことはありますが、報告されるのは一度
に1つです。`partial` の検査が先に実行されるので、まず BCF1001 だけが出ます。修飾子を足すと、次に
BCF1004 が出ます。

## 次に読むもの

- [カウンターの実例](/counter)で、イベント、`If`、キー付き `ForEach` の動きを見る。
- [要素と装飾](./elements-and-decorations.md)で書ける要素の一覧を、[制御構文](./control-flow.md#if)で
  条件分岐とリストを学ぶ。
