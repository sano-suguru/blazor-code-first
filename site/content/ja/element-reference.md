---
title: 要素リファレンス
description: この API が宣言する要素ヘルパー、装飾、制御構文の一覧。空要素には印を付けている。
order: 90
group: reference
source-hash: f5c912c9
---

この API が宣言するものの一覧です。それぞれの意味は
[要素と装飾](./elements-and-decorations.md)と[制御構文](./control-flow.md)にあります。

## 要素

ヘルパーの名前はタグ名の先頭1文字だけを大文字にしたものです。`FigCaption` ではなく
`Figcaption` です。アスタリスクは空要素の印で、子を取りません
（[BCF3016](./diagnostics.md#bcf3016)）。

100個あります。HTML Living Standard が準拠要素として挙げ、かつレンダーツリーで意味を持つ要素の
すべてです。

### セクション

`Address`, `Article`, `Aside`, `Footer`, `H1`, `H2`, `H3`, `H4`, `H5`, `H6`, `Header`, `Hgroup`,
`Main`, `Nav`, `Search`, `Section`

### グルーピング

`Blockquote`, `Dd`, `Div`, `Dl`, `Dt`, `Figcaption`, `Figure`, `Hr`\*, `Li`, `Menu`, `Ol`, `P`,
`Pre`, `Ul`

### テキストレベル

`A`, `Abbr`, `B`, `Bdi`, `Bdo`, `Br`\*, `Cite`, `Code`, `Data`, `Dfn`, `Em`, `I`, `Kbd`, `Mark`,
`Q`, `Rp`, `Rt`, `Ruby`, `S`, `Samp`, `Small`, `Span`, `Strong`, `Sub`, `Sup`, `Time`, `U`, `Var`,
`Wbr`\*

### 編集

`Del`, `Ins`

### 埋め込み

`Area`\*, `Audio`, `Canvas`, `Embed`\*, `Iframe`, `Img`\*, `Map`, `Picture`, `Source`\*, `Track`\*,
`Video`

### 表

`Caption`, `Col`\*, `Colgroup`, `Table`, `Tbody`, `Td`, `Tfoot`, `Th`, `Thead`, `Tr`

### フォーム

`Button`, `Datalist`, `Fieldset`, `Form`, `Input`\*, `Label`, `Legend`, `Meter`, `Optgroup`,
`Option`, `Output`, `Progress`, `Select`, `Selectedcontent`, `Textarea`

### 対話

`Details`, `Dialog`, `Summary`

### それ以外: `Element(tag)`

`Element("my-widget")` は、ヘルパーの無い要素を書くための構文です。カスタム要素と Web Components、
そして意図して外した標準要素がこれに該当します。文書と `<head>` の要素、生テキスト要素、`template`
と `slot`、`object`、SVG と MathML の要素です。タグはタグ名の形をしたコンパイル時定数で
なければなりません（[BCF3009](./diagnostics.md#bcf3009)）。何が該当し、なぜ該当するのかは
[要素と装飾](./elements-and-decorations.md#要素)にあります。

## 要素に付ける装飾

| 装飾 | 書くもの |
| --- | --- |
| `.Attr(name, value)` | 任意の属性を名前で |
| `.Class(value)` | class のチャネル。畳まれる（[class のチャネル](./elements-and-decorations.md#class-のチャネル)） |
| `.Role(value)` | `role` |
| `.On(name, handler)` | 任意のイベントを名前で |
| `.OnClick(handler)` | `onclick` |
| `.PreventDefault()` | 直前のイベントの `preventDefault` |
| `.StopPropagation()` | 直前のイベントの `stopPropagation` |
| `.Bind(…)` | 双方向バインド（[双方向バインディング](./two-way-binding.md)） |
| `.Key(value)` | 差分用のキー。マークアップには残らない（[制御構文](./control-flow.md#foreach-とそのキー)） |
| `.Ref(capture)` | 要素参照。マークアップには残らない |

ほかの装飾はすべて標準由来の属性ショートカットです。HTML Living Standard の属性名が C# の識別子に
そのまま綴れるものへ、1つずつ対応します。`.Id(value)` は `id` を、`.HttpEquiv(value)` は
`http-equiv` を書きます。142個あり、略記であって別の仕組みではありません。`.Id("x")` と
`.Attr("id", "x")` は同じフレームを出し、どちらも同じチャネルを数えます
（[BCF3010](./diagnostics.md#bcf3010)）。

ショートカットが取る型は、Blazor が条件付き属性として読む属性なら `bool`（`true` で値の空な属性を
書き、`false` で出しません）、それ以外は `string?` です。

### 真偽値を取るもの

`.Allowfullscreen`, `.Alpha`, `.Async`, `.Autofocus`, `.Autoplay`, `.Checked`, `.Controls`,
`.Default`, `.Defer`, `.Disabled`, `.Formnovalidate`, `.Headingreset`, `.Inert`, `.Ismap`,
`.Itemscope`, `.Loop`, `.Multiple`, `.Muted`, `.Nomodule`, `.Novalidate`, `.Open`, `.Playsinline`,
`.Readonly`, `.Required`, `.Reversed`, `.Selected`, `.Shadowrootclonable`,
`.Shadowrootcustomelementregistry`, `.Shadowrootdelegatesfocus`, `.Shadowrootserializable`

### 文字列を取るもの

`.Abbr`, `.Accept`, `.AcceptCharset`, `.Accesskey`, `.Action`, `.Allow`, `.Alt`, `.As`,
`.Autocapitalize`, `.Autocomplete`, `.Autocorrect`, `.Blocking`, `.Charset`, `.Cite`, `.Closedby`,
`.Color`, `.Colorspace`, `.Cols`, `.Colspan`, `.Command`, `.Commandfor`, `.Content`,
`.Contenteditable`, `.Coords`, `.Crossorigin`, `.Data`, `.Datetime`, `.Decoding`, `.Dir`, `.Dirname`,
`.Download`, `.Draggable`, `.Enctype`, `.Enterkeyhint`, `.Fetchpriority`, `.For`, `.Form`,
`.Formaction`, `.Formenctype`, `.Formmethod`, `.Formtarget`, `.Headers`, `.Headingoffset`, `.Height`,
`.Hidden`, `.High`, `.Href`, `.Hreflang`, `.HttpEquiv`, `.Id`, `.Imagesizes`, `.Imagesrcset`,
`.Inputmode`, `.Integrity`, `.Is`, `.Itemid`, `.Itemprop`, `.Itemref`, `.Itemtype`, `.Kind`, `.Label`,
`.Lang`, `.List`, `.Loading`, `.Low`, `.Max`, `.Maxlength`, `.Media`, `.Method`, `.Min`, `.Minlength`,
`.Name`, `.Nonce`, `.Optimum`, `.Pattern`, `.Ping`, `.Placeholder`, `.Popover`, `.Popovertarget`,
`.Popovertargetaction`, `.Poster`, `.Preload`, `.Referrerpolicy`, `.Rel`, `.Rows`, `.Rowspan`,
`.Sandbox`, `.Scope`, `.Shadowrootmode`, `.Shadowrootslotassignment`, `.Shape`, `.Size`, `.Sizes`,
`.Slot`, `.Span`, `.Spellcheck`, `.Src`, `.Srcdoc`, `.Srclang`, `.Srcset`, `.Start`, `.Step`,
`.Tabindex`, `.Target`, `.Title`, `.Translate`, `.Type`, `.Usemap`, `.Value`, `.Width`, `.Wrap`,
`.Writingsuggestions`

## 構文

| 構文 | すること |
| --- | --- |
| `If(condition, then, otherwise?)` | 分岐。互いに重ならないシーケンス範囲を取る |
| `ForEach(source, key, content)` | キー付きのリスト |
| `Fragment(children…)` | 包む要素を出さずに子をまとめる |
| `Raw(html)` | 信頼できる HTML をそのまま差し込む |
| `Slot` | `SlotView` を返す `[ViewPart]` が、呼び出し側の子を置く場所 |
| `Component<T>()` | Blazor コンポーネントを呼ぶ |

## コンポーネントの呼び出しに付ける装飾

| 装飾 | 書くもの |
| --- | --- |
| `.Param(selector, value)` | `[Parameter]` を1つ |
| `.Template(selector, template)` | `RenderFragment<T>` のパラメーター |
| `.Bind(selector, …)` | パラメーターの双方向バインド（[双方向バインディング](./two-way-binding.md#コンポーネントのパラメーターをバインドする)） |
| `.Key(value)` | 差分用のキー |
| `.RenderMode(mode)` | 呼び出し側のレンダーモード（[導入とホスティング](./installation-and-hosting.md#レンダーモードを指定する)） |
| `.Ref(capture)` | コンポーネント参照 |

角括弧に書いた子の内容は `ChildContent` を設定し、そのパラメーターのバインドとして数えます
（[BCF3007](./diagnostics.md#bcf3007)）。

## 基底型

| 型 | 用途 |
| --- | --- |
| `BodyComponentBase` | コンポーネント。`Body` をオーバーライドする |
| `ChromeLayoutBase` | レイアウト。`Chrome` をオーバーライドする（[レイアウト](./layouts.md)） |
| `[ViewPart]` | マークアップを呼び出し側へ展開するメソッド |
| `SlotView` | 呼び出し側の子を受け取るパーツの戻り値の型 |
