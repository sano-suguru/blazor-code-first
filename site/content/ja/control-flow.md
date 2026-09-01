---
title: 制御構文
description: If と ForEach。テンプレートの各位置にコンパイル時のシーケンス番号を割り当てるための構文。
order: 50
group: write
source-hash: 46485d19
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

キーの引数に既定値はありません。見分ける材料を持たないリストは、`key: null` と明示します。

```csharp
Ul[ForEach(_columns, key: null, content: c => Li[c.Header])]
```

固定のメニュー、決まった列の集合、並び替わらない `Select` の結果には、この書き方が適します。
代償は、BCF3002 が警告しているものと同じです。差分の計算はインデックスをキーに
したときと同じになり、先頭に1件挿入すると全行が書き直され、行ごとの状態が失われます。`SetKey` を
出さないので、BCF3002 は検査する対象を持たず、BCF3003 も適用されません。内容のルートには、
`Fragment`、`Raw`、単独の `If` のいずれも置けます。

## `Select` の結果を子に展開する

子のリストには、通常の射影、つまり `Select` の結果をスプレッドで展開する書き方もあります。

```csharp
Ul[[.. _columns.Select(c => Li[c.Header])]]
```

上のキーを使わない `ForEach` の、2つ目の書き方です。同じ `foreach` になります。展開した項目はその
位置に並ぶので、手で書いた子と混ぜられます。

```csharp
Ul[[Li["先頭"], .. _columns.Select(c => Li[c.Header]), Li["末尾"]]]
```

畳めるのは `<source>.Select(<その場に書いた式のラムダ>)` と、イテレータ `[ViewPart]` の呼び出し
（下の[`[ViewPart]` でイテレートする](#viewpart-でイテレートする)）です。それ以外のスプレッド、
たとえば保存した `View` の配列や、それを返すメソッドは、静的に順序付けできる子ではないので
[BCF1003](./diagnostics.md#bcf1003) を報告します。保存した `View` を子として1つ書いたときと同じ
結果です。

## `[ViewPart]` でイテレートする

`[ViewPart]` はイテレータにもできます。`IEnumerable<View>` を返す `static` メソッドで、本体の末尾
で C# 本来の `foreach` が繰り返しごとに1つ `yield return` します。

```csharp
[ViewPart]
private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
{
    foreach (var item in items)
    {
        yield return Li.Key(item.Id)[item.Name];
    }
}
```

呼び出しは他のスプレッドと同じ形です。

```csharp
Ul[[.. Rows(_items)]]
```

これは通常の `[ViewPart]` 呼び出しとは違う経路です。通常の呼び出しは呼び出し箇所ごとに本体を展開
しますが、繰り返す回数は実行時にしか分かりません。そのためスプライスしたイテレータ部品は
`ForEach` 自身の出力をそのまま使い回します。静的なシーケンス空間を1つだけ持ち、それを繰り返しの
たびに使い回すのは、上のキーを使わないスプレッドと同じで、呼び出しごとに本体をコピーするわけでは
ありません。

`.Key(...)` は省略でき、書く場合は yield した要素自身の装飾として書きます。要素自身のキーであり、
`ForEach` の `key:` 引数のように別の引数へは渡しません。C# 本来の `foreach` のヘッダーには、それを
渡す引数の場所がないからです。省略すると `SetKey` は出ません。`ForEach` でキーを使わないときと
同じです。

ここで受け付けるのは `yield return` だけで、それも `[ViewPart]` でだけです。`foreach` を `return`
で終える書き方では、最初の1件で抜けてしまい、全件を作ることになりません。C# が `yield return` を
許すのは本物のイテレータの中だけで、それになれるのはメソッドだけです。プロパティのゲッターやラム
ダはなれません。そのためこの形は `foreach`/`if`/`switch` を受け付けるどの位置にも通らず、
`[ViewPart]` のこの位置だけで受け付けます（[BCF1002](./diagnostics.md#bcf1002)）。

`[ViewPart]` は他と同じく `static` である必要があります。本体からインスタンスフィールドを直接読む
ことはできないので、ループの元になる値は常に引数として渡します（上の `items`）。他の `[ViewPart]`
の引数と同じです。

## Fragment

`Fragment` は、ラッパーの要素を出さずに、複数の子を1つの `View` にまとめます。

```csharp
Fragment(H2["Title"], P["Body"])
```

要素を開かないので、装飾は付けられず（[BCF3008](./diagnostics.md#bcf3008)）、`ForEach` の内容の
ルートにもできません（[BCF3003](./diagnostics.md#bcf3003)）。

## Raw

`Raw` は、HTML の文字列をそのまま流し込みます。`MarkupString` にあたるものです。

:::warning
`Raw` はエスケープせずに DOM へ書き込みます。渡してよいのは、自分で作った内容だけです。利用者が
入力した文字列や、外部のサービスから返ってきた文字列を渡すと、そのまま HTML として解釈され、
含まれていたスクリプトが動きます。
:::

このページも `Raw` で表示しています。Markdown をビルド時に HTML へ変換し、その結果を `Raw` に渡し
ています。変換するのは、このリポジトリにあるツールです。

```csharp
Article.Class("prose")[Raw(entry.Html)]
```

## 次に読むもの

- 書ける要素の一覧は[要素と装飾](./elements-and-decorations.md#装飾)へ。
- コンポーネントから別のコンポーネントを呼ぶ方法は[コンポーネントと再利用](./components-and-reuse.md)へ。
