---
title: Razor からの移行
order: 30
group: start
source-hash: 1579a416
---

このページには、Razor の書き方をこの API へ置き換える対応表と、綴りではなく形が違う4点を載せ
ます。

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
| `@foreach (var r in rows) { … }` | `ForEach(rows, key: r => r.Id, content: r => …)` |
| `@key="r.Id"` | `ForEach` の `key` 引数、または `.Key(…)` |
| `@ref="_el"` | `.Ref(…)` |
| `<Card Title="x" />` | `Component<Card>().Param(c => c.Title, "x")` |
| `<Card>…</Card>` | `Component<Card>()[…]` |
| `<CascadingValue Value="@_t">` | `Component<CascadingValue<T>>().Param(c => c.Value, _t)[…]` |
| `@((MarkupString)html)` | `Raw(html)` |
| `<text>…</text>` | `Fragment(…)` |
| `@layout MainLayout` | `[Layout(typeof(MainLayout))]`、そのまま |
| レイアウトの `@Body` | オーバーライドした `Chrome` の中の `Body`（[レイアウト](./layouts.md)） |

## 綴りではなく形が違う4点

### 属性が子より先に来る

Razor では属性を開始タグのどこに書いてもよく、子はその後に続きます。ここでは装飾が要素に繋がり、
子は角括弧に入り、順序はこの通りです。角括弧はもう `View` を作り終えているので、その後ろに装飾を
繋ぐことはできません（[BCF3008](./diagnostics.md#bcf3008)）。

### ゲッターは1つの式へ到達する

`.razor` ファイルは、文を含むテンプレートです。`Body` は返り値の式が1つで、その手前にローカル
変数を置けます。2つ目の `return` やネイティブの `if` は、それぞれ専用のシーケンス空間を要する
ため、どちらも受け付けません（[BCF1004](./diagnostics.md#bcf1004)）。

`If` と `ForEach` が C# のキーワードではなく構文として用意されているのも、同じ理由です。分岐の
あるテンプレートを書き直す前に[制御構文](./control-flow.md)を読んでください。

### キーは必須で、外すなら明示する

Razor の `@key` は省略でき、省略しても気づきません。`ForEach` の `key` に既定値はないので、
リストは項目を識別するか、`key: null` と書いて代償を受け入れるかのどちらかです。それが
[キーを使わない](./control-flow.md#キーを使わない)です。

### HTML とは別の名前を覚え直さない

どの要素も同じ名前の HTML 要素で、レイアウトは CSS が担います。`VStack` と `.Padding()` は
ありません。`Text()` もなく、文字列をそのまま書けばテキストノードです。要素について知るべきこと
は、HTML について知っていることだけです。

## コンポーネントを1つ書き直す

マークアップから始めて、C# には手を付けないでください。`@code` のメンバーをクラス本体へ移し、
テンプレートを `Body` ゲッターに置き換え、翻訳できないものはビルドに報告させます。どの診断も
代わりに書くものを示し、[リファレンス](./diagnostics.md)に項があります。

先に出る間違いは機械的なものです。クラスが `partial` でない
（[BCF1001](./diagnostics.md#bcf1001)）、ゲッターが `return` の後ろにも文を持っている
（[BCF1004](./diagnostics.md#bcf1004)）の2つです。

## 次に読むもの

- 要素の全体は[要素と装飾](./elements-and-decorations.md)。
- 2種類のコンポーネントを1つのプロジェクトで混ぜるなら
  [コンポーネントと再利用](./components-and-reuse.md#razor-から-blazorcodefirst-のコンポーネントを使う)。
