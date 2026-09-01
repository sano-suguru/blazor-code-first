---
title: Razor からの移行
description: Razor 構文からこの API への対応表と、置き換えでは済まず、コンポーネントの形そのものが変わる4箇所。
order: 30
group: start
source-hash: 756bedb3
---

このページには、Razor の書き方をこの API へ置き換える対応表と、その置き換えでは済まず、
コンポーネントの形そのものが変わる4点を載せます。

## 変わらないもの

ここで書くコンポーネントは Blazor のコンポーネントです。`BodyComponentBase` は `ComponentBase`
から派生します。ライフサイクルメソッド、`[Parameter]`、`[Inject]`、`[CascadingParameter]` は、
いま使っているものがそのまま使えます。`StateHasChanged` と `IDisposable` も同じです。`.razor`
ファイルからこのコンポーネントを呼べますし、その逆もできます
（[コンポーネントと再利用](./components-and-reuse.md#既存の-razor-コンポーネントやサードパーティのコンポーネントを呼ぶ)）。

## 対応表

| Razor | ここ |
| --- | --- |
| `@page "/counter"` | `[Route("/counter")]` |
| `@inject IJSRuntime Js` | `[Inject] private IJSRuntime Js { get; set; }` |
| `@code { … }` | 通常のクラスメンバー |
| `<div class="card">…</div>` | `Div.Class("card")[…]` |
| `<img src="/x.png" />` | `Img.Src("/x.png")` |
| `@onclick="Save"` | `.OnClick(() => Save())` |
| `@bind="_name"` | `.Bind("value", "oninput", () => _name)` |
| `@if (x) { … } else { … }` | `If(x, () => …, () => …)` |
| `@foreach (var r in rows) { … }` | `ForEach(rows, key: r => r.Id, content: r => …)`、または `[ViewPart]` イテレータをスプレッドで差し込む（[制御構文](./control-flow.md#viewpart-でイテレートする)） |
| `@key="r.Id"` | `ForEach` の `key` 引数、または `.Key(…)` |
| `@ref="_el"` | `.Ref(…)` |
| `<Card Title="x" />` | `Component<Card>().Param(c => c.Title, "x")` |
| `<Card>…</Card>` | `Component<Card>()[…]` |
| `<CascadingValue Value="@_t">` | `Component<CascadingValue<T>>().Param(c => c.Value, _t)[…]` |
| `@((MarkupString)html)` | `Raw(html)` |
| `<text>…</text>` | `Fragment(…)` |
| `@layout MainLayout` | `[Layout(typeof(MainLayout))]`、そのまま |
| レイアウトの `@Body` | オーバーライドした `Chrome` の中の `Body`（[レイアウト](./layouts.md)） |

## 置き換えでは済まない4点

### 属性が子より先に来る

Razor では、属性を開始タグのどこに書いてもよく、子はその後に続きます。この API では、装飾を要素
に繋げてから、子を角括弧に入れます。角括弧はその時点で `View` を作り終えているので、その後ろに
装飾を繋ぐことはできません（[BCF3008](./diagnostics.md#bcf3008)）。

### ゲッターが返すのは1つの式

`.razor` ファイルは、文を書けるテンプレートです。`Body` が返すのは1つの式で、その手前にローカル
変数を置けます。2つ目の `return` は専用のシーケンス空間を必要とするため受け付けません。C# 本来の
`if`/`switch` ならゲッターの末尾に置けますが、その分だけ縮退します
（[BCF1004](./diagnostics.md#bcf1004)、[BCF2002](./diagnostics.md#bcf2002)）。

`If` と `ForEach` が C# のキーワードではなく構文として用意されているのも、同じ理由です。分岐の
あるテンプレートを書き直す前に[制御構文](./control-flow.md)を読んでください。

### キーは必須で、外すなら明示する

Razor の `@key` は省略でき、書き忘れても気づきません。`ForEach` の `key` に既定値はないので、
リストは項目を識別するか、`key: null` と書いて代償を引き受けるかのどちらかです。後者が
[キーを使わない](./control-flow.md#キーを使わない)書き方です。

### 覚える名前は HTML の名前だけ

どの要素も同じ名前の HTML 要素で、レイアウトは CSS が担います。`VStack` と `.Padding()` は
ありません。`Text()` もなく、文字列をそのまま書けばテキストノードです。HTML を知っていれば、
要素について覚えることは他にありません。

## コンポーネントを1つ書き直す

書き直すのはマークアップだけで、C# はそのままにします。`@code` のメンバーをクラス本体へ移し、
テンプレートを `Body` ゲッターに書き換えます。置き換えられないものは、ビルドが指摘します。どの
診断も代わりに書くコードを示し、[リファレンス](./diagnostics.md)に1件ずつ項があります。

最初に出る間違いは機械的な2つです。1つはクラスに `partial` が付いていないこと
（[BCF1001](./diagnostics.md#bcf1001)）、もう1つはゲッターの `return` の後ろに文が残って
いること（[BCF1004](./diagnostics.md#bcf1004)）です。

## 次に読むもの

- 要素をひととおり見るなら[要素と装飾](./elements-and-decorations.md)。
- 2種類のコンポーネントを1つのプロジェクトで混ぜるなら
  [コンポーネントと再利用](./components-and-reuse.md#razor-から-blazorcodefirst-のコンポーネントを使う)。
