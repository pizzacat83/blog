---
draft: true
published: 2025-03-30
summary: あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。
---
# 自作ブラウザ CSS・レンダリング編

書籍『［作って学ぶ］ブラウザのしくみ』を参考にブラウザの自作を進めている。CSS を解釈し画面を描画することを1つの区切りとして、この記事を書く。この本の第5章・第6章に相当する部分である。

第5章より前までに出来上がっていたものは、HTTP クライアントと HTML パーサーであった。ここからさらに CSS を解釈して画面を描画できるようにすると、HTML をフェッチして画面に表示できる。これは大きな転換点である。やはり、「HTTP クライアントと HTML パーサーだけ」の状態と比べると、「HTML を画面上に表示できる」という状態の "ブラウザっぽさ" は段違いだ。一刻も早く、この状態に至りたい。

なお HTTP クライアントや HTML パーサーの実装についても追々記事を書きたいが、まずは直近やっている CSS とレンダリングの話から書いていく。ちなみに自作 HTML パーサーの延長上で「[HTML パーサー自作で理解する Flatt Security XSS Challenge 1](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345)」という記事を書いたので、よかったらこちらも。

## ブラウザ自作プロジェクトについて

ブラウザ自作の全体像についてこれまで特に書いていなかったからここで書くことにする。

書籍『［作って学ぶ］ブラウザのしくみ』を参考にブラウザを自作しているブラウザの赤ちゃんは、以下のリポジトリにて公開している。

<!--todo-->

この本は自作ブラウザを saba (SAmple Browser Application) と名付けているので、今後この本は sababook と呼ぶことにする。ちなみに私の自作ブラウザは、sababook からちょくちょく脱線するスタイルで作っていることを考慮し、リポジトリ名少しアレンジして sabatora と名付けた。🐈

### ブラウザを自作する目的

私がブラウザ自作に取り組む目的は主に以下の3点である。

- ブラウザの仕組みや Web の標準の理解を深める
- no_std Rust に触れる
- 自作キーボードのタイピングに慣れる

第一の目的は書名にもあるように、「ブラウザの仕組みや Web の標準の理解を深める」ことである。ブラウザ自作の教材は sababook の他にも「[Web Browser Engineering](https://browser.engineering)」や「[ちいさな Web ブラウザを作ろう](https://browserbook.shift-js.info/)」などがあるが、ブラウザが動く仕組みを深く理解する上で sababook は優れていると感じる。自作ブラウザを自作 OS の上で動かし、`core` と自作 OS が提供するライブラリしか使わないという縛りによって、ブラックボックスである部分がかなり小さくなっている。それに、パーサーコンビネータ等のライブラリを使わないので、定義されている仕様をライブラリの世界観に合わせてどうにか翻訳する必要はなく、仕様書を素直に実装することができる。no_std を使う機会というのもまたありがたい。

自作キーボードの件は脇道に逸れるけれどもモチベーションの一つではある。去年自作キーボードを購入したもののまだタイピング練習を十分にできておらず、練習台として写経の題材が欲しかったのだ。慣れないキーボードで創造的なタスクをやろうとすると、タイプミスに脳のリソースを奪われて創造性が阻害されている感じがするし、フィードバックループが思ったスピードで回せずストレスが溜まってしまう。一方写経的なタスクであれば、コードを書く際にタイピングを意識する脳の余裕は残っている。

そういうモチベでブラウザ自作をやっているので、コードを書く際は Copilot の補完をオフにしている。どうも Copilot の補完は Web の各種標準の知識をしっかり持っているようで、正しい実装を一気に20行ぐらいサジェストしてくるのである。これではブラウザ自作がタブキー連打になってしまう。なんだか手を動かして理解した感じがしないし、タイピングの練習にもならない。

### 実装の進め方

実装は大まかに以下のような流れで進めている。

1. sababook をざっくり読む
2. sababook を参考に、最初のゴールを決める
	- 例: `display` プロパティを扱えるようにする
3. 型定義を書く
4. ゴールをもとにテストを書く
	- 例: `.card { display: block }` を正しくパースできる
5. 仕様書をもとに、テストが通るコードを書く
6. より大きいゴールを定め、3に戻る
7. たまに、Servo や Chromium などの実装を見て比較する

特に重視しているのは、「sababook ではなく Web 標準の仕様書を基に実装すること」「実装より先にテストを書くこと」である。先述したように、ブラウザ自作の最大の目的は、ブラウザの仕組みや Web の標準への理解を深めることであり、sababook をベースに実装するよりも、Web 標準をベースに実装した方が、この目的に沿う。それに、sababook に書かれていないブラウザ機能を自作したくなった場合には、頼れるものは仕様書だけである。そのためには、仕様書と自分のブラウザの実装がどのように紐づいているのかを把握しなければならない。仕様書をベースにブラウザを実装しておけば、気兼ねなく sababook の内容をはみ出せる。

仕様書をベースに実装するなら sababook は必要なのか、と思われるかもしれないが、非常に助かっている。喩えるならば、ブラウザ自作において仕様書は地図であり、sababook は旅行ガイドである。ブラウザの世界は広大であり、地図のみで3泊4日の旅行プランを立てるのは至難の業だ。見どころをピックアップし解説する旅行ガイドのおかげで、限られた時間の中でメインスポットを巡れる。しかし旅行ガイドに書かれていない場所に足を伸ばすなら、旅行ガイドに書かれているスポットが地図のどこに対応するのかを把握しなくてはならない。旅行ガイドを読みながら地図にマーカー線を引き、興味の赴くままに寄り道と回帰を繰り返す。これが私のブラウザ自作の進め方である。

このように sababook の内容を逸脱しながらブラウザ自作を進めるコツが、「実装する前にテストを書く」である。ブラウザを実装する行為は旅と違って stateful であり、「脱線した後に元の軌道に戻る」ことは自明ではない。例えば sababook の第5章は、CSS をパースする処理と、パース結果を元に要素を配置する処理に分けられるが、仮に sababook の記述を完全に無視して我流で CSS パーサーを作ったとして、第5章の後半に書かれた「ここまで実装してきた CSS パーサーの結果を元に、要素を配置する処理を実装しましょう」のような記述・サンプルコードが、どれほど私のコードベースにおいて参考になるだろう？sababook の記述やサンプルコードを自作ブラウザの実装に活用するには、sababook と自分の実装の類似度をある程度維持する必要がある。その類似度の基準として有用なのが、「同じインターフェースを提供すること」「サンプルコードのテストを通すこと」である。sababook のサンプルコードはありがたいことにモジュラーな設計になっており、テストコードも用意されている。したがって、自作ブラウザの実装の詳細が sababook から乖離していたとしても、両者が同じインターフェースを提供していて、外形的な振る舞いも一致するならば、後続の章はたぶんなんとかなるだろう、と思っている (少なくとも、現時点ではなんとかなっている)。

### 私の事前知識について

ブラウザ自作を始めた時点での、周辺技術に関する私の習熟度についても書いておく。<!-- TODO: かく -->

