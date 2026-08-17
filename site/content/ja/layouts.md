---
title: レイアウト
order: 70
group: write
source-hash: 1da59476
---

レイアウトは、ルーティングされたページを共通の外枠で包みます。ヘッダー、ナビゲーション、
フッターといったものです。BlazorCodeFirst のレイアウトは、コンポーネントと同じように書きます。
`ChromeLayoutBase` から派生し、設計時の UI の式を宣言し、レンダリングはソースジェネレーターに
作らせます。

## Chrome と Body

`ChromeLayoutBase` は Blazor の `LayoutComponentBase` から派生しています。そのため、ルーティング
されたページを受け取る `Body` パラメーターを既に備えています。レイアウト自身が描く外枠は、
`Chrome` という別のプロパティをオーバーライドして書きます。

```csharp
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

public partial class MainLayout : ChromeLayoutBase
{
    protected override View Chrome =>
        Div.Class("shell")[
            Header[H1["My App"]],
            Main.Class("content")[Body],
            Footer["© 2026"]];
}
```

ここの `Main[Body]` は、Razor の `<main>@Body</main>` そのものです。ルーティングされたページを、
要素の内容として置いています。下の出力は、ページが入る位置にプレースホルダーを置いたものです。

```csharp
protected override View Chrome =>
    Div.Class("shell")[
        Header[H1["My App"]],
        Main.Class("content")[Body],
        Footer["© 2026"]];
```

```html
<div class="shell">
    <header><h1>My App</h1></header>
    <main class="content">the routed page</main>
    <footer>© 2026</footer>
</div>
```

## Body ではなく Chrome である理由

Blazor は、レイアウトが包む内容を `Body` という名前のパラメーターで公開するよう求めます。名前は
`Body` ちょうどでなければなりません。一方 C# は、1つの型に同じ名前のメンバーを2つ宣言できま
せん。そこで `Body` は Razor での意味、つまり包まれる側のページのまま残し、レイアウト自身の
設計時の式には `Chrome` という別の名前を付けました。

## レイアウトを入れ子にする

レイアウト自身を、別のレイアウトの中に置けます。ページに付けるのとまったく同じように、
レイアウトの型に `[Layout]` を付けます。

```csharp
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

[Layout(typeof(SiteLayout))]
public partial class DocsLayout : ChromeLayoutBase
{
    protected override View Chrome => Div.Class("docs")[Aside[TableOfContents()], Main[Body]];
}
```

入れ子を解決するのは Blazor で、BlazorCodeFirst ではありません。`LayoutView` がレイアウトの型
から属性を読み、それを自分のレイアウトで包みます。BlazorCodeFirst のレイアウトは、通常の
`LayoutComponentBase` の子孫です。どの段の `Body` も、その1つ下の段を受け取ります。`SiteLayout`
の `Body` は描かれた `DocsLayout` で、その `DocsLayout` の `Body` がルーティングされたページです。

## RenderFragment はそのまま内容になる

`Body` は BlazorCodeFirst の型ではなく、ただの Blazor の `RenderFragment?` です。それでも上の
`Main[Body]` は、専用の構文なしにコンパイルできます。`View` が `RenderFragment?` からの暗黙の
変換を持っているからです。そのためフラグメントは、要素の内容が来る場所ならどこにでも書けます。
変換元はジェネリックでない `RenderFragment` だけで、`RenderFragment<T>` は変換されません。
`Fragment` や `Raw` と同じように、`RenderFragment` はキーを付けられるフレームを開きません。よって
`ForEach` の内容のルートにはできず（[BCF3003](./diagnostics.md#bcf3003)）、装飾も付けられ
ません
（[BCF3008](./diagnostics.md#bcf3008)）。

同じ仕組みで、BlazorCodeFirst のコンポーネントは Razor から渡された子を描けます。
`[Parameter] public RenderFragment? ChildContent` を持つコンポーネントは、それを `Body` と
まったく同じように使います。

```csharp
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

public partial class Card : BodyComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override View Body => Div.Class("card")[ChildContent];
}
```

逆向き、つまり BlazorCodeFirst のコードから Razor や手書きのコンポーネントへ内容を渡す場合は、
`Component<T>()` を使います。[子の内容を渡す](./components-and-reuse.md#子の内容を渡す)を見て
ください。

## 読めるが、書き換えられない

`Chrome` と `Body` は、どちらもコンポーネントの状態を読めます。状態を UI へ映すことが、
そもそもの役目だからです。ただし、どちらも状態を書き換えられません。ここでの `Body` は
`BodyComponentBase` のもので、レイアウトがルーティングされたページを受け取るパラメーターでは
ありません。どちらの中で状態を書き換えても [BCF3001](./diagnostics.md#bcf3001) を報告します。
通常のコンポーネントの `Body` に当てはまるのと同じ診断です。

## 次に読むもの

- コンポーネントから別のコンポーネントを呼ぶ方法は[コンポーネントと再利用](./components-and-reuse.md)へ。
- 上で使った要素の語彙は[要素と装飾](./elements-and-decorations.md)へ。
