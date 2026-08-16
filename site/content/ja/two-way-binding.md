---
title: 双方向の束縛
order: 60
source-hash: 48941587
---

双方向の束縛は、値を DOM へ書き出し、利用者の編集を自分の状態へ読み戻す、1つの装飾です。`.Bind`
は Razor の `@bind` にあたります。必要なものが、すべて目に見える引数として並ぶ書き方になって
います。ジェネレーターはこれを、属性のフレームと、Blazor 自身の `CreateBinder` を運ぶイベントの
フレームに落とします。束縛した属性が `value` か `checked` のときは、要素を正直に保つための DOM
の再同期も加えます。生成されたコードが実行時に式ツリーをコンパイルすることはなく、リフレクション
を使うこともありません。enum を束縛したときだけ、フレームワーク自身の変換器の中で1つだけ
リフレクションによる参照が起きます。

## 要素を束縛する

値を運ぶ属性、変化を知らせるイベント、そして今の値を読むラムダを名指しします。この3つのうち、
マークアップに跡を残すのは最初の1つだけです。

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
ジェネレーターの書く代入の左辺にもなります。だから相手は代入できるものでなければなりません。
フィールド、セッターを持つプロパティ、あるいはそのどちらかを通る経路（`_form.Name`、
`Model.Items[0].Title`、`_dict["k"]`）です。`() => _name.ToUpper()` のような計算した式は
[BCF3018](./diagnostics.md#bcf3018) を報告します。それを書きたいときのための形が、下のセッターを明示する書き方です。

打鍵のたびに束縛するなら `"oninput"`、要素からフォーカスが外れたときに束縛するなら `"onchange"`
を使います。ふつうに欲しくなるのはこの2つですが、組み合わせを縛る一覧はありません。縛るのは
次の3つの規則だけです。

- どちらの名前も、空でないコンパイル時定数であること（[BCF3011](./diagnostics.md#bcf3011)）
- イベントの名前が `on` で始まること（[BCF3019](./diagnostics.md#bcf3019)）
- どちらも、同じ要素の別の装飾で既に束縛されていないこと（[BCF3010](./diagnostics.md#bcf3010)）

名前を HTML と照合する検査はありません。

## 名前を2つとも書く理由

Razor は属性をマークアップから推論します。`.razor` のファイルからリテラルの `type="checkbox"` を
読み取り、`value` ではなく `checked` を束縛します。この API には、読み取れるリテラルがありません。
タグは文字列で、`type` は式です。`Input.Type(kind)` はふつうの C# の呼び出しで、その値は走って
みるまで決まらないこともあります。だから推論を照合する相手がありません。

`value` を既定にすれば、避けるためにわざわざ手を尽くす価値のある失敗が起こります。チェック
ボックスが間違った属性に束縛され、それが黙って起き、知らせる診断もない、という失敗です。だから
この API 全体の規則は **確かめられることだけを推論する** になっています。要素の側は確かめられ
ないので、推論しません。代わりに短い文字列を2つ書きます。

この間違いのうち、確かめ *られる* 半分は捕まえます。`on` で始まらないイベントの名前は BCF3019 を
報告します。だから2つの引数を取り違えても、死んだ属性が増えることはなく、コンパイル時に止まり
ます。

`.Bind` のコンポーネント側は名前を推論します。同じ規則が、そちらでは推論を許すからです。
[コンポーネントのパラメーターを束縛する](#コンポーネントのパラメーターを束縛する)を見てください。

## チェックボックスは `checked` を束縛する

チェックボックスは、`bool` を `checked` 属性へ、`onchange` で束縛します。同じ装飾で、最初の引数
だけが違い、出力に出る属性も違います。

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
たびに走らせたい処理は、ここに置きます。

```csharp
private string _name = "";

protected override View Body =>
    Input.Type("text").Bind("value", "oninput", () => _name, v => _name = v.Trim());
```

これは Razor が `@bind:get` / `@bind:set` と `@bind:after` に分けているものを、まとめて覆います。
セッターが書き込みそのものなので、後で走らせたかった処理も同じラムダに入ります。ただし書き込み
は自分の仕事になります。セッターを渡した時点で、代わりに代入してくれるものはなくなります。
メソッドグループ（`SetName`）も使え、ラムダの本体はブロックでもよく、`Task` を返せば `async` の
形も使えます。

```csharp
Input.Type("text").Bind("value", "oninput", () => _query, async v =>
{
    _query = v;
    await SearchAsync(v);
});
```

代入できるゲッターが要るのは、ゲッターだけの形のときだけです。セッターがあれば、ゲッターは読ま
れるだけなので、どんな式でもかまいません。

正規化するセッターは、ずれを生みます。要素は打った文字を見せ、フィールドは切り詰めた値を持ち
ます。ふつうの差分は何も書きません。前回のレンダリングからレンダーツリーが変わっていないから
です。`value` か `checked` の束縛なら、`.Bind` はその属性を DOM の再同期に登録して、このずれを
埋めます。要素は正規化した値を見せるように直されます。頼まなくても、そうなります。

この2つの名前がすべてです。Blazor のクライアントが返してくるのは、フォーム要素自身の `value`、
チェックボックスなら `checked` で、それ以外は返ってきません。だからジェネレーターが登録する名前
も、その2つだけです。ほかの属性への束縛は何も登録しません。カスタム要素での
`.Bind("hue", "onhuechange", () => _hue, Normalize)` が、よくある形です。セッターは変わらず走り、
新しい値も次のレンダリングでふつうの差分によって DOM へ届きます。無いのは、上に書いた修復だけ
です。正規化してもレンダーツリーが変わらず、要素が打った文字を見せ続ける場合のための修復です。

テキスト入力を空にしたとき、セッターが受け取るのは `""` で、`null` ではありません。だから引数の
型は null 許容でない `string` です。セッターから自分の状態へ書き込むのは許されています。`Body`
のほかの場所で状態へ書き込めば [BCF3001](./diagnostics.md#bcf3001) になるのに、です。セッターは
`.OnClick` のラムダと同じ遅延したハンドラーで、ツリーを組み立てている最中には走らないからです。

## 数値、日付、enum

カルチャーを書けば、どの型でも束縛できます。カルチャーは最後の引数で、省略はできません。

```csharp
private int _age;

protected override View Body =>
    Input.Type("number").Bind("value", "oninput", () => _age, CultureInfo.InvariantCulture);
```

このカルチャーが、出ていく値を書式化し、戻ってきた値を解析します。通るのは Blazor 自身の
`BindConverter` です。数値、日付、時刻、`Guid`、enum、そしてそれらの null 許容形が、どれも動き
ます。変換がこのライブラリのものではなく、フレームワークのものだからです。

既定値ではなく引数にしているのは、既定値にすれば、見えないところでカルチャーが選ばれるからです。
Razor は要素のリテラルな `type` から選びます。この API が読まないあのリテラルで、属性の名前を
推測しないのと同じ理由です。だから選択は、目に見える位置へ移してあります。

### `number` と `date` には不変カルチャーを書く

`<input type="number">` と `<input type="date">` は、利用者のロケールではなく固定の書式で定義され
ています。ここで `CultureInfo.CurrentCulture` を使うと、小数点にコンマを使うロケールでは、要素が
受け取れない値が出ます。

**これは診断しません。** 検査するには `type` を読む必要がありますが、ここでの `type` は式です。
リテラルを書いたときだけ発火する規則は、同じ誤りをある書き方では捕まえ、別の書き方では見逃し
ます。この2つには `CultureInfo.InvariantCulture` を書いてください。利用者が文章として読む値には
現在のカルチャーを使います。

### 解析できない値は元に戻る

打たれた文字を変換器が読めない場合、セッターは呼ばれず、フィールドも要素も前の値に戻ります。
これは Blazor の挙動で、`.Bind` は上に書いた DOM の再同期を通じてそこへ届きます。

意識して選ぶべき帰結が1つあります。`"oninput"` では巻き戻しがキーストロークごとに走るので、`int`
の束縛に打った小数点は残りません。`4.` が拒否され、`.` がそのまま取り去られます。数値の入力では、
たいていこれは望みではありません。

```csharp
// フォーカスが外れたときに巻き戻すので、打ちかけの数値が生き残ります。
Input.Type("number").Bind("value", "onchange", () => _amount, CultureInfo.InvariantCulture);
```

`"oninput"` が正しいのは、途中の値がどれも意味を持つときです。range のスライダーや、打てる文字
なら何でも受ける text の欄がこれにあたります。

欄を空にした場合は別の話で、これは拒否ではありません。Blazor は空文字列をその型の既定値として
読むので、`int` の束縛を空にすると前の値が残るのではなく `0` になります。値が本当に任意なら
`int?` を束縛してください。そちらは `null` を受け取ります。

### 日付には書式が要る

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

値型を1つでも束縛すると、Blazor の `BindConverter` が丸ごと保持されます。自分が束縛しない型の
変換器も残ります。トリム済みの self-contained な publish で実測すると、
`Microsoft.AspNetCore.Components.dll` のおよそ 10 KB にあたります。`string` と `bool` だけを束縛
するアプリにこの費用はかかりません。束縛の個数ではなく、一度きりの費用です。

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

## コンポーネントのパラメーターを束縛する

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
コンポーネントの型の上で探すので、`{Name}Changed` が無かったり綴りが違ったりすれば、何も束縛
しないのではなく [BCF3020](./diagnostics.md#bcf3020) を報告します。これが、要素側との非対称を、
ちぐはぐではなく規則にしています。

対象を参照渡しではなくゲッターのラムダとして書くのは、`{Name}Expression` のためです。`EditForm`
の下にあるコンポーネントは、その式から `FieldIdentifier` を解決します。この識別子が、入力を
モデルのプロパティに結び付けます。おかげで検証のメッセージが正しいフィールドに届きます。ほかの
書き方では、これを渡せません。

周りの `EditForm` も同じ書き方です。`EditForm.ChildContent` は `RenderFragment<EditContext>` で、
角括弧はこれをコンテキストを捨てて渡します。下の内容には、ほかに要るものがありません。
`EditContext` を読む内容だけ、`.Template` で名指しします。

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

束縛した入力は、角括弧の中に直接置いても、独立したコンポーネントに置いてもかまい
ません。上の束縛と、それが渡す `ValueExpression` は、どちらでも変わりません。`EditContext` を
読む書き方と、`.Param` でキャッシュしたデリゲートを渡すほうがよい場面については、
[ジェネリックなフラグメントのパラメーター](./components-and-reuse.md#ジェネリックなフラグメントのパラメーター)を
見てください。

セッターを明示する形と `async` のセッターは、こちらでも使えます。意味は要素のときと同じです。
`TValue` はカルチャーも書式も取りません。値が向かう先は DOM ではなくパラメーターで、途中で書式化
も解析もされないため、書き留めるべき選択がそもそも無いからです。

`Component<T>()` が、同じプロジェクトで宣言した `.razor` のコンポーネントを名指しできないことは
覚えておいてください（[BCF3012](./diagnostics.md#bcf3012)）。
`InputText` のようなフレームワークのコンポーネントと、手書きの C# のコンポーネントは、いつでも
解決します。

## 何が検査されるか

`.Bind` を読む診断は6つあり、どれも[リファレンス](./diagnostics.md)に項があります。ゲッターの形を
見る [BCF3017](./diagnostics.md#bcf3017)、ゲッターだけの形で代入できない相手を見る
[BCF3018](./diagnostics.md#bcf3018)、`on` の無いイベント名を見る
[BCF3019](./diagnostics.md#bcf3019)、対応する変更コールバックの無いコンポーネントを見る
[BCF3020](./diagnostics.md#bcf3020)、`.Class` と並んだ `class` の束縛を見る
[BCF3024](./diagnostics.md#bcf3024)、そして値の型に変換器の無い書式を見る
[BCF3031](./diagnostics.md#bcf3031) です。

1つの要素が `.Bind` を複数持つことはできます。そのうち2つが属性の名前かイベントの名前を共有
すれば [BCF3010](./diagnostics.md#bcf3010) で、どの装飾を2つ重ねても出るのと同じ重複です。DOM の再同期、つまり利用者が打った
文字の上に正規化した値を置き直す修復は、`value` と `checked` にだけ効きます。ブラウザーがイベント
と一緒に返してくるのが、その2つだけだからです。

## 次に読むもの

- これが組み立てられている、一方向の `.Attr` と `.On` は[要素と装飾](./elements-and-decorations.md#装飾)へ。
- `.Param` とコンポーネント周りの残りは[コンポーネントと再利用](./components-and-reuse.md)へ。
