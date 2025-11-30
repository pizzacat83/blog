---
draft: true
published: 2025-11-27
summary: あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。
---
# HTML パーサー自作で理解する mXSS (CVE-2019-16728・CVE-2020-6413 篇)

Web 標準に沿って HTML パーサーをスクラッチ実装することを通して、mXSS などパース周りの細かい仕様を活用した XSS テクニックの原理を理解していくシリーズの第2弾。今回は、mXSS を利用した DOMPurify < 2.0.1 のバイパス ([CVE-2019-16728](https://nvd.nist.gov/vuln/detail/CVE-2019-16728)・[CVE-2020-6413](https://nvd.nist.gov/vuln/detail/CVE-2020-6413)[^two-cves]) を題材とする。

第1弾: [HTML パーサー自作で理解する Flatt Security XSS Challenge 1](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345)

ちなみに CVE-2019-16728 と CVE-2020-6413 はどちらもいずれも同一の mXSS に対する CVE であり、CVE-2019-16728 は DOMPurify に対するもの、CVE-2020-6413 は Chrome に対するものである。というのも、DOMPurify は HTML のパース・シリアライズを自身で実装せず、ブラウザの API を利用している。そのため、パース・シリアライズの振る舞いに起因するこの mXSS は、DOMPurify と Chrome 両方に対して脆弱性報告がなされた。その結果、HTML Standard における構文解析の仕様を改訂するにまで至った。今回の脆弱性による影響を以下にまとめる。

- DOMPurify: 2019/09 リリースの 2.0.1 ([diff](https://github.com/cure53/DOMPurify/compare/2.0.0...2.0.1)) にてサニタイズを追加。ある要素の属性値中に `</` が出現する場合はその要素ごと削除するというもの[^dompurify-aggressive-sanitization]。
- Chrome: 2020/02 リリースの 80.0.3987.87 にて HTML パーサーの実装を修正 ([issue](https://issues.chromium.org/issues/40050167))。下記 HTML Standard の改訂に先行するもの。
- HTML Standard: 2021/06 に、構文解析の仕様を修正

[^dompurify-aggressive-sanitization]: `</` を含む属性値を扱いたいというニーズは無いわけではなかったようで、サニタイズを緩和して欲しい旨の [Issue](https://github.com/cure53/DOMPurify/issues/369) が立てられた。`<svg>`, `<math>` を禁止することでも本件の mXSS を防げることから、DOMPurify の設定において `<svg>`, `<math>` が禁止されていない場合のみ、`</` を含む属性値をサニタイズするようになった。ユーザー視点からすれば、patch update である 2.0.0 → 2.0.1 によって既存のワークロードが正常に動作しなくなったのであるが、これは実に息が苦しくなる話である。どう困った話であるかというと、「DOMPurify をアプデすると、今まで動いていたものが正しく動かなくなるかもしれない」という不安が、patch update にすら付きまとうのである。最新の攻撃手法に対策するためにはサニタイザを常に最新に保つことが重要であるのにもかかわらず、アップデートに対する抗力が存在するこの構造が悲しい。なんとかしなければならない。そもそも信頼できない HTML を扱う必要性をなくせるならそれが一番だし、どうしても必要な場合は、DOMPurify をいつでもアップデートできる体制を整えるべきだ。サニタイザの挙動がどう変化しても問題ないなら、心置きなく DOMPurify をアップデートできる。「〇〇が除去されると困る」のような要求があるならば、「DOMPurify が〇〇を除去しない」というテストコードを用意しておけば、DOMPurify のアップデートに対する受け入れ可能性は比較的容易に判定できるだろう。DOMPurify のアップデートに対する受け入れ可能条件の言語化だけでも、導入時にやっておくべきだ。

というわけで今回の脆弱性は HTML Standard の改訂や Chrome の修正によって解決されたため、**現代のブラウザで再現することはできない**。しかし古い仕様を参照して HTML パーサーを実装すれば再現できる。

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
