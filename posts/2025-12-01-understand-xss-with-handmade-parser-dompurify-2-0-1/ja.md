---
published: 2025-12-01
summary: Mutation XSS を利用した DOMPurify < 2.0.1 のバイパス (CVE-2019-16728・CVE-2020-6413) のメカニズムを、HTML パーサーの仕様・実装を読み解きながら理解する。How よりも Why に焦点を当てた解説の試み。
head: |
  <meta property="og:image" content="https://blog.pizzacat83.com/ja/posts/2025-12-01-understand-xss-with-handmade-parser-dompurify-2-0-1/eyecatch.png">
  <meta name="twitter:card" content="summary_large_image">
---
# HTML パーサー自作で理解する mXSS (CVE-2019-16728・CVE-2020-6413 篇)

Web 標準に沿って [HTML パーサーをスクラッチ実装](https://github.com/pizzacat83/sabatora/blob/main/saba_core/src/renderer/html.rs)することを通して、パース周りの細かい仕様を活用した XSS テクニックの原理を理解していくシリーズの第2弾。今回は、mutation XSS (mXSS) を利用した DOMPurify < 2.0.1 のバイパス ([CVE-2019-16728](https://nvd.nist.gov/vuln/detail/CVE-2019-16728)・[CVE-2020-6413](https://nvd.nist.gov/vuln/detail/CVE-2020-6413)) を題材とする。HTML パーサーの仕様・実装に基づいてメカニズムを理解していくので、how だけでなく why の理解も深められるだろう。

第1弾: [HTML パーサー自作で理解する Flatt Security XSS Challenge 1](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345) (ここから10ヶ月。ﾋｴｯ)

ちなみに CVE-2019-16728 と CVE-2020-6413 はどちらもいずれも同一の mXSS に対する CVE であり、CVE-2019-16728 は DOMPurify に対するもの、CVE-2020-6413 は Chrome に対するものである。というのも、DOMPurify は HTML のパース・シリアライズを自身で実装せず、ブラウザの API を利用している。そのため、パース・シリアライズの振る舞いに起因するこの mXSS は、DOMPurify と Chrome 両方に対して脆弱性報告がなされた。その結果、HTML Standard における構文解析の仕様を改訂するにまで至った。今回の脆弱性による影響を以下にまとめる。

- DOMPurify: 2019/09 リリースの 2.0.1 ([diff](https://github.com/cure53/DOMPurify/compare/2.0.0...2.0.1)) にてサニタイズを追加。ある要素の属性値中に `</` が出現する場合はその要素ごと削除するというもの。
- Chrome: 2020/02 リリースの 80.0.3987.87 にて HTML パーサーの実装を修正 ([issue](https://issues.chromium.org/issues/40050167), [diff](https://chromium.googlesource.com/chromium/src.git/+/d16226271d4d501de19f019aba1c145930b45503))。下記 HTML Standard の改訂に先行するもの。
- HTML Standard: 2021/06 ([issue](https://github.com/whatwg/html/issues/5113), [PR](https://github.com/whatwg/html/pull/6736))に、構文解析の仕様を修正

というわけで今回の脆弱性は HTML Standard の改訂や Chrome の修正によって解決されたため、**現代のブラウザで再現することはできない**。しかし古い仕様を参照して HTML パーサーを実装すれば再現できる。これは自作 HTML パーサーの強みの一つかもしれない (?)

## 脆弱性の概要

本稿が題材とする脆弱性は、DOMPurify のバイパスである。以下のコードはユーザー入力 `dangerousHTML` を DOMPurify でサニタイズした上で `innerHTML` に代入する。DOMPurify は XSS を防ぐため、`<script>` タグやイベントハンドラ `onerror="..."` などの危険なものを除去する。DOMPurify に脆弱性がないと仮定すると、攻撃者がどのような `dangerousHTML` を与えようが、JavaScript 実行につながるものが悉く除去された `safeHTML` が出力され、XSS は起きないはずである。

```js
function safelySetInnerHTML(dangerousHTML) {
  const safeHTML = DOMPurify.sanitize(dangerousHTML)
  document.body.innerHTML = safeHTML
}
```

しかし `dangerousHTML` として以下の文字列を与えると、(当時の Chrome と DOMPurify 2.0.0 以前において、) `alert(1)` が実行されてしまうのだ。

```
<svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
```

## 振る舞いを観察する

どうしてサニタイザをすり抜けるように XSS を起こせてしまったのか。まずはパーサーの実装に深入りせずに、外形的に振る舞いを見てみよう。「どうしてそうなるの？」と思うところはいくつもあるが、それらは後で実装・仕様を深掘りするので安心して心に留めておいてほしい。

今回の脆弱性報告を受けて HTML パーサーの仕様・実装が修正されたため、お手持ちのブラウザでこの振る舞いを再現することはできないが、修正前の仕様に基づくパーサーの挙動を手軽に観察するには、parser5 の古いバージョン (< 7.0.0) を使うのが良いだろう。[AST Explorer](http://astexplorer.net/#/1CHlCXc4n4) (parse5 6.0.0)を片手に読み進めていくことを推奨する。

もちろん私の自作 HTML パーサーで以下の振る舞いを観察することもできるが、まあ手軽なのは環境構築不要の AST Explorer だろう。もし私の自作 HTML パーサーで観察してみたいならば、以下のコマンドで試せる。なお私の HTML パーサーはまだ HTML Standard 完全準拠ではなく、入力 HTML によってはパーサーが panic する場合があることを了承いただきたい。

```sh
git clone --depth 1 --branch blog-mxss-dompurify-2-0-1 https://github.com/pizzacat83/sabatora.git # main ブランチは最新の HTML Standard 準拠を目指しているため、代わりに古い仕様のパーサーがあるタグをチェックアウトする
cargo test --package saba_core --lib --renderer::html::parser::tests::test_cve_2020_6413 --exact --show-output
```

本題に戻ると、DOMPurify は、大まかに以下の流れでサニタイズを行う。

1. ブラウザの API ([`DOMParser`](http://developer.mozilla.org/ja/docs/Web/API/DOMParser) 等) を利用して、テキストから DOM ツリーを得る ([実装](https://github.com/cure53/DOMPurify/blob/4c8ca9db5b4b2a79ed6c779ac6f22587ba16a3e1/src/purify.js#L461-L549))
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
		- `a` `id`: `</style><img src=1 onerror=alert(1)>`

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
- `img` `src`: `1` `onerror`: `alert(1)`
- \#text: `">`

奇妙なことがいくつか起きている。

- `svg` の子要素であったはずの `p`, `style` が `svg` の兄弟となった
- `<style><a id="</style><img src=1 onerror=alert(1)>">` において、`</style>` は「`a` タグの `id` 属性の一部」ではなく、「`style` の閉じタグ」として解釈されるようになった
	- その結果、「`style` タグの中に `a` タグがあり、その `id` 属性が `</style><img src=1 onerror=alert(1)>` ではなく、「`<style><a id="</style>`」「`<img src=1 onerror=alert(1)>`」「`">`」と解釈された

このようにして、DOMPurify が安全と判断した DOM ツリーとは異なる DOM ツリーが構築され、危険なイベントハンドラ `onerror` が出現してしまった。これにより、`alert(1)` が実行されてしまう。

このように、HTML のシリアライズ・パースを経て DOM 構造が変化 (mutation) してしまうことを利用した XSS のテクニックが mutation XSS (mXSS) と呼ばれる。

でも、どうしてこんなことに？？？

## 奇妙な振る舞いを分解し、仕様と突き合わせる

上記のペイロードの振る舞いは、HTML の様々な仕様を組み合わせてできたものである。これをパーツごとに分解し、仕様を読んで原理を理解しよう。

### SVG 内外での `<style>` に対するパーサーの挙動

先述のペイロードの部分文字列である `<style><a id="</style><img src=1 onerror=alert(1)>">` は、SVG の中にあるか否かでパース結果が異なる。本質は `</style>` の解釈の違いであり、結論から言えば SVG の外では「`style` の閉じタグ」と見なされ、SVG の中では「`a` タグの `id` 属性の一部」と見なされる。

では早速、AST Explorer で見てみよう。

まずは SVG に包まれていない場合。

```
<style><a id="</style><img src=1 onerror=alert(1)>">
```

このとき `</style>` は `style` の閉じタグとして解釈され、後続の文字列が `img` タグとして解釈される ([AST Explorer](https://astexplorer.net/#/gist/be7fa874975f80974710ecaa27b76760/9f1013c46ca4ab920feafcf07d671baa7a80a48b))。

- `style`  
	- \#text: `<a id="`
- `img` `src`: `1` `onerror`: `alert(1)`
- \#text: `">`

次に、SVG の中にある場合。

```
<svg><style><a id="</style><img src=1 onerror=alert(1)>"></svg>
```

この `</style>` は `a` タグの `id` 属性として解釈される ([AST Explorer](https://astexplorer.net/#/gist/809898651ef83ef5348a15a0d1076cb7/76ed5f463d8693c35e5f87c357a46f7ced1c4c96))。

- `svg`
	- `style`
		- `a` `id`: `</style><img src=1 onerror=alert(1)>`

どうして SVG の内外どちらにあるかによって `</style>` の解釈が異なるのか。パーサーの実装に入る前に、まずは宣言的な仕様の観点から説明する。

SVG の内外にある `<style>` は文字列としては同一だが、それが指し示す要素は別物である。SVG の外にある `<style>` は [HTML 名前空間の `style` 要素](https://developer.mozilla.org/ja/docs/Web/HTML/Reference/Elements/style)を指し、SVG の内部にある `<style>` は [SVG 名前空間の `style` 要素](https://developer.mozilla.org/ja/docs/Web/SVG/Reference/Element/style)を指す。これらは、element kind も異なる。

|                  | 名前空間 |                                       Element kind                                        |
| :--------------: | :--: | :---------------------------------------------------------------------------------------: |
| SVG 外の `<style>` | HTML | [raw text elements](https://html.spec.whatwg.org/multipage/syntax.html#raw-text-elements) |
| SVG 内の `<style>` | SVG  |  [foreign elements](https://html.spec.whatwg.org/multipage/syntax.html#foreign-elements)  |
それぞれの element kind のコンテンツについて、HTML Standard は以下のように規定している。

> Raw text elements can have text, though it has restrictions described below.
> ([HTML Standard 13.1.2](https://html.spec.whatwg.org/multipage/syntax.html#foreign-elements:~:text=Raw%20text%20elements%20can%20have%20text%2C%20though%20it%20has%20restrictions%20described%20below.))
>
> The text in raw text and escapable raw text elements must not contain any occurrences of the string "`</`" (U+003C LESS-THAN SIGN, U+002F SOLIDUS) followed by characters that case-insensitively match the tag name of the element followed by one of U+0009 CHARACTER TABULATION (tab), U+000A LINE FEED (LF), U+000C FORM FEED (FF), U+000D CARRIAGE RETURN (CR), U+0020 SPACE, U+003E GREATER-THAN SIGN (`>`), or U+002F SOLIDUS (`/`).
> 
> ([HTML Standard 13.1.2.6](https://html.spec.whatwg.org/multipage/syntax.html#cdata-RAWTEXT-restrictions))

<!-- -->

> Foreign elements (中略) can have text, character references, CDATA sections, **other elements**, and comments, but the text must not contain the character U+003C LESS-THAN SIGN (<) or an ambiguous ampersand.
> 
> ([HTML Standard 13.1.2](https://html.spec.whatwg.org/multipage/syntax.html#foreign-elements:~:text=Foreign%20elements%20whose%20start%20tag%20is%20not%20marked%20as%20self%2Dclosing%20can%20have%20text%2C%20character%20references%2C%20CDATA%20sections%2C%20other%20elements%2C%20and%20comments%2C%20but%20the%20text%20must%20not%20contain%20the%20character%20U%2B003C%20LESS%2DTHAN%20SIGN%20(%3C)%20or%20an%20ambiguous%20ampersand.), 強調は引用者による)

すなわち、HTML 名前空間の `<style>` 要素は、内部にテキストを持てるが子要素は持てない。`<a id="` の部分は子要素ではなく単なるテキストコンテンツとして解釈され、`</style>` によって `style` 要素が閉じられた。

一方で、SVG 名前空間の `<style>` 要素は、内部にテキストだけでなく子要素を持つことができる。`<a id="...` の部分は、子の `a` 要素として解釈されたのだ。

直感的には、HTML 名前空間の `style` 要素に対するパーサーの挙動は (`div` など一般的なタグと比べて) 風変わりで、SVG 名前空間の `style` 要素に対するパーサーの挙動は見慣れた感じがする。確かに `style` 要素の中に子要素があっても扱いに困るのだが[^children-elements-of-svg-style]。

[^children-elements-of-svg-style]: なお SVG 名前空間の `style` 要素のコンテンツモデルは character data であり、`style` に子要素を持たせるのはここでも御法度ではある。

ではパーサーの仕様を見ていこう。「SVG の内外どちらにあるかによって、`</style>` 周りのパース結果が異なる」という振る舞いは、どのように仕様で規定されているのか？

まずはパース処理の全体像を復習する。パース処理の仕様は HTML Living Standard [13.2 Parsing HTML documents](https://html.spec.whatwg.org/multipage/parsing.html) に定義されている。パースの主な処理は、tokenization stage と tree construction stage に分けられる。Tokenization stage は文字列をトークンの列に変換し、tree construction stage はトークンの列から DOM を構築する。どちらの stage もそれぞれ state machine を持っていて、文字やトークンを消費して自身の状態を変える。なお、tree construction stage は自身の状態だけでなく、tokenization stage の状態を変更することもあることに注意が必要だ。大域的には、2つの stage を独立に捉えることはできない。

さて、SVG の内外によって処理が分岐するのは、tree construction stage の仕様の冒頭である。

> As each token is emitted from the tokenizer, the user agent must follow the appropriate steps from the following list, known as the tree construction dispatcher:
> 
> - (中略) If the adjusted current node is an element **in the HTML namespace** (中略)
>     - Process the token according to the rules given in the section corresponding to the current insertion mode in HTML content.
> - **Otherwise**
> 	- Process the token according to the rules given in the section for parsing tokens in foreign content.
> 
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#tree-construction-dispatcher), 強調は引用者による)

これは、tokenization stage が出力したトークンをどのように処理するかに関する規定である。

ここで adjusted current node とはほとんどの場合、まだ閉じていないタグのうち最も内側のものを指す。つまり、今 HTML 名前空間の要素の中にいるならば1つ目の規則、それ以外 (SVG 空間の要素も該当) ならば2つ目の規則が適用される。なお、前者の ["the section corresponding to the current insertion mode in HTML content"](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhtml) は1600行ほどあり、後者の ["the section for parsing tokens in foreign content"](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign) は120行程度であることから、前者の方に魔境の雰囲気が漂う。

まずは HTML 名前空間の要素の中にいる場合の仕様を見ていこう。構文解析器は 21 種類の「挿入モード (insertion mode)」を遷移しながらトークンを処理していく。挿入モードはたくさんあるが、`<style>` 開始タグが出現した際は大抵色々たらい回しにされたのちに "in head" mode に辿り着くので、ここを読めば良い。(HTML パーサーの実装が手元にある人は、デバッガのステップ実行等で状態遷移の流れも見てみると簡単に流れを追えるだろう。HTML パーサーの実装とは、型推論・コードジャンプ・ステップ実行ができるようになった HTML パーサーの仕様書である (?))

> 13.2.6.4.4 The "in head" insertion mode
> 
> When the user agent is to apply the rules for the "in head" insertion mode, the user agent must handle the token as follows:
> 
> - (略; タグの名前に基づく大量の分岐)
> - A start tag whose tag name is one of: "noframes", "style"
>     - Follow the generic raw text element parsing algorithm.
> - (略)
> 
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#current-node:~:text=A%20start%20tag%20whose%20tag%20name%20is%20one%20of%3A%20%22noframes%22%2C%20%22style%22))

ここで、 "the generic raw text element parsing algorithm" は以下のように規定されている。

> The generic raw text element parsing algorithm (略) consist of the following steps. (略)
> 
> 1. Insert an HTML element for the token.
> 2. (中略) **switch the tokenizer to the RAWTEXT state**; (略)
> 3. Set the original insertion mode to the current insertion mode.
> 4. Then, switch the insertion mode to "text".
> 
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#generic-raw-text-element-parsing-algorithm), 強調は引用者による)

なんとここで、構文解析器が字句解析器の状態を上書きするのだ！

この RAWTEXT state はざっくり言えば、「対応する閉じタグ `</style>` が出現するまでは全部テキストとして扱う」ものである。実はこれが、`<a id="</style>...` のパース結果をもたらす根源である。RAWTEXT state の具体的な仕様と挙動については、SVG 名前空間の場合のパース仕様を眺めた後に深掘りしていこう。

というわけで今度は、SVG 名前空間の要素の中にいる場合に、`<style>` 開始タグがどう処理されるかを見ていく。件の "the section for parsing tokens in foreign content" には以下のように定義されている。

> 13.2.6.5 The rules for parsing tokens in foreign content
> 
> When the user agent is to apply the rules for parsing tokens in foreign content, the user agent must handle the token as follows:
> 
> - (略)
> - Any other start tag
> 	- (略)
> 	- Insert a foreign element for the token, with the adjusted current node's namespace and false.
> 	- (略)
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign))

なんと、DOM ツリーに `style` 要素を挿入するだけである。字句解析器の状態を変えるような、変わったことは特にしない。HTML パーサーを実装してきた人はみなこう言うだろう。「なんてシンプルなんだ！」

ここまでをまとめると、`<style>` が SVG の内外どちらにあるかによって、以下の違いが生じることが分かった。

- SVG の外にある場合: 構文解析器が字句解析器の状態を RAWTEXT に上書きする
- SVG の中にある場合: 構文解析器は字句解析器の状態を変えない
	- (大抵の場合、字句解析器は Data state の状態にある)

(第1弾を読んだ方なら既視感が湧いているかもしれない。実際、ここからの流れは第1弾とかなり近しい。)

さて、この字句解析器の状態の差異が `<a id="</style><img src=1 onerror=alert(1)>">` のパースにどのような影響を与えるのかを見ていこう。

まずは変哲のない方である、Data state の場合 (つまり SVG の中に `<style>` があるケース) について見ていこう。`<a id="</style><img src=1 onerror=alert(1)>">` を字句解析すると、状態を以下のように遷移させながらトークンを出力していく。

| state                           | input                                  | next state                      | emit                                                                                     | draft token                                                                              | note                        |
| ------------------------------- | -------------------------------------- | ------------------------------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- | --------------------------- |
| data                            | `<`                                    | tag open                        |                                                                                          |                                                                                          |                             |
| tag open                        | `a`                                    | tag name                        |                                                                                          | start tag                                                                                | reconsume                   |
| tag name                        | `a`                                    | tag name                        |                                                                                          | start tag<br>name: `a`                                                                   |                             |
| tag name                        | <code>&nbsp;</code>                    | before attribute name           |                                                                                          | start tag<br>name: `a`                                                                   |                             |
| before attribute name           | `i`                                    | attribute name                  |                                                                                          | start tag<br>name: `a`                                                                   | reconsume                   |
| attribute name                  | `i`                                    | attribute name                  |                                                                                          | start tag<br>name: `a`<br>attributes:<br>- `i`                                           |                             |
| attribute name                  | `d`                                    | attribute name                  |                                                                                          | start tag<br>name: `p`<br>attributes:<br>- `id`                                          |                             |
| attribute name                  | `=`                                    | before attribute value          |                                                                                          | start tag<br>name: `a`<br>attributes:<br>- `id`                                          |                             |
| before attribute value          | `"`                                    | attribute value (double-quoted) |                                                                                          | start tag<br>name: `a`<br>attributes:<br>- `id`                                          |                             |
| attribute value (double-quoted) | `</style><img src=1 onerror=alert(1)>` | attribute value (double-quoted) |                                                                                          | start tag<br>name: `a`<br>attributes:<br>- `id` : `</style><img src=1 onerror=alert(1)>` | どの文字も同じ遷移をするので、この表では1行にまとめた |
| attribute value (double-quoted) | `"`                                    | after attribute value (quoted)  |                                                                                          | start tag<br>name: `a`<br>attributes:<br>- `id` : `</style><img src=1 onerror=alert(1)>` |                             |
| after attribute value (quoted)  | `>`                                    | data                            | start tag<br>name: `a`<br>attributes:<br>- `id` : `</style><img src=1 onerror=alert(1)>` |                                                                                          |                             |
字句解析器は `<a id="...` の部分について、まず `<` を読んでタグの始まりを認識し、tag open state に遷移する。そして `a` を読んで開始タグのタグ名部分を認識し、空白文字まで読んでタグ名の終了を認識する。その後 `id` 属性の始まりが認識されて attribute value (double-quoted) state に遷移するが、属性値からの脱出につながる文字は `"` だけであるから、`</style><img src=1 onerror=alert(1)>` 全体が `id` の属性値と認識される。というわけで、「`id` 属性が `</style><img src=1 onerror=alert(1)>` である `a` 開始タグ」というトークンが出力される。

今度は RAWTEXT state (つまり HTML 名前空間の `<style>` の直後) から始まる場合の状態遷移を見ていく。

| state                  | input               | next state             | emit                    | draft token              | note                        |
| ---------------------- | ------------------- | ---------------------- | ----------------------- | ------------------------ | --------------------------- |
| RAWTEXT                | `<`                 | RAWTEXT less-than sign |                         |                          |                             |
| RAWTEXT less-than sign | `a`                 | RAWTEXT                | char `<`                |                          | reconsume                   |
| RAWTEXT                | `a`                 | RAWTEXT                | char `a`                |                          |                             |
| RAWTEXT                | <code>&nbsp;</code> | RAWTEXT                | char ` `                |                          |                             |
| RAWTEXT                | `i`                 | RAWTEXT                | char `i`                |                          |                             |
| RAWTEXT                | `d`                 | RAWTEXT                | char `d`                |                          |                             |
| RAWTEXT                | `=`                 | RAWTEXT                | char `=`                |                          |                             |
| RAWTEXT                | `"`                 | RAWTEXT                | char `"`                |                          |                             |
| RAWTEXT                | `<`                 | RAWTEXT less-than sign |                         |                          |                             |
| RAWTEXT less-than sign | `/`                 | RAWTEXT end tag open   |                         |                          |                             |
| RAWTEXT end tag open   | `s`                 | RAWTEXT end tag name   |                         |                          | reconsume                   |
| RAWTEXT end tag name   | `s`                 | RAWTEXT end tag name   |                         | end tag<br>name: `s`     |                             |
| RAWTEXT end tag name   | `t` `y` `l` `e`     | RAWTEXT end tag name   |                         | end tag<br>name: `style` | どの文字も同じ遷移をするので、この表では1行にまとめた |
| RAWTEXT end tag name   | `>`                 | data                   | end tag<br>tag: `style` |                          |                             |
RAWDATA 系の状態では、直近の開始タグ `<style>` に対応する閉じタグ `</style>` が登場するまでは、全てが単なる文字を表すトークンとして認識される。特に、`<a id="...` の字句解析では、まず先頭にある `<` は `</style>` の先端かもしれないので、RAWTEXT less-than sign という状態に遷移し `/` を待ち受ける。しかしその次の文字は `/` ではなく `a` だったので、先ほどの `<` は単なる文字だったのだと文字トークン `<` を出力し、RAWTEXT 状態に戻ってしまう。その後の <code>&nbsp;id=&quot;</code> も単なる文字の並びとして扱われる。そして `</style>` に到達し、これが閉じタグとして出力されるのである。

というわけで、以下の HTML は SVG の内外どちらにあるかによって、「`style` タグの中に `a` タグ」「`style` タグの兄弟となる `img` タグ」という異なるパース結果になるのだ。

```
<style><a id="</style><img src=1 onerror=alert(1)>">
```

### SVG の中の `p` タグの振る舞い

今回の mXSS におけるもう一つの鍵が、SVG の中にある `p` タグの挙動である。

以下のように、`svg` の中に `p` タグを含めた HTML を考える。

```
<svg><p></p></svg>
```

これをパースした DOM ツリーは以下 ([AST Explorer](https://astexplorer.net/#/gist/20700561e08e8b397ea410fe4ffb97b0/e530f187ef7090eae8bda0f4555b19b373504534)):
- `svg`
- `p`

`svg` の中に `p` があったはずが、兄弟となっている。

`p` の後にコンテンツを追加すると、`p` の後に続くコンテンツまでもが `svg` から "出される"。

```
<svg><p></p>aaa</svg>
```

パース結果 ([AST Explorer](https://astexplorer.net/#/gist/63c3741ff1c80126fe2e7da13d482351/5065e892682a9426ca91d3ab0a262315b6cf4da8)):

- `svg`
- `p`
- \#text: `aaa`

この振る舞いは一見、「`p` タグ以降のコンテンツが `svg` の外に移動する」というものに見えるかもしれないが、仕様を読むとより正確なメンタルモデルが得られる。そのメンタルモデルとは、「SVG の中に `<p>` が出現すると、その直前で SVG が閉じられたものとみなす」というものである。

つまり、`<svg><p></p>aaa</svg>` の `<p></p>aaa` が「後ろに移動して」`<svg></svg> <p></p>aaa` になったわけではない。`<p>` の直前に `</svg>` が隠れていると解釈され、`<svg></svg><p></p>aaa</svg>` のように見なされたのだ。ここで、末尾の `</svg>` は無効な閉じタグとして無視される。

![](p-in-svg.png)

なぜ`<p>` の前で `svg` 要素が暗黙的に閉じられるかというと、これは恐らく、「SVG の仕様に存在しないが HTML に存在するタグが出現した場合に、『その直前で SVG を閉じ忘れた』のだと推定してパースを続行する」という意図で制定された仕様ではないかと思う。[SVG の仕様](https://www.w3.org/TR/SVG/struct.html#SVGElement)を読むと、`svg` 要素のコンテンツモデルには `a` 要素など限られた要素のみが含まれており、確かに `p` 要素は `svg` 要素の子になれない (というより、SVG 名前空間において `p` という要素は定義されていない)。なお、この仕様の詳細は少し後で深掘りしていく。

ちなみに同様の振る舞いは、閉じタグ `</p>` を削除しても発生する。

```
<svg><p></svg>
```

パース結果 ([AST Explorer](https://astexplorer.net/#/gist/7e07aa759d2977f5ab96ed848dcb9f75/ee098656ed6b73afc1adcf1d4c0d6a8ed645f8f5)):

- `svg`
- `p`

一方で、開始タグ `<p>` を削除し、閉じタグ `</p>` だけを `<svg>` の中に入れるとどうなるだろう。

```
<svg></p>aaa</svg>
```

パース結果 ([AST Explorer](https://astexplorer.net/#/gist/5477f818981d4b05ae777dcce895c5cf/0d9280dbea160231c6c3fd5b733593e2dc2a8e4a)):

- `svg`
	- `p`
	- \#text: `aaa`

なんと、閉じタグだけを `<svg>` の中に入れると、これまでの結果とは違い、`p` は `svg` 要素の兄弟ではなく子要素となる。このときのメンタルモデルは、「閉じタグ `</p>` は、開始タグを書き忘れた空の`p` 要素とみなされる」というものである。

この DOM ツリーをシリアライズすると、以下の文字列が得られる。

```
<svg><p></p>aaa</svg>
```

さらにこれをパースすると、もちろん今度は `p` が `svg` の兄弟となる ([AST Explorer](https://astexplorer.net/#/gist/63c3741ff1c80126fe2e7da13d482351/5065e892682a9426ca91d3ab0a262315b6cf4da8)):

- `svg`
- `p`
- \#text: `aaa`

というわけで、これはシリアライズとパースを繰り返すと DOM ツリーの構造が変化する不思議な HTML なのであった。

では、どうして SVG 内の `p` タグに対してパーサーがこのように振る舞うのか、パーサーの仕様を見ていこう。

まずは、開始タグ`<p>` が登場する場合 (`<svg><p></p>aaa</svg>` など) の仕様を見ていく。今は SVG 名前空間の中にいるので、["the section for parsing tokens in foreign content"](https://web.archive.org/web/20210622212326/https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign) に従う。

> 13.2.6.5 The rules for parsing tokens in foreign content
> 
> When the user agent is to apply the rules for parsing tokens in foreign content, the user agent must handle the token as follows:
>
> - (略)
> - A start tag whose tag name is one of: "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl", "dt", "em", "embed", "h1", "h2", "h3", "h4", "h5", "h6", "head", "hr", "i", "img", "li", "listing", "menu", "meta", "nobr", "ol", **"p"**, "pre", "ruby", "s", "small", "span", "strong", "strike", "sub", "sup", "table", "tt", "u", "ul", "var"
> - A start tag whose tag name is "font", if the token has any attributes named "color", "face", or "size"
> 	- Parse error.  
> 	  While the current node is not a MathML text integration point, an HTML integration point, or an element in the HTML namespace, pop elements from the stack of open elements.  
> 	  Process the token using the rules for the "in body" insertion mode.
> - (略)
> 
> ([HTML Standard](https://web.archive.org/web/20210622212326/https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign), 強調は引用者による)

すなわち、`p` などの開始タグを受け取ると、パーサーは HTML 名前空間に戻ってくるまで、まだ閉じていない要素を閉じてゆく。HTML 名前空間に戻ってきたら、そのタグを HTML 名前空間に適用される規則でパースしていく。

したがって、`<svg><p></p>aaa</svg>` をパースすると、パーサーは `<p>` を受け取った時点で、HTML 名前空間に戻るべく `svg` 要素を閉じる。その結果、`<p>` 以降は `svg` 要素の兄弟として、HTML 名前空間の規則に沿ってパースされていくのである。お、おせっかいだなあ……

一方、閉じタグだけがある場合 `<svg></p>aaa</svg>` の仕様はどう定められているか。同じセクションに以下のように記載されている。

> - Any other end tag
> 	- Run these steps:
> 		1. Initialize node to be the current node (the bottommost node of the stack).
> 		2. If node's tag name, converted to ASCII lowercase, is not the same as the tag name of the token, then this is a parse error.
> 		3. Loop: If node is the topmost element in the stack of open elements, then return. (fragment case)
> 		4. If node's tag name, converted to ASCII lowercase, is the same as the tag name of the token, pop elements from the stack of open elements until node has been popped from the stack, and then return.
> 		5. Set node to the previous entry in the stack of open elements.
> 		6. If node is not an element in the HTML namespace, return to the step labeled loop.
> 		7. Otherwise, **process the token according to the rules given in the section corresponding to the current insertion mode in HTML content**.
> 
> ([HTML Standard](https://web.archive.org/web/20210622212326/https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign), 強調は引用者による)

色々と書かれているが、かいつまむと、当該閉じタグに対応する「まだ閉じていない要素」が存在しない場合は 7. のステップを踏む。このステップは、「HTML の規則に沿って処理してね」というものである。

ここでちょっと思い出してほしい。「SVG 内外での `<style>` に対するパーサーの挙動」の節において、私は「HTML のパーサー仕様は複雑、foreign contents はシンプル」と述べた。しかし foreign contents 内の閉じタグについては、HTML での規則に依拠するのである。つまり、foreign contents の中においても、閉じタグに関しては、HTML の闇から逃れられていなかった！

というわけで、HTML 名前空間の中で閉じタグ `</p>` が出現した場合の仕様を見ていこう。今回は "in body" mode の仕様を見れば良い。

> - An end tag whose tag name is "p"
> 	- **If the stack of open elements does not have a p element** in button scope, then this is a parse error; **insert an HTML element for a "p" start tag token** with no attributes.  
> 	  Close a p element.
> 
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#rawtext-less-than-sign-state:~:text=An%20end%20tag%20whose%20tag%20name%20is%20%22p%22), 強調は引用者による)

すなわち、「まだ閉じていない `p` 要素」が存在しない場合、まず (HTML 名前空間の) `p` 要素を挿入してそれをすぐに閉じる、つまり空の `p` 要素が挿入される。

開始タグ `<p>` が SVG 内にある場合と異なり、「SVG 名前空間が暗黙的に閉じられる」という振る舞いは、閉じタグ `</p>` だけがある場合には発生しない。閉じタグの仕様にその規定がないからだ。閉じタグに起因する空の `p` 要素が挿入された後も、SVG 名前空間が続いている。

こうして、開始タグ `<p>` がなく閉じタグ `</p>` だけが `<svg>` 内にあるとき、「`svg` が `p` を子に持つ」という異常な DOM ツリーが構築されてしまったのである。

ちなみに、`<svg><p></p>aaa</svg>` において末尾の `</svg>` は無効な閉じタグとして無視されるわけであるが、この挙動の仕様も一応確認しておこう。ここでも "in body" mode を参照する。

> - Any other end tag
> 	- Run these steps:
> 		1. Initialize node to be the current node (the bottommost node of the stack).
> 		2. Loop: If node is an HTML element with the same tag name as the token, then:
> 			1. Generate implied end tags, except for HTML elements with the same tag name as the token.
> 			2. If node is not the current node, then this is a parse error.
> 			3. Pop all the nodes from the current node up to node, including node, then stop these steps.
> 		3. Otherwise, if node is in the special category, then this is a parse error; **ignore the token**, and return.
> 		4. Set node to the previous entry in the stack of open elements.
> 		5. Return to the step labeled loop.
> 
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#rawtext-less-than-sign-state:~:text=a%20noscript%20element.-,Any%20other%20end%20tag,-Run%20these%20steps), 強調は引用者による)

「まだ閉じていない `svg` 要素」が無い場合はいずれ 3 に到達し、閉じタグ `</svg>` は無視されるというわけであった。

### パズルのピースを再びつなげる

ペイロードを構成するパーツのからくりがわかったところで、ペイロードが全体としてどのようにして mXSS を引き起こしているのか振り返ってみよう。

```
<svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
```

この HTML をパースすると、孤立した閉じタグ `</p>` は空の `p` 要素とみなされる。しかし、SVG 名前空間はここで途切れずそのまま続く。したがって直後の `<style>` は **SVG 名前空間の `style` 要素**であり、字句解析器の状態上書きをしない。続く `<a id="...` は素直に `a` タグとして解釈される。その結果、以下の DOM ツリーが得られる ([AST Explorer](https://astexplorer.net/#/gist/10fcc967e9361171c97dbb4c0ec6ad2c/83ccf1216469343b2672ea57d4025f2e470c3f41)):

- `svg`
	- `p`
	- `style`
		- `a` `id`: `</style><img src=1 onerror=alert(1)>`

そしてこの DOM ツリーに危険なものは含まれないため、DOMPurify はこの DOM ツリーをそのままシリアライズするのだった。このシリアライズにより、当初のペイロードに開始タグ `<p>` が追加される。

```
<svg>
	<p></p>
	<style>
		<a id="</style><img src=1 onerror=alert(1)>"></a>
	</style>
</svg>
```

そしてこの `safeHTML` を `innerHTML` に代入しようとするパース時に、`<p>` が悪さをするのである。開始タグ `<p>` は SVG 名前空間の終了を引き起こし、先頭の `svg` 要素は閉じられ、`<p>` 以降は HTML 名前空間の規則でパースされていく。したがって直後の `<style>` は **HTML 名前空間の `style` 要素**であり、字句解析器の状態は RAWTEXT state に上書きされ、閉じタグ `</style>` が登場するまでは全てが単なる文字として認識される。つまり `<a id="` の部分は単なる文字の並びとして `style` 要素のコンテンツとなり、直後の `</style>` で要素は閉じられる。そして続く
 `<img src=1 onerror=alert(1)>` が、当然 `img` タグとして解釈され、`onerror` イベントハンドラがセットされてしまう。というわけで、以下の全く異なる DOM ツリーが構築されてしまったのだ ([AST Explorer](https://astexplorer.net/#/gist/4e4111b42c7263b45dd97fcf97f2007f/0eb7e80bbd6e12a0d6e577ea94b7c68a531f3328))。

- `svg`
- `p`
- `style`
	- \#text: `<a id="`
- `img` `src`: `1` `onerror`: `alert(1)`
- \#text: `">`

このペイロードの原理をざっくりとまとめる。

- `<p>` は祖先の SVG 要素を閉じて自身以降を HTML 名前空間の規則でパースさせる能力を持つ
- `<style>` は名前空間が HTML か SVG かによって字句解析の挙動が異なり、`<style><a id="</style><img src=1 onerror=alert(1)>">` は異なるトークン列に分解される
- `<svg>` 内に `<p>` ではなく閉じタグ `</p>` だけを入れておくことにより、DOMPurify に処理される1回目のパース時に `<p>` を SVG 内に "密輸" できた

## 修正を理解する

冒頭で述べたように、この脆弱性報告を受けて、DOMPurify, Chrome, そして HTML Standard が修正された。これらの変更がどのようにしてこの mXSS を防いでいるか見てみよう。修正の概略は以下:

- DOMPurify: 2019/09 リリースの 2.0.1 ([diff](https://github.com/cure53/DOMPurify/compare/2.0.0...2.0.1)) にてサニタイズを追加。ある要素の属性値中に `</` が出現する場合はその要素ごと削除するというもの。
- Chrome: 2020/02 リリースの 80.0.3987.87 にて HTML パーサーの実装を修正 ([issue](https://issues.chromium.org/issues/40050167), [diff](https://chromium.googlesource.com/chromium/src.git/+/d16226271d4d501de19f019aba1c145930b45503))。下記 HTML Standard の改訂に先行するもの。
- HTML Standard: 2021/06 ([issue](https://github.com/whatwg/html/issues/5113), [PR](https://github.com/whatwg/html/pull/6736)) に構文解析の仕様を修正。foreign content 内において、開始タグのみならず閉じタグ `</p>` `</br>` が出現した場合にも HTML 名前空間への巻き戻しを行うというもの。

この mXSS は HTML パース・シリアライズのエッジケースを巧妙に利用したものであるが、DOMPurify はパース・シリアライズを自身で実装するのではなくブラウザの API をそのまま利用しているため、パース・シリアライズの挙動を DOMPurify 自身が修正することはできない。そのため DOMPurify の修正は「mXSS ペイロードらしきものを除去する」というものである。そして、パーサーの実装・仕様もこの脆弱性報告を受けて修正された。

### DOMPurify の修正

DOMPurify 2.0.1 の [diff](https://github.com/cure53/DOMPurify/compare/2.0.0...2.0.1) を見ると、以下の処理が `_sanitizeAttributes` 関数に追加されている ([code](https://github.com/cure53/DOMPurify/blob/4c8ca9db5b4b2a79ed6c779ac6f22587ba16a3e1/src/purify.js#L841-L844))。まさに、ある要素の属性値が `</` を含む場合に要素ごと削除するという処理である。

```js
/* Check for possible Chrome mXSS */
if (removeSVGAttr && value.match(/<\//)) {
  _forceRemove(currentNode);
}
```

これによって、問題のペイロードは無害化される。DOMPurify が検査する DOM ツリーは以下の構造をしていたのであった:

- `svg`
	- `p`
	- `style`
		- `a` `id`: `</style><img src=1 onerror=alert(1)>`

この `a` 要素の `id` 属性には `</` が含まれているので、`a` 要素ごと削除され、以下の HTML が出力される。

```html
<svg><p></p><style></style></svg>
```

この HTML は確かに XSS を引き起こさない。

ちなみにこの `removeSVGAttr` 変数は以下のように算出されている ([code](https://github.com/cure53/DOMPurify/blob/4c8ca9db5b4b2a79ed6c779ac6f22587ba16a3e1/src/purify.js#L534-L537))。本件の一因である「`<svg></p></svg>` をパースすると、`p` が `svg` の子要素になってしまう」という挙動を持つブラウザでのみ、このサニタイズを有効化する、ということだろう。

```js
const doc = _initDocument('<svg></p></svg>');
if (doc.querySelector('svg p')) {
  removeSVGAttr = true;
}
```

実際、Firefox (Gecko) はこの mXSS の報告前から `<svg></p></svg>` を現代の仕様と同様にパースしていたらしい。つまり `removeSVGAttr` は、この mXSS が起きない Firefox でサニタイズを飛ばす効果を持つ。

ちなみに `</` を含む属性値を扱いたいというニーズは無いわけではなかったようで、サニタイズを緩和して欲しい旨の [Issue](https://github.com/cure53/DOMPurify/issues/369) が立てられた。`<svg>`, `<math>` を禁止することでも本件の mXSS を防げることから、DOMPurify の設定において `<svg>`, `<math>` が禁止されていない場合のみ、`</` を含む属性値をサニタイズするようになった。

ここでユーザー視点に立つと、patch update である 2.0.0 → 2.0.1 によって既存のワークロードが正常に動作しなくなったわけであるが、これは実に息が胃の痛む話である。その痛みとは、「DOMPurify をアプデすると、今まで動いていたものが正しく動かなくなるかもしれない」という不安が、patch update にすら付きまとうことである。最新の攻撃手法に対策するためにはサニタイザを常に最新に保つことが重要であるのにもかかわらず、アップデートに対する抗力が存在するこの構造はつらい。なんとかしなければならない。

そもそも信頼できない HTML を扱う必要性をなくせるならそれが一番だし、どうしても必要な場合は、DOMPurify をいつでも自信を持ってアップデートできる体制を整えるべきだ。サニタイザの挙動がどう変化しても問題ないなら、心置きなく DOMPurify をアップデートできる。「〇〇が除去されると困る」のような要求があるならば、「DOMPurify が〇〇を除去しない」というテストコードを用意しておけば、DOMPurify のアップデートに対する受け入れ可能性は比較的容易に判定できるだろう。DOMPurify のアップデートに対する受け入れ可能条件の言語化だけでも、導入時にやっておくべきだと思う。

### HTML パーサーの仕様の修正

この脆弱性報告を受けた Chromium チームは、これが HTML の仕様自体のバグであると判断し、改訂に働きかけた。その結果 HTML Standard における構文解析の仕様は以下のように変更された ([PR](https://github.com/whatwg/html/pull/6736))。

*閉じタグ `</p>` `</br>` が foreign content 内で出現した場合には、開きタグ `<p>` `<br>` と同様、HTML 名前空間への巻き戻しを行う。*

[PR](https://github.com/whatwg/html/pull/6736) の diff を見ると一目瞭然だが、HTML 名前空間への巻き戻しに関する分岐の条件節に、閉じタグに関する条件を追加しただけである。

さて、この仕様変更を適用した上で問題のペイロードがどうパースされるか考えてみよう。なおこれは最新の HTML Standard の挙動であるから、お手元のブラウザで、`innerHTML` に代入してみてもよい。

```
<svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
```

`svg` 内の孤立した閉じタグ `</p>` は、HTML 名前空間への巻き戻しを引き起こす。つまり`</p>` の直前で `svg` 要素は閉じられ、`</p>` 以降は HTML 名前空間としてパースされていく。HTML 名前空間では、`</p>` は空の `p` タグとみなされる。そしてここからは `<svg><p></p><style>...` のパースの流れと同様である。`</p>` **直後の `<style>` は HTML 名前空間の `style` 要素**であり、字句解析器の状態は RAWTEXT state に上書きされ、閉じタグ `</style>` が登場するまでは全てが単なる文字として認識される。つまり `<a id="` の部分は単なる文字の並びとして `style` 要素のコンテンツとなり、直後の `</style>` で要素は閉じられる。そして続く
 `<img src=1 onerror=alert(1)>` が、当然 `img` タグとして解釈され、`onerror` イベントハンドラがセットされる。

パース結果:

- `svg`
- `p`
- `style`
	- \#text: `<a id="`
- `img` `src`: `1` `onerror`: `alert(1)`
- \#text: `">`

あれ、`alert(1)` が……?

いや、これで問題ないのである。そもそもこの脆弱性は「DOMPurify のバイパス」であった。問題設定を思い出すと:

```js
function safelySetInnerHTML(dangerousHTML) {
  const safeHTML = DOMPurify.sanitize(dangerousHTML)
  document.body.innerHTML = safeHTML
}
```

というわけで、`dangerousHTML` として件のペイロードを与えると、DOMPurify は以下の DOM ツリーを検査することになる。

- `svg`
- `p`
- `style`
	- \#text: `<a id="`
- `img` `src`: `1` `onerror`: `alert(1)`
- \#text: `">`

これは見るからに危険なイベントハンドラ `onerror=alert(1)` を含むため、DOMPurify はこれをしっかり除去してくれる。そのため、上記の問題設定で `innerHTML` に代入されるのは、`onerror` イベントハンドラが除去された無害な HTML である。めでたしめでたし。

## 教訓を考える

このような DOMPurify バイパスができてしまった本質的な原因を考えてみると、それは以下に尽きると思う。

DOMPurify が「これは安全だ」と判断した DOM ツリーと、`innerHTML` への代入で挿入される DOM ツリーが異なる。

DOMPurify が DOM ツリーに GO サインを出してからそれがドキュメントに挿入されるまでのどこかでコミュニケーションミスが起きた、つまり DOM のシリアライズとパースを経て異なる DOM ツリーに変化してしまったことで、安全ではないものがドキュメントに挿入されてしまったのだ。

この類の脆弱性を根本的に防げないものか。DOM をシリアライズしてパースする処理が恒等関数であれば良いのだが……

ここまで見てきたように、HTML のパース処理は複雑怪奇である。それでも恒等関数を作れるようなシリアライズアルゴリズムは作れるのか……？一縷の望みをかけて、DOM のシリアライズ (`innerHTML` の getter) の[仕様](https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments)をざっくり眺めてみよう。

短くはないのでここには引用しないが、割と直感通りの処理である。`<tag key1=val1 key2=val2 ...>` を出力したのち、子要素を全て出力し、`</tag>` で閉じることを再帰的に繰り返す。

……HTML のパースはあんなにも複雑なのに、シリアライズはこんなにシンプルで良いのか？

と首を傾げていると、警告色の Warning! ボックスがその下に待ち受けている。

> Warning!
> It is possible that the output of this algorithm, **if parsed with an HTML parser, will not return the original tree structure**. Tree structures that do not roundtrip a serialize and reparse step can also be produced by the HTML parser itself, although such cases are typically non-conforming.
> ([HTML Standard](https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments:~:text=It%20is%20possible%20that%20the%20output%20of%20this%20algorithm%2C%20if%20parsed%20with%20an%20HTML%20parser%2C%20will%20not%20return%20the%20original%20tree%20structure.%20Tree%20structures%20that%20do%20not%20roundtrip%20a%20serialize%20and%20reparse%20step%20can%20also%20be%20produced%20by%20the%20HTML%20parser%20itself%2C%20although%20such%20cases%20are%20typically%20non%2Dconforming.), 強調は引用者による)

そう、HTML Standard で規定されている DOM のシリアライズアルゴリズムは、「シリアライズ→パース」が恒等関数になることを目指していない。つまり、DOM をシリアライズしたら、もう元の DOM に戻せないかもしれないのだ。

これはサニタイザを利用する上で由々しき問題である。「サニタイザが安全だと判断した DOM ツリーと、サニタイザの出力をパースして得られる DOM ツリーは異なるかもしれない」という不安に HTML Standard は応えてくれない。

どうにかする方法はないか。

そもそも、シリアライズしなければ良いのである。

サニタイザに文字列ではなく DOM ツリーを出力させ、それを `.appendChild()` などでドキュメントに挿入すれば、「サニタイザが安全だと判断した DOM ツリー」がそのままドキュメントに挿入される。ここに HTML パース・シリアライズの複雑性は絡まない。

幸い DOMPurify にはそのためのオプション `RETURN_DOM`, `RETURN_DOM_FRAGMENT` がある。これを以下のように使うことで、サニタイズ結果をシリアライズすることなく、ドキュメントに挿入できる。

```js
function safelySetInnerHTML2(dangerousHTML) {
  const safeDOM = DOMPurify.sanitize(dangerousHTML, {RETURN_DOM_FRAGMENT: true})
  document.body.replaceChildren(safeHTML)
}
```

このようにすれば、round-trip mXSS に分類される脆弱性は起きないと言える。そもそも round-trip する機会がないからだ。

ところで、HTML Standard がシリアライズ・パースの round-trip 恒等性を保証しないことを踏まえると、「サニタイザが安全と判断した DOM ツリーをシリアライズする行為」は一種の desanitization と捉えることができるのではないかと考えている。

Desanitization とは、サニタイズ結果を加工して使用した結果、無害ではなくなってしまうという脆弱性のパターンである。

> If you sanitize content and then modify it afterwards, you can easily void your security efforts.
> 
> ([Cross Site Scripting Prevention - OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html#html-sanitization))

HTML サニタイザは DOM ツリーを操作して無害化し、安全な DOM ツリーを構築する。しかしそれをシリアライズしてしまうと、そのパース結果は「安全な DOM ツリー」とは異なるものになる可能性がある。これを踏まえると、「サニタイザが安全と判断した DOM ツリーをシリアライズする行為」は、一種の加工、つまり desanitization によるサニタイズの無効化と捉えることができるのではないか。

まとめると、round-trip mXSS 対策としては以下の2点両方を満たすのが極めて有効であると考えている。

- サニタイズをクライアント側で行う
- サニタイザに DOM ツリーを出力させ、途中でシリアライズすることなく DOM ツリーに挿入する

これら2点を遵守する大きなデメリットは特に思いつかないので、HTML サニタイザ利用時のベストプラクティスとしても良いのではないかと思うが、どうだろう (JS を無効化している環境でコンテンツを描画できないぐらいか？)。もしこれら2点を回避すべき状況を知っている人がいたらぜひ教えてほしい。

## おわりに

HTML パーサーの自作を始める前は mXSS には手も足も出なかったのだが、ある程度 HTML の仕様を読み慣れてきたので、そこまで苦戦せずにこの mXSS を理解できた。

振り返ると、第1弾で私はこんな感想を残していた。

> 汎用的・体系的な理解をしたくて HTML パース処理の仕様に飛び込んだが、「複雑すぎて体系も何もないのではないか」というのが今のところの正直な感想である。

一方で今回は、なんとなく mXSS のテクニックのようなものを少し掴んだ気がする。特に以下のまとめは、いくらか汎用性がありそうに思える。

> - `<p>` は祖先の SVG 要素を閉じて自身以降を HTML 名前空間の規則でパースさせる能力を持つ
> - `<style>` は名前空間が HTML か SVG かによって字句解析の挙動が異なり、`<style><a id="</style><img src=1 onerror=alert(1)>">` は異なるトークン列に分解される
> - `<svg>` 内に `<p>` ではなく閉じタグ `</p>` だけを入れておくことにより、DOMPurify に処理される1回目のパース時に `<p>` を SVG 内に "密輸" できた

これが実際に他の mXSS でも活かせるものなのかそうでないのかは、第3弾以降で明らかになるだろう。続編に乞うご期待 2

この記事は、ei-chan さんが企画してくれた社内勉強会に向けて調査・執筆したものである。この機会を作ってくれた ei-chan さん、いつもありがとうございます！
