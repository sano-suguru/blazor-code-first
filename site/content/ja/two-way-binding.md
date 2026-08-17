---
title: 双方向バインディング
order: 80
group: write
source-hash: 269abcef
---

双方向バインディングは、値を DOM へ書き出し、利用者の編集を自分の状態へ読み戻す、1つの装飾
です。`.Bind` は Razor の `@bind` にあたります。必要なものが、すべて目に見える引数として並ぶ
書き方になっています。ジェネレーターはこれを、属性のフレームと、Blazor 自身の `CreateBinder`
を運ぶイベントのフレームに落とします。生成されたコードが実行時に式ツリーをコンパイルすることは
なく、リフレクションを使うこともありません。enum をバインドしたときだけ、フレームワーク自身の
変換器の中で1つだけリフレクションによる参照が起きます。

## 要素をバインドする

値を運ぶ属性、変化を知らせるイベント、そして今の値を読むラムダを指定します。この3つのうち、
マークアップに現れるのは最初の1つだけです。

```csharp
protected override View Body =>
    Div[
        Input.Type("text").Bind("value", "oninput", () => _name),
        P[$"Hello, {_name}"]];
```

```html
<div>
    <input type="text" value="Ada">
    <p>Hello, Ada</p>
</div>
```

このラムダは両方向に読まれます。本体は属性の値になり、ゲッターだけのこの形では、同じ本体が、
ジェネレーターの書く代入の左辺にもなります。そのため対象は代入できるものでなければなりません。
フィールド、セッターを持つプロパティ、あるいはそのどちらかを通る経路（`_form.Name`、
`Model.Items[0].Title`、`_dict["k"]`）です。`() => _name.ToUpper()` のような計算した式は
[BCF3018](./diagnostics.md#bcf3018) を報告します。そう書きたいときは、下のセッターを明示する形
を使います。

キー入力のたびにバインドするなら `"oninput"`、要素からフォーカスが外れたときにバインドするなら
`"onchange"` を使います。通常使うのはこの2つですが、組み合わせを縛る一覧はありません。
制約は次の3つの規則だけです。

- どちらの名前も、空でないコンパイル時定数であること（[BCF3011](./diagnostics.md#bcf3011)）
- イベントの名前が `on` で始まること（[BCF3019](./diagnostics.md#bcf3019)）
- どちらも、同じ要素の別の装飾で既にバインドされていないこと（[BCF3010](./diagnostics.md#bcf3010)）

名前を HTML と照合する検査はありません。

## 名前を2つとも書く理由

Razor は属性をマークアップから推論します。`.razor` のファイルからリテラルの `type="checkbox"` を
読み取り、`value` ではなく `checked` をバインドします。この API には、読み取れるリテラルがあり
ません。タグは文字列で、`type` は式です。`Input.Type(kind)` は通常の C# の呼び出しで、その値は
実行するまで決まらないこともあります。そのため推論を照合する対象がありません。

`value` を既定にすると、もっとも避けたい失敗が起こります。チェックボックスが間違った属性に
バインドされ、それが黙って起き、知らせる診断も出ない、という失敗です。そのためこの API 全体の
規則は **確かめられることだけを推論する** です。要素の側は確かめられないので推論せず、代わりに
短い文字列を2つ書きます。

この間違いのうち、確かめ *られる* 半分は検出します。`on` で始まらないイベントの名前は BCF3019 を
報告します。そのため2つの引数を取り違えても、何もしない属性が増えるのではなく、コンパイル時に
止まります。

`.Bind` のコンポーネント側は名前を推論します。同じ規則が、そちらでは推論を許すからです。
[コンポーネントのパラメーターをバインドする](#コンポーネントのパラメーターをバインドする)を
見てください。

## チェックボックスは `checked` をバインドする

チェックボックスは、`bool` を `checked` 属性へ、`onchange` でバインドします。同じ装飾で、最初の
引数だけが違い、出力に出る属性も違います。

```csharp
protected override View Body =>
    Label[
        Input.Type("checkbox").Bind("checked", "onchange", () => _agreed),
        " I agree"];
```

```html
<label><input type="checkbox" checked> I agree</label>
```

`bool` は HTML の真偽値属性の形で、[`.Attr` が取る](./elements-and-decorations.md#装飾)のと同じ
ものです。`true` なら値の空な属性として出力し、`false` なら属性ごと出しません。

## セッターを明示して正規化する

4つ目の引数を渡すと、生成される代入の代わりに自分のセッターが使われます。検証や正規化、編集の
たびに実行したい処理は、ここに置きます。

```csharp
private string _name = "";

protected override View Body =>
    Input.Type("text").Bind("value", "oninput", () => _name, v => _name = v.Trim());
```

これは Razor が `@bind:get` / `@bind:set` と `@bind:after` に分けているものを、まとめて引き受け
ます。セッターが書き込みそのものなので、後で実行したかった処理も同じラムダに入ります。ただし
書き込みは自分で行います。セッターを渡した時点で、代わりに代入する処理はなくなります。
メソッドグループ（`SetName`）も使え、ラムダの本体はブロックでもよく、`Task` を返せば `async`
の形も使えます。

```csharp
Input.Type("text").Bind("value", "oninput", () => _query, async v =>
{
    _query = v;
    await SearchAsync(v);
});
```

代入できるゲッターが必要なのは、ゲッターだけの形のときだけです。セッターがあれば、ゲッターは読ま
れるだけなので、どんな式でもかまいません。ただしゲッター自身は、どちらの形でも、その場に書いた
式本体のラムダである必要があります（[BCF3017](./diagnostics.md#bcf3017)）。その本体が、属性の値
とバインダーの両方へ写されるからです。

正規化するセッターは、ずれを生みます。要素は入力した文字を表示し、フィールドは切り詰めた値を持ち
ます。通常の差分計算は何も書きません。前回のレンダリングからレンダーツリーが変わっていないから
です。`value` か `checked` のバインドなら、`.Bind` はその属性を DOM の再同期に登録して、この
ずれを埋めます。要素は正規化した値を表示するよう修正されます。設定は不要です。

登録される名前はこの2つだけです。Blazor のクライアントが返してくるのは、フォーム要素自身の
`value`、チェックボックスなら `checked` で、それ以外は返ってきません。ほかの属性へのバインドは
何も登録しません。カスタム要素での `.Bind("hue", "onhuechange", () => _hue, Normalize)` が、
よくある形です。セッターは変わらず実行され、新しい値も次のレンダリングで通常の差分計算によって
DOM へ届きます。行われないのは、上に書いた修復だけです。この修復は、正規化してもレンダーツリーが
変わらず、要素が入力した文字を表示し続ける場合のためにあります。

テキスト入力を空にしたとき、セッターが受け取るのは `""` で、`null` ではありません。そのため引数
の型は null 許容でない `string` です。セッターから自分の状態へ書き込むのは許されています。`Body`
のほかの場所で書き込めば [BCF3001](./diagnostics.md#bcf3001) になります。セッターだけが例外なの
は、`.OnClick` のラムダと同じ遅延したハンドラーで、ツリーを組み立てている最中には実行されない
ためです。

## 数値、日付、enum

カルチャーを書けば、どの型でもバインドできます。カルチャーは最後の引数で、省略はできません。

```csharp
private int _age;

protected override View Body =>
    Input.Type("number").Bind("value", "oninput", () => _age, CultureInfo.InvariantCulture);
```

このカルチャーが、出ていく値を書式化し、戻ってきた値を解析します。通るのは Blazor 自身の
`BindConverter` です。数値、日付、時刻、`Guid`、enum、そしてそれらの null 許容形が、どれも動き
ます。変換がこのライブラリのものではなく、フレームワークのものだからです。

既定値ではなく引数にしているのは、既定値にすれば、見えないところでカルチャーが選ばれるからです。
Razor は要素のリテラルな `type` から選びます。この API はそのリテラルを読みません。属性の名前を
推論しないのと同じ理由です。そのため選択は、呼び出し側へ移してあります。

### `number` と `date` には不変カルチャーを書く

`<input type="number">` と `<input type="date">` は、利用者のロケールではなく固定の書式で定義され
ています。ここで `CultureInfo.CurrentCulture` を使うと、小数点にコンマを使うロケールで、要素の
受け取れない値になります。

**これは診断しません。** 検査するには `type` を読む必要がありますが、ここでの `type` は式です。
リテラルを書いたときだけ発火する規則は、同じ誤りをある書き方では捕まえ、別の書き方では見逃し
ます。この2つには `CultureInfo.InvariantCulture` を書いてください。利用者が文章として読む値には
現在のカルチャーを使います。

### 解析できない値は元に戻る

入力された文字を変換器が読めない場合、セッターは呼ばれません。フィールドと要素は、どちらも前の値
に戻ります。これは Blazor の挙動で、`.Bind` は上に書いた DOM の再同期を通じてそこへ届きます。

意識して選ぶべき帰結が1つあります。`"oninput"` では巻き戻しがキー入力のたびに実行されるので、`int`
のバインドに入力した小数点は残りません。`4.` が拒否され、`.` がそのまま取り去られます。数値の
入力では、多くの場合これは望ましくありません。

```csharp
// フォーカスが外れたときに巻き戻すので、入力途中の数値が消えません。
Input.Type("number").Bind("value", "onchange", () => _amount, CultureInfo.InvariantCulture);
```

`"oninput"` が正しいのは、途中の値がどれも意味を持つときです。range のスライダーや、入力できる
文字をすべて受け付ける text の欄がこれにあたります。

欄を空にした場合は事情が異なり、これは拒否ではありません。Blazor は空文字列をその型の既定値として
読むので、`int` のバインドを空にすると前の値が残るのではなく `0` になります。値が本当に任意なら
`int?` をバインドしてください。そちらは `null` を受け取ります。

### 日付には書式が必要

date の入力は `yyyy-MM-dd` を要求します。Razor と違い、この API はそれを `type` から補えません。
カルチャーの1つ手前の引数として書きます。

```csharp
private DateOnly _due = new(2026, 8, 14);

protected override View Body =>
    Input.Type("date").Bind(
        "value", "oninput", () => _due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
```

書式を受け取るのは `DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly` とその null 許容形だけ
で、ほかの型は受け取りません。フレームワークが書式付きの変換器を宣言しているのが、その8つだから
です。`int` に書けば [BCF3031](./diagnostics.md#bcf3031) です。数値を書式化したい場合は、ゲッター
で書式化し、セッターを明示して解析してください。

### トリムして配布する場合

値型を1つでもバインドすると、Blazor の `BindConverter` が丸ごと保持されます。自分がバインドしない
型の変換器も残ります。トリム済みの self-contained な publish で実測しました。
`Microsoft.AspNetCore.Components.dll` のおよそ 10 KB にあたります。`string` と `bool` だけを
バインドするアプリに、このコストはかかりません。バインドの個数ではなく、一度きりのコストです。

### 変換を自分で書く

明示する書き方も、これまでどおり使えます。変換ではなく検証をしたいときは、そちらが向いています。

```csharp
private decimal _amount;

protected override View Body =>
    Input.Type("text").Bind(
        "value", "onchange",
        () => _amount.ToString(CultureInfo.InvariantCulture),
        v => _amount = decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : _amount);
```

ここでは `Parse` ではなく `TryParse` を使ってください。セッターから投げられた例外は、フレーム
ワークが値を拒否する経路ではありません。描画そのものが失敗します。

## コンポーネントのパラメーターをバインドする

コンポーネントでの名前は、書くのではなく導きます。そちらでは、導いた名前を確かめられるから
です。`.Bind` はラムダでパラメーターを選び、ジェネレーターが `{Name}Changed` を、コンポーネント
が宣言していれば `{Name}Expression` も足します。

```csharp
using Microsoft.AspNetCore.Components.Forms;

private readonly NameModel _model = new();

protected override View Body =>
    Component<InputText>().Bind(c => c.Value, () => _model.Name);
```

この1回の呼び出しが、`Value`、`ValueChanged`、`ValueExpression` を渡します。導いた名前はどれも
コンポーネントの型から探すので、`{Name}Changed` が無かったり綴りが違ったりすれば、何もバインド
しないのではなく [BCF3020](./diagnostics.md#bcf3020) を報告します。これが、要素側との非対称を、
ちぐはぐではなく規則にしています。

対象を参照渡しではなくゲッターのラムダとして書くのは、`{Name}Expression` のためです。`EditForm`
の下にあるコンポーネントは、その式から `FieldIdentifier` を解決します。この識別子が入力をモデル
のプロパティに結び付けるので、検証のメッセージが正しいフィールドに届きます。ほかの書き方では、
これを渡せません。

周りの `EditForm` も同じ書き方です。`EditForm.ChildContent` は `RenderFragment<EditContext>` で、
角括弧は、コンテキストを捨ててこれに渡します。下の内容には、ほかに必要なものはありません。
`EditContext` を読む内容だけ、`.Template` で指定します。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

バインドした入力は、角括弧の中に直接置いても、独立したコンポーネントに置いてもかまいません。
上のバインドと、それが渡す `ValueExpression` は、どちらでも変わりません。`EditContext` を
読む書き方と、`.Param` でキャッシュしたデリゲートを渡すほうがよい場面については、
[ジェネリックなフラグメントのパラメーター](./components-and-reuse.md#ジェネリックなフラグメントのパラメーター)を
見てください。

セッターを明示する形と `async` のセッターは、こちらでも使えます。意味は要素のときと同じです。
`TValue` は、カルチャーと書式のどちらも取りません。値が向かう先は DOM ではなくパラメーターです。
途中で書式化や解析が入らないので、書き留めるべき選択がそもそもありません。

`Component<T>()` が、同じプロジェクトで宣言した `.razor` のコンポーネントを指定できないことは
覚えておいてください（[BCF3012](./diagnostics.md#bcf3012)）。
`InputText` のようなフレームワークのコンポーネントと、手書きの C# のコンポーネントは、いつでも
解決します。

## 何が検査されるか

`.Bind` を読む診断は6つあり、どれも[リファレンス](./diagnostics.md)に項があります。

- [BCF3017](./diagnostics.md#bcf3017) はゲッターの形を見る
- [BCF3018](./diagnostics.md#bcf3018) は、ゲッターだけの形で代入できない対象を見る
- [BCF3019](./diagnostics.md#bcf3019) は `on` の無いイベント名を見る
- [BCF3020](./diagnostics.md#bcf3020) は、対応する変更コールバックの無いコンポーネントを見る
- [BCF3024](./diagnostics.md#bcf3024) は、`.Class` と並んだ `class` のバインドを見る
- [BCF3031](./diagnostics.md#bcf3031) は、値の型に変換器の無い書式を見る

1つの要素が `.Bind` を複数持つことはできます。そのうち2つが属性の名前かイベントの名前を共有
すれば [BCF3010](./diagnostics.md#bcf3010) で、どの装飾を2つ重ねても出るのと同じ重複です。
DOM の再同期の対象は `value` と `checked` だけです。利用者が入力した文字の上に、正規化した値を
置き直す修復のことです。ブラウザーがイベントと一緒に返してくるのが、その2つだけだからです。

## 次に読むもの

- これが組み立てられている、一方向の `.Attr` と `.On` は[要素と装飾](./elements-and-decorations.md#装飾)へ。
- `.Param` とコンポーネント周りの残りは[コンポーネントと再利用](./components-and-reuse.md)へ。
