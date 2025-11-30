---
draft: true
published: 2025-11-27
summary: あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。
---
# HTML パーサー自作で理解する mXSS (CVE-2019-16728・CVE-2020-6413 篇)

Web 標準に沿って [HTML パーサーをスクラッチ実装](https://github.com/pizzacat83/sabatora/blob/main/saba_core/src/renderer/html.rs)することを通して、mXSS などパース周りの細かい仕様を活用した XSS テクニックの原理を理解していくシリーズの第2弾。今回は、mXSS を利用した DOMPurify < 2.0.1 のバイパス ([CVE-2019-16728](https://nvd.nist.gov/vuln/detail/CVE-2019-16728)・[CVE-2020-6413](https://nvd.nist.gov/vuln/detail/CVE-2020-6413)[^two-cves]) を題材とする。

第1弾: [HTML パーサー自作で理解する Flatt Security XSS Challenge 1](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345)

ちなみに CVE-2019-16728 と CVE-2020-6413 はどちらもいずれも同一の mXSS に対する CVE であり、CVE-2019-16728 は DOMPurify に対するもの、CVE-2020-6413 は Chrome に対するものである。というのも、DOMPurify は HTML のパース・シリアライズを自身で実装せず、ブラウザの API を利用している。そのため、パース・シリアライズの振る舞いに起因するこの mXSS は、DOMPurify と Chrome 両方に対して脆弱性報告がなされた。その結果、HTML Standard における構文解析の仕様を改訂するにまで至った。今回の脆弱性による影響を以下にまとめる。

- DOMPurify: 2019/09 リリースの 2.0.1 ([diff](https://github.com/cure53/DOMPurify/compare/2.0.0...2.0.1)) にてサニタイズを追加。ある要素の属性値中に `</` が出現する場合はその要素ごと削除するというもの[^dompurify-aggressive-sanitization]。
- Chrome: 2020/02 リリースの 80.0.3987.87 にて HTML パーサーの実装を修正 ([issue](https://issues.chromium.org/issues/40050167))。下記 HTML Standard の改訂に先行するもの。
- HTML Standard: 2021/06 に、構文解析の仕様を修正

[^dompurify-aggressive-sanitization]: `</` を含む属性値を扱いたいというニーズは無いわけではなかったようで、サニタイズを緩和して欲しい旨の [Issue](https://github.com/cure53/DOMPurify/issues/369) が立てられた。`<svg>`, `<math>` を禁止することでも本件の mXSS を防げることから、DOMPurify の設定において `<svg>`, `<math>` が禁止されていない場合のみ、`</` を含む属性値をサニタイズするようになった。ユーザー視点からすれば、patch update である 2.0.0 → 2.0.1 によって既存のワークロードが正常に動作しなくなったのであるが、これは実に息が苦しくなる話である。どう困った話であるかというと、「DOMPurify をアプデすると、今まで動いていたものが正しく動かなくなるかもしれない」という不安が、patch update にすら付きまとうのである。最新の攻撃手法に対策するためにはサニタイザを常に最新に保つことが重要であるのにもかかわらず、アップデートに対する抗力が存在するこの構造が悲しい。なんとかしなければならない。そもそも信頼できない HTML を扱う必要性をなくせるならそれが一番だし、どうしても必要な場合は、DOMPurify をいつでもアップデートできる体制を整えるべきだ。サニタイザの挙動がどう変化しても問題ないなら、心置きなく DOMPurify をアップデートできる。「〇〇が除去されると困る」のような要求があるならば、「DOMPurify が〇〇を除去しない」というテストコードを用意しておけば、DOMPurify のアップデートに対する受け入れ可能性は比較的容易に判定できるだろう。DOMPurify のアップデートに対する受け入れ可能条件の言語化だけでも、導入時にやっておくべきだ。

というわけで今回の脆弱性は HTML Standard の改訂や Chrome の修正によって解決されたため、**現代のブラウザで再現することはできない**。しかし古い仕様を参照して HTML パーサーを実装すれば再現できる。これは自作 HTML パーサーの強みの一つかもしれない (?)

## 脆弱性の概要

本稿が題材とする脆弱性は、DOMPurify のバイパスである。以下のコードはユーザー入力 `dangerousHTML` を DOMPurify でサニタイズした上で `innerHTML` に代入する。DOMPurify は XSS を防ぐため、`<script>` タグやイベントハンドラ `onerror="..."` などの危険なものを除去する。DOMPurify に脆弱性がないと仮定すると、攻撃者がどのような `dangerousHTML` を与えようが、JavaScript 実行につながるものが悉く除去された `safeHTML` が出力され、XSS は起きないはずである。

```js
function safelySetInnerHTML(dangerousHTML) {
  const safeHTML = DOMPurify.sanitize(dangerousHTML)
  document.body.innerHTML = safeHTML
}
```

しかし `dangerousHTML` として以下の文字列を与えると、(当時の Chrome と DOMPurify 2.0.0 以前において、)`alert(1)` が実行されてしまうのだ。

```
<svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
```

## 振る舞いを観察する

どうしてサニタイザをすり抜けるように XSS を起こせてしまったのか。まずはパーサーの実装に深入りせずに、外形的に振る舞いを見てみよう。「どうしてそうなるの？」と思うところはいくつもあるが、それらは後で実装・仕様を深掘りするので安心して心に留めておいてほしい。

今回の脆弱性報告を受けて HTML パーサーの仕様・実装が修正されたため、お手持ちのブラウザでこの振る舞いを再現することはできないが、修正前の仕様に基づくパーサーの挙動を手軽に観察するには、parser5 の古いバージョン (< 7.0.0) を使うのが良いだろう。[AST Explorer](http://astexplorer.net/#/1CHlCXc4n4) (parse5 6.0.0)を片手に読み進めていくことを推奨する。


DOMPurify は、大まかに以下の流れでサニタイズを行う。

1. ブラウザの API ([`DOMParser`](http://developer.mozilla.org/ja/docs/Web/API/DOMParser) 等) を利用して、テキストから DOM ツリーを得る ([実装](https://github.com/cure53/DOMPurify/blob/4c8ca9db5b4b2a79ed6c779ac6f22587ba16a3e1/src/purify.js#L461-L549)))
2. DOM ツリーを解析し、危険な要素・属性の削除などを行い、無害化された DOM ツリーを構築する
3. ブラウザの API (`.innerHTML` 等) を利用して、無害化された DOM ツリーを文字列に変換する ([実装](https://github.com/cure53/DOMPurify/blob/4c8ca9db5b4b2a79ed6c779ac6f22587ba16a3e1/src/purify.js#L1113))

これを踏まえ、先述のペイロードがどのように処理されていくかを見ていこう。

```
<svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
```

このペイロードには2点、奇妙な部分がある。
- 閉じタグ `</p>` が突如出てくる
- `<a>` タグの `id` 属性に `</style>` が出現する
これらの振る舞いに注目しながら処理の流れを追っていく。

まずこの HTML をパースすると以下の DOM ツリーが得られる ([AST Explorer](https://astexplorer.net/#/gist/10fcc967e9361171c97dbb4c0ec6ad2c/83ccf1216469343b2672ea57d4025f2e470c3f41)):

- `svg`
	- `p`
	- `style`
		- `a` `id="</style><img src=1 onerror=alert(1)>"`

閉じタグ `</p>` は、空の `p` 要素となった。そして `</style>` は、`a` タグの属性値の一部として解釈された。

この DOM ツリーに危険なものは含まれないため、DOMPurify はこの DOM ツリーをそのままシリアライズする。この DOM ツリーの中の `<img src=1 onerror=alert(1)>` は、単なる `id` 属性中の文字列であり、何ら危険な作用を持たないのだ。

シリアライズした結果、すなわち DOMPurify の出力 `safeHTML` は以下のようになる (読みやすさのために改行・インデントを追加)。

```
<svg>
	<p></p>
	<style>
		<a id="</style><img src=1 onerror=alert(1)>"></a>
	</style>
</svg>
```

最初のペイロードからの変化は以下の通り:

- 閉じタグ `</p>` の前に開始タグが挿入された
- `a`, `style`, `svg` の終了タグが挿入された

最後に `document.body.innerHTML = safeHTML` を実行すると `alert(1)` が実行されてしまう。というのも、ブラウザが上記の HTML をパースすると、以下の DOM ツリーが構築されてしまうのだ ([AST Explorer](https://astexplorer.net/#/gist/4e4111b42c7263b45dd97fcf97f2007f/0eb7e80bbd6e12a0d6e577ea94b7c68a531f3328))。

- `svg`
- `p`
- `style`
	- \#text: `<a id="`
- `img` `src=1` `onerror=alert(1)`
- \#text: `">`

奇妙なことがいくつか起きている。

- `svg` の子要素であったはずの `p`, `style` が `svg` の兄弟となった
- `<style><a id="</style><img src=1 onerror=alert(1)>">` において、`</style>` は「`a` タグの `id` 属性の一部」ではなく、「`style` の閉じタグ」として解釈されるようになった
	- その結果、「`style` タグの中に `a` タグがあり、その `id` 属性が `"</style><img src=1 onerror=alert(1)>"` ではなく、「`<style><a id="</style>`」「`<img src=1 onerror=alert(1)>`」「`">`」と解釈された

このようにして、DOMPurify が安全と判断した DOM ツリーとは異なる DOM ツリーが構築され、危険なイベントハンドラ `onerror` が出現してしまった。これにより、`alert(1)` が実行されてしまう。

## 奇妙な振る舞いを分解し、仕様と突き合わせる

上記のペイロードの振る舞いは、HTML の様々な仕様を組み合わせてできたものである。これをパーツごとに分解し、仕様を読んで原理を理解しよう。

### SVG 内外での `<style>` に対するパーサーの挙動

先述のペイロードの部分文字列である `<style><a id="</style><img src=1 onerror=alert(1)>">` は、SVG の中にあるか否かでパース結果が異なる。本質は `</style>` の解釈の違いであり、結論から言えば SVG の外では「`style` の閉じタグ」と見なされ、SVG の中では「`a` タグの `id` 属性の一部」と見なされる。

AST Explorer で見てみよう。

まずは SVG に包まれていない場合。

```
<style><a id="</style><img src=1 onerror=alert(1)>">
```

このとき `</style>` は `style` の閉じタグとして解釈され、([AST Explorer](https://astexplorer.net/#/gist/be7fa874975f80974710ecaa27b76760/9f1013c46ca4ab920feafcf07d671baa7a80a48b)):

- `style`  
	- \#text: `<a id="`
- `img` `src=1` `onerror=alert(1)`
- \#text: `">`



### SVG の中の `p` タグの振る舞い

以下のように、`svg` の中に `p` タグを含めた HTML を考える。

```
<svg><p></p></svg>
```

これをパースした DOM ツリーがこちら ([AST Explorer](https://astexplorer.net/#/gist/20700561e08e8b397ea410fe4ffb97b0/e530f187ef7090eae8bda0f4555b19b373504534))。

- `svg`
- `p`

`svg` の中に `p` があったはずが、兄弟となっている。

`p` の後にコンテンツを追加すると、`p` の後に続くコンテンツも `svg` から "出される"。

```
<svg><p></p>aaa</svg>
```

パース結果:
- `svg`
- `p`
- \#text: `aaa`

この振る舞いは一見、「`p` タグ以降のコンテンツが `svg` から抜き出される」というものに見えるかもしれないが、仕様を読むとより正確なメンタルモデルが得られる。このメンタルモデルとは、「SVG の中に `<p>` が出現すると、その直前で SVG が閉じられたものとみなす」というものである。

つまり、`<svg><p></p>aaa</svg>` の `<p></p>aaa` が「後ろに移動して」`<svg></svg> <p></p>aaa` になったわけではない。`<p>` の直前に `</svg>` が隠れていると解釈され、`<svg></svg><p></p>aaa</svg>` のように見なされたのだ。ここで、末尾の `</svg>` は無効な閉じタグとして無視される。


## 修正を理解する