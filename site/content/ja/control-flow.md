---
title: 制御構文
order: 50
group: write
source-hash: d11f8fa0
---

条件分岐とリストには、専用の構文があります。テンプレートのどの位置にも、ジェネレーターがコン
パイル時にシーケンス番号を振れるようにするためです。

## If

`If` は、条件と内容のサンクを取ります。else の分岐は省けます。1回の描画は1つの状態なので、下の
出力は `_items.Length == 0` が選ばなかった側です。

```csharp
protected override View Body =>
    Div[
        If(_items.Length == 0,
            () => P["Nothing here yet."],
            () => Span[$"{_items.Length} items"])];
```

```html
<div><span>2 items</span></div>
```

互いに排他な分岐には、重ならないシーケンス番号の範囲を割り当てます。そのため分岐が切り替わっても、
周りにある兄弟の位置は動きません。

## ForEach とそのキー

`ForEach` は、位置ではなく要素そのものを見分けるキーを取ります。キーはマークアップに出力されま
せん。属性ではなく、差分計算への指示だからです。

```csharp
protected override View Body =>
    Ul[
        ForEach(_items,
            key: item => item.Id,
            content: item => Li[item.Name])];
```

```html
<ul>
    <li>Alpha</li>
    <li>Beta</li>
</ul>
```

シーケンス番号はテンプレートの位置を、キーはデータの実体を見分けます。インデックスをキーに渡す
と、差分の計算は意味を失います。並び替えたときに、Blazor が別の要素の状態を使い回すからです。

自分の要素をまったく参照しないキーは報告します。次の3つは、どれも
[BCF3002](./diagnostics.md#bcf3002) です。

- `key: _ => 0`
- ラムダの外にあるカウンターから読んだキー
- 入れ子のループで、外側の要素しか参照していない内側のキー

```csharp
ForEach(_groups, key: g => g.Id, content: g =>
    Div[ForEach(g.Items, key: i => g.Id, content: i => Span[i.Name])])   // 内側のキーで BCF3002
```

これはエラーではなく警告で、コンポーネントの出力は止めません。リストの表示そのものは正しく、
差分の計算だけが非効率になるからです。検査もあえて控えめです。見るのは要素を参照したかどうか
だけで、その値が要素を見分けられるかは検査しません。要素から作ってはいても実際には位置と変わら
ないキーは、この検査を通ります。BCF3002 は保証ではなく下限です。

どちらのラムダも、その場に書いた式のラムダである必要があります。メソッドをそのまま渡さず、
呼び出しで包んでください。`Row` ではなく `item => Row(item)` と書きます
（[BCF3004](./diagnostics.md#bcf3004)）。内容のルートは単一の要素かコンポーネントである必要が
あり（[BCF3003](./diagnostics.md#bcf3003)）、そのルートが自分でキーを書くこともできません
（[BCF3032](./diagnostics.md#bcf3032)）。

## キーを使わない

キーの引数に既定値はありません。見分ける材料を持たないリストは、`key: null` とはっきり書きます。

```csharp
Ul[ForEach(_columns, key: null, content: c => Li[c.Header])]
```

固定のメニュー、決まった列の集合、並び替わらない射影には、この書き方が適します。引き換えになる
のは、BCF3002 が警告しているものと同じ性質です。差分の計算はインデックスをキーにしたときと同じ
になり、先頭に1件挿入すると全行が書き直され、行ごとの状態が失われます。`SetKey` を出さないので、
BCF3002 は検査する対象を持たず、BCF3003 も適用されません。内容のルートには、`Fragment`、
`Raw`、単独の `If` のいずれも置けます。

## 射影を差し込む

子のリストには、通常の射影をスプレッドで差し込むこともできます。

```csharp
Ul[[.. _columns.Select(c => Li[c.Header])]]
```

これは上のキーを使わない `ForEach` の糖衣で、同じ `foreach` になります。子のリスト全体を渡す形と
違って、周りに書いた兄弟と混ざります。

```csharp
Ul[[Li["先頭"], .. _columns.Select(c => Li[c.Header]), Li["末尾"]]]
```

畳めるのは `<source>.Select(<その場に書いた式のラムダ>)` だけです。それ以外のスプレッド、たとえば
保存した `View` の配列や、それを返すメソッドは、静的に順序付けできる子ではないので
[BCF1003](./diagnostics.md#bcf1003) を報告します。保存した `View` を子として1つ書いたときと同じ
結果です。

## Fragment

`Fragment` は、ラッパーの要素を出さずに子をまとめます。`<>...</>` にあたるものです。

```csharp
Fragment(H2["Title"], P["Body"])
```

要素を開かないので、装飾は付けられず（[BCF3008](./diagnostics.md#bcf3008)）、`ForEach` の内容の
ルートにもできません（[BCF3003](./diagnostics.md#bcf3003)）。

## Raw

`Raw` は、信頼できる HTML の文字列をそのまま流し込みます。`MarkupString` にあたるものです。この
ページ自体もそうやって表示しています。Markdown をビルド時に HTML へ変換し、`Raw` に渡しています。

```csharp
Article.Class("prose")[Raw(entry.Html)]
```

`Raw` はエスケープせずに DOM へ書き込むので、受け付けてよいのは信頼できる内容だけです。利用者の
入力や、外部から返ってきた応答を通さないでください。

## 次に読むもの

- 要素の語彙は[要素と装飾](./elements-and-decorations.md#装飾)へ。
- コンポーネントから別のコンポーネントを呼ぶ方法は[コンポーネントと再利用](./components-and-reuse.md)へ。
