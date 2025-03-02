---
published: 2025-03-02
summary: このブログを構築する過程の紆余曲折を綴ります。
---
# F# でこのブログを構築した

周囲に触発されて英語ブログを書きたくなったが、どこに記事を置くかをいろいろ検討した結果、静的サイトジェネレータで構築することにした。このサイトの公開に至るまでの色々を気の赴くままに書いていく。

## このサイトの技術構成

このブログサイトを構築するまでには色々と紆余曲折があり、後続のセクションにはその紆余曲折が延々と綴られているのだが、最初に結末を書いておこう。

このサイトのジェネレータ実装とコンテンツは [pizzacat83/blog](https://github.com/pizzacat83/blog) リポジトリに置いてある。

ジェネレータとしては、Fornax という F# の静的サイトジェネレータを私が fork したものを使っている。Markdown 原稿の読み込みや HTML・RSS の生成処理はすべて F# で実装されている。

生成されたファイル一式は、Cloudflare Pages で配信している。

## コンテンツを書くことに集中したい

さて英語ブログを書きたいと思い立ったわけだが、その手段として最も重要だと思った性質は、「書くことに集中できること」だと思った。だから実装なんかせずにブログサイトを使えば、あとはコンテンツを書くだけである。実際、今までブログをはてなブログに書いてきたが、「Markdown を書いて公開するだけ」という感覚で使えるのは魅力的だった。

しかし悲しいことに、なんというか気に入った英語ブログサイトがなかったので、静的サイトジェネレータを使うことにした。そこでざっくり要件を書き出すと:

- コンテンツを Markdown で書ける
- 記事ページに OGP タグをつけられる
- 外部サイトへのリンクを埋め込み表示できる
- フィードが生成される

それからもう一つ、「JavaScript をなるべく書かない縛りをやってみたい」という感情が湧いた。これまでフロントエンドは React や Vue などで書くことがほとんどで、逆に JS を使わずに Web サイトを作ったことがほとんどない。動的な UI を作りたいときは、ブラウザで動作する言語が限られている都合上、どうしても JS に接する技術を選んでしまいがちだ。一方で今回のブログサイトの UI は静的で良いと思っているので、JS に縛られる必要がない。せっかくの機会なのだから、ここは JS を使わない新鮮な選択をしてみたいと思った。

こうして静的サイトジェネレータを探す旅に出た。色々事例を眺めていると、「ジェネレータやプラグインの破壊的なアップデートに追従するのが大変」みたいな話をちらほら見かける。私はコンテンツを書いて公開したいのであって、ジェネレータの破壊的変更に対応したいわけじゃない。安定したジェネレータを使いたい。

しかし「非 JS」「安定」を意識して静的サイトジェネレータを探すと、なんというか気に入ったものが見つからない。無意識の中に、「なんかイケてるやつを使いたい」という感情があることに気がついた。これと「安定」との両立が難しい。

そして、要件のうち「外部サイトへのリンクを埋め込み表示できる」が核心的である気がしてきた。Markdown ファイルのリンク先をフェッチしていい感じにレンダリングする必要があるが、そのような処理はテンプレート言語の表現力では実現が難しいことが多い。静的サイトジェネレータのドキュメントに目を通し、その世界観を理解し、埋め込み表示をするための諸々の処理が世界観のどこに収まるものなのかを考えて実装する必要があるような感触を得た。しかし私はコンテンツを書いて公開したいのであって、静的サイトジェネレータの世界観を理解してその中にリンク埋め込み機能をねじ込みたいわけではない。

なんというか、全ての要件と感情をスマートに満たす解は無い気がしてきた。

## 「コンテンツを書く」以外のタスクを楽しめる選択を: つまり自作

少し考え方を変えて、「要件を満たすためのカスタム実装や、アプデに追従するための改修を、ウキウキ進められるような選択をしよう」と考えた。コンテンツを書くことへの完全な集中は諦めて、コンテンツを書く以外のタスクを楽しめるかどうかを重視することにした。

ここで静的サイトジェネレータを自作することを思い立った。私は[ブラウザの自作に手を出した](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345)のだから、静的サイトジェネレータを自作してもおかしくはない。好きな言語でジェネレータを自作しよう。既成の静的サイトジェネレータの世界観を読み解く必要はない。言語の破壊的変更に追従する必要はあるかもしれないが、好きな言語だからそれは愛せる。

ジェネレータの実装に使う言語は、Rust か Scala にしようと思った。ちなみに Rust は趣味や仕事でそこそこ書いたことがあるが、Scala を書いたことはほぼない。Scala は勉強したいと以前から思っていたが「Scala でつくりたいもの」がなかなか思い浮かばず、手をつけられていなかった。

Scala を勉強したいと思っていた理由は、「可変参照が不要でパフォーマンス要件がゆるいときの選択肢が欲しい」というものである。Rust を愛好している理由はいくつかあり、struct や enum などのデータ型の表現力、match や if-let などの便利で堅牢な構文、所有権型システムによる可変参照の安全な取り扱い、そしてこれらを土台に形成された堅牢なエコシステムなどが挙げられる。ただ、可変参照が不要でかつパフォーマンスを気にしなくていい状況では、Rust の参照周りの機能は必要性を感じないことが多い。コンパイラに言われた通りに `&` や `*` をつけ外ししたり `.clone()` を呼んだりすればコンパイルは通るし、コンパイラの言うことは極めて真っ当なのだが、ただ、それは開発したいものの本筋からは離れる。こういった状況では、Scala のような言語で immutable に処理を実装したほうが、開発したいものの本質的な実装に集中できると思うのだ。

静的サイトジェネレータは、個人のブログを生成するぐらいの用途であれば、パフォーマンスはあまり気にしなくて良いし、可変参照が必要な場面はかなり少ない。そこで静的サイトジェネレータの実装がてら Scala に入門しようと思った。ただ悲しいことに、 Scala のいい感じの Markdown パーサーライブラリを見つけられなかったので、Rust で実装することにした。Markdown パーサーを自作するという選択肢は取りたくなかった。記事を書きながら、「あ〜、自作パーサーは表の記法に対応してないんだった。実装重いから表は書かないでおくか」のような思考はしたくないからだ。

### 自作の第一歩: 人力サイトジェネレータ

ジェネレータを自作するにあたり最初にやったことは、生成したい HTML を一旦全部手で書くことである。Pandoc で Markdown ファイルを HTML に変換し、それにヘッダーやフッターを手で書き足して、CSS を書いた。手作業で HTML を生成する作業を少しずつ自動化していけば、静的サイトジェネレータが出来上がってくる、という算段である。たとえ私が道半ばで力尽きても、ブログサイトは公開できる。それに HTML 手書きなら、Rust のコンパイル時間を待つ必要もなく、サクサク見た目を調整できる。

デザインは、めちゃくちゃシンプルにするかめちゃくちゃ個性を出すのが好きだけれども、個性が出るデザインがパッと思いつかなかったので、めちゃくちゃシンプルにした。記事本文の文字サイズや行間は、自分が読みやすいと思える数値に調整した。

スタイルはフレームワークを使わずに手書きすることにした。これもまた、破壊的変更に追従する必要を減らすための決定である。しかし書いていると、`color: #333333;` とか逐一書くのがつらくなってきて、Tailwind の `text-gray-800` が恋しくなってくる。使う色には一定の統一感を持たせたいが、同じカラーコードを何箇所にもコピペするのはしんどい。そこで、CSS Variables で色を管理することにした。

```css
:root {
    --lighter-gray: #eee;
    --light-gray: #ddd;
    --gray: #888;
    --dim-gray: #666;
    --dark-gray: #333;
    --darker-gray: #111;

    --pale-blue: #3c7fac;
}
```

色の名前は、`--title` のような用途に即した名前ではなく、色そのものの名前をつけることにした。用途の適切な名前を考えるのも大変だし、例えば同じ「タイトル」という概念でも、場所によって使いたい色は違うかもしれない。色そのものの名前としては `--gray-3` のような名前をつける手もあったけれど、まあ CSS を書いている時の脳内は「明るいグレー」「真ん中のグレー」「暗めのグレー」「や、もうちょっと明るく」みたいなレベルでしか考えていないから、`--light-gray`, `--gray`, `--dark-gray` +α という感じで名前をつけた。

話を脱線させると、一通り CSS を書いてから Tailwind の [Utility-First というコンセプトの説明](https://tailwindcss.com/docs/styling-with-utility-classes)を読んだ。従来の HTML と CSS を分けて書く方式と違って、クラス名を捻り出す必要がないし、2つのファイルを行き来する必要もないし、local に見た目を制御しているが故の安心感がある。インラインスタイルとの比較については、hover のような擬似クラスに依存する制御や、`@media` による制御を記述できることを長所としている。全くもってその通りで、CSS フレームワーク禁止縛りをして CSS を書いたことで、フレームワークがどういったメリットをもたらしているのかを実感を持って理解できたように感じる。

### 今度はライブラリ探しの旅へ

そんなこんなで人力サイトジェネレータでブログサイトをざっくり構築したので、次は静的サイトジェネレータの実装である。Markdown パーサーとしては [pulldown-cmark](https://crates.io/crates/pulldown-cmark) があり、パース結果を HTML に変換することもできる。テンプレートエンジンはどうしようか。

テンプレートエンジンと聞いてまず思い浮かぶのは [handlebars](https://crates.io/crates/handlebars) だった。しかし handlebars のようなテンプレート言語は Rust の型システム等とあまり連携していない。変数名の補完は効かないし、フィールド名を typo してもコンパイル時点では検出されず、実行時エラーとなる。enum を綺麗にパターンマッチできるわけでもない。頑張ってブログサイトの種々のデータを struct や enum で綺麗に定義しても、その恩恵は handlebars のテンプレート内では受けられないのだ。

また静的型システムは、数ヶ月前に書いたコードを思い出すのに役に立つ。コード辺のおおまかな仕様が型として表現されていて、静的型システムがその仕様を保証してくれているからだ。Rust の型システムと連携していない handlebars のテンプレートだと、「この変数はどういうデータ構造なんだっけ？」をなんとか探り出さねばならない。

というわけで handlebars を保留して Rust の型システムと連携したテンプレートエンジンを探し求めると、[askama](https://crates.io/crates/askama) を見つけた。これは type-safe, compiled Jinja-like templates を謳う。テンプレートファイルは、確かに見慣れた記法である。

```
Hello, {{ name }}!
```

このテンプレートファイルを、以下のように derive マクロで Rust の世界に取り込む。

```rust
#[derive(askama::Template)]
#[template(path = "hello.html")]
struct HelloTemplate<'a> {
    name: &'a str,
}
```

ここで `{{ nam }}` のようにフィールド名を typo したら、コンパイル時にちゃんと型エラーとして検出される。テンプレートの入力となるデータの型も明快である。パターンマッチの構文も用意されている。

ただ残念なのは、テンプレートを書くときの開発体験が、handlebars 等とさほど変わらないことだった。Askama の IntelliJ Plugin はあるものの、テンプレートファイルを編集する際には、コード補完や型エラーのフィードバック、jump to definition などの機能は提供されていないようだった (私の環境構築がうまくいっていないのかもしれない)。テンプレートを書くときの体験が、Rust の普通のコードを書く時と同じくらい快適ならいいのに…と思った。つまり欲しかったのは型システムだけではなく、Rust のエコシステムが全体として醸成するあの快適な開発体験だった。

そこで Rust 言語との統合性を軸にさらに調べると、[maud](https://maud.lambda.xyz/) というテンプレートエンジンを見つけた。公式サイトに載っているサンプルコードは以下の通り。

```rust
html! {
    h1 { "Hello, world!" }
    p.intro {
        "This is an example of the "
        a href="https://github.com/lambda-fairy/maud" { "Maud" }
        " template language."
    }
}
```
(引用元: https://maud.lambda.xyz/)

このように、マクロを用いた簡潔な構文で、HTML を構築できる。`match` 文に関するサンプルコードも掲載されている。

```rust
enum Princess { Celestia, Luna, Cadance, TwilightSparkle }

let user = Princess::Celestia;

html! {
    @match user {
        Princess::Luna => {
            h1 { "Super secret woona to-do list" }
            ul {
                li { "Nuke the Crystal Empire" }
                li { "Kick a puppy" }
                li { "Evil laugh" }
            }
        },
        Princess::Celestia => {
            p { "Sister, please stop reading my diary. It's rude." }
        },
        _ => p { "Nothing to see here; move along." }
    }
}
```
(引用元: https://maud.lambda.xyz/control-structures.html)

Rust のコード内に書くマクロの形態なので、シンタックスハイライトも jump to definition も使える。Rust の恩恵をかなり享受できる、かなり魅力的なライブラリだと思う。後述するきっかけがあり結局は F# でブログを構築することになるのだが、そのきっかけがなければ、maud を使って Rust でブログを構築していたと思う。

しかし、TSX が恋しい！脱線するが、TSX の開発体験は感動的だ。HTML と TypeScript を書き慣れていれば、いつも通り HTML や TS を書くだけで、望みの処理を実装できる。追加で覚えるべき文法は極めて少ない。そしてコード補完や型検査など、TS で使える強力なエディタ支援機能は TSX でも同様に使える。さらに、TS の型消去ベースのトランスパイルや hot loading 等の技術のおかげで、コードを保存すると即座にレンダリング結果を確認できる。

UI を実装するエコシステムにおいて、「コードを保存したらレンダリング結果を即座に確認できる」という性質は重要だと思っている。UI は人間がソフトウェアと接する部分であるから、「人間が見て・触って使いやすい」ということが主要なゴールであり、それを確かめるには人の目と手で確かめるのが第一である。それゆえに UI を実装する際はしばしば「見た目をちょっと変えて再確認」という周期の短いループが発生する。TSX のエコシステムはこの点でも UI 実装の開発体験に大きく寄与していると思う。

## そして出会った scriptable SSG using F#: Fornax

折しもこの時、『[関数型ドメインモデリング](https://tailwindcss.com/docs/styling-with-utility-classes)』という、サンプルコードが F# で書かれている本を読んでいた。これに触発されて、Rust ではなく F# でジェネレータを実装してみようかなと思った。そこで F# の Markdown パーサーライブラリを探す中で、F# で書かれた [Fornax](https://github.com/ionide/Fornax) という静的サイトジェネレータを知った。そしてこれに一目惚れした。

(なお、『関数型ドメインモデリング』を読むきっかけは、Rust.tokyo で関数型パラダイムでのドメイン駆動設計の話をちらほら聞いたことだ。Rust で進めようとしていたプロジェクトを F# に方針転換したきっかけが Rust.tokyo だというのは皮肉な話である。)

さて Fornax の何に一目惚れしたか。それは、10分でその世界観を理解でき、どうすれば所望のブログサイトを生成できるかがはっきりとわかることである。

Fornax のドキュメントは [GitHub にある README](https://github.com/ionide/Fornax/blob/master/README.md) の一枚がほぼ全てである。全体の概要は数分流し読みすれば掴めると思うので、読んでみてほしい。

ただでさえ簡潔明瞭なこのドキュメントをさらに要約すると、Fornax では、サイトの生成方法をユーザーが F# スクリプトで記述する。ユーザーは、データを読み込む loaders, 読み込んだデータからファイルを生成する generators, そして諸々の設定を書く config の3種のスクリプトを書く。F# スクリプトゆえ、カスタマイズの自由度は極めて高い。ファイルシステムからデータを取得してもいいし、何かの API をフェッチしてもいい。HTML を生成することも、RSS を生成することも可能だ。これらの手続きを F# で普通に実装するだけで、これらが実現できる。外部リンクの OGP タグを抽出して埋め込むのだって、F# でその処理を実装する方法はなんとなく想像がつくから、その通りに書けば良い。

Fornax は、HTML を生成するためのヘルパー関数群も提供している。`article`, `time`,  `h1` などが、Fornax の提供する関数である。これを使うと、HTML の生成ロジックは以下のように、宣言的に・簡潔に記述できる。

```fsharp
type Post = {
    published: System.DateOnly
    href: string
    title: string
    summary: string
    body: RawHtml
}

let post_summary (post: Post) =
    article [] [
        time [DateTime (post.published.ToString("yyyy-MM-dd"))] [];
        h1 [Class "post-title"] [a [Href post.href] [!! post.title]];
        p [Class "post-summary"] [!! post.summary]
    ]

let post_list (posts: Post seq) =
    div [Class "post-list"] (posts |> Seq.map post_summary)
```

これはコンポーネント指向にもなっている。データを引数として HTML のツリーを返す、F# の普通の関数を書くだけである。React でいう children を持つようなコンポーネントも、`HtmlElement -> HtmlElement` のような関数を書くだけである。

そして、HTML 生成ロジックの記述に用いられているのは独自のテンプレート言語ではなく F# だから、自在に F# の構文を入れ込むことができるし、F# の型検査やコード補完の恩恵を享受できる。実際、`post_list` では記事のシーケンスを `Seq.map` に流して記事一覧の HTML を構築している。もちろん、`if` や `match`  も利用できる。

静的サイトジェネレータ自作と同程度の自由度、学習コストの低さ、そして F# 譲りの開発体験に魅せられ、私は Fornax でブログを構築することにした。

### 手書き HTML を Fornax に移植

Fornax の魅力を存分に語ったところで、ここからは実際に Fornax でサイトを構築した流れの話をしていく。既に手書きしたブログサイトの HTML があったので、これをベースにすれば良い。

`fornax watch` を実行するとサイトが生成され、ローカルサーバーが立ち上がり生成結果を確認できる。F# スクリプトや Markdown 原稿を書き換えると、サイトが再生成される。

まずは Markdown の原稿を HTML に変換する。Fornax のサンプルコードでは、`Marking.Markdown.ToHtml` を呼び出している。ただ、将来的には Markdown パーサーや HTML への変換をカスタマイズしたくなるかもしれない。Markdig は C# で実装されているが、`FSharp.Formatting.Markdown` は F# で実装されているので、将来的なカスタマイズの可能性を考慮し、後者を採用した。

```fsharp
open FSharp.Formatting.Markdown

let doc = Markdown.Parse source
let body = Markdown.ToHtml (MarkdownDocument(doc.Paragraphs, doc.DefinedLinks))
```

このような外部ライブラリを F# スクリプト内で読み込む一般的な方法は、`#r "nuget: FSharp.Formatting"` 構文である。しかしこの構文の実行には結構時間がかかってしまうので、スクリプトを書き換えてはサイト生成結果を確認することを繰り返す Fornax において、この構文の遅さは開発体験を悪化させてしまう。代わりに、DLL ファイルを事前にローカルに落としておき、`#r "path/to/FSharp.Formatting.Markdown.dll"` のように読み込めば、ロード時間は改善する。DLL ファイルをローカルに落とす手段は色々あるが、F# なら paket を使うのが便利だ。`dotnet paket init` を実行して `paket.dependencies` に `nuget FSharp.Formatting` と書いておくと、`./packages` の下に DLL ファイルが格納されるので、それを `#r` で読み込めば良い。ちなみに F# Interactive では `--compilertool` オプションをうまく使うと `#r "paket: nuget FSharp.Formatting"` と書くこともできるが、Fornax で同様のことをやるには Fornax 内部のスクリプト実行機構に手を入れる必要がありそうで、まだ実装できていない。

続いて、「F# スクリプトや Markdown 原稿が変更された際に、サイトの再生成後にブラウザをリロードする」ための機構を導入する。Fornax のサンプルコードを見ると、以下のスクリプトを挿入する処理がある。

```fsharp
let websocketScript =
    """
    <script type="text/javascript">
        var wsUri = "ws://localhost:8080/websocket";
        function init()
        {
            websocket = new WebSocket(wsUri);
            websocket.onclose = function(evt) { onClose(evt) };
        }
        function onClose(evt)
        {
            console.log('closing');
            websocket.close();
            document.location.reload();
        }
        window.addEventListener("load", init, false);
    </script>
    """
```

このスクリプトは、WebSocket で `ws://localhost:8080/websocket` に接続し、クローズされたらページをリロードする、というものである。このスクリプトをページに埋めておくと、サイト再生成後にページがリロードされる。これは、Fornax のローカルサーバーが、サイトの再生成完了を (close によって) 知らせる WebSocket エンドポイントを持っているためである。ソースコードを書き換えるとブラウザが自動でリロードされる機構はかなり開発体験を幸せにする要素だが、それがこのように実現できるのか、と勉強になる。

次に、Markdown から変換された HTML に、いい感じのヘッダー・フッター等をつける。手書きした HTML が既にあるから、その本文部分に Markdown の変換結果を挿入してやる、というゴリ押し実装で行く。

```fsharp
let layout (content: RawHtml) =
    $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>A Useful Custom Function to Debug Firestore Security Rules</title>

<link rel="stylesheet" href="/assets/style.css">
<link rel="stylesheet" href="/assets/post.css">

<link rel="alternate" type="application/rss+xml" title="posts" href="/rss">

{websocketScript |> (fun (RawHtml x) -> x)}
</head>
<body>

<header>
<nav>
    <div class="blog-title">
        <a href="/">pizzacat83's blog</a>
    </div>
    <div>
        <a href="https://pizzacat83.com">About</a>
    </div>
</nav>
</header>
<main>

<article>

<header>
<time datetime="2022-05-21">2022-05-21</time>
<h1 id="a-useful-custom-function-to-debug-firestore-security-rules">A
Useful Custom Function to Debug Firestore Security Rules</h1>
</header>
{content |> (fun (RawHtml x) -> x)}

</article>
</main>

<footer>
   <p>© 2025 pizzacat83 • <a href="/rss">Feed</a></p>
</footer>

</body>
</html>
    """
```

コンポーネント指向はどこに行った、と言われるかもしれないが、そういったリファクタリングは追々やっていく。実装がいくら綺麗だろうと、サイトが完成せずブログがいつまでも公開されないなら、その実装は価値を発揮しない。最初はゴリ押し実装で完成に漕ぎつけ、コードは追々磨いていく。

さて、上記の `layout` 関数は本文を挿入しただけで、ページタイトルや投稿日はダミーのままである。ページタイトルは、Markdown ファイルの h1 見出しから抽出したい。投稿日は、frontmatter に書くことにする。そこで、Markdown から h1 見出しと frontmatter を抽出する処理を実装する。

```fsharp
let splitMarkdown (markdown: string) =
    let lines = markdown.Split('\n')

    let frontmatter =
        if lines[0] = "---" then
            lines[1..]
                |> Array.takeWhile (fun x -> x <> "---")
                |> String.concat "\n"
                |> Some
        else
            None

    let title = 
        lines
        |> Array.tryFind (fun x -> x.StartsWith("# "))
        |> Option.map (fun x -> x.Substring(2))
        |> Option.defaultValue "No title"

    let body =
        lines
        |> Array.filter (fun x -> not (x.StartsWith("# ")))
        |> String.concat "\n"

    {
        frontmatter = frontmatter
        title = title
        body = body
    }
```

`"# "` で始まる行をとってくる、というこれまたゴリ押し実装である。`FSharp.Formatting.Markdown` のパース結果を活用した方が綺麗だが、一旦この実装で凌ぐ。Frontmatter については、さらに `YamlDotNet` でパースして投稿日を抽出する。これらの値を HTML に埋め込んでやれば、記事ページはひとまず完成である。

次に、記事一覧ページを実装する。これも手書きした HTML ファイルをベースにやっていく。

```fsharp
let renderPost (post: Postloader.Post) =
    let published = post.published.ToString("yyyy-MM-dd")
    let href = Lib.postHref post.key

    $"""
    <article>
        <time datetime="{published}">{published}</time>
        <h1><a href="{href |> WebUtility.HtmlEncode}">{post.title |> WebUtility.HtmlEncode}</a></h1>
        
        <p class="post-summary">
            {post.summary |> WebUtility.HtmlEncode}
        </p>
    </article>
    """

let generate (ctx : SiteContents) (_) (_) =
    let posts = ctx.TryGetValues<Postloader.Post> () |> Option.defaultValue Seq.empty |> Seq.sortByDescending (fun p -> p.published)

    Lib.layout "pizzacat83's blog" $"""
<div class="post-list">
    <div>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </div>
</div>
    """ ["/assets/index.css"]
```

記事一覧ページのヘッダーやフッター等は、記事ページと共通である。そういった共通の枠の部分は `Lib.layout` 関数に切り出した。

続いて RSS を生成する。これも手書きしたものがあるので、温かみのあるコピペを `Seq.map` に置換すれば良い。理想的には XML のシリアライズライブラリを使って RSS を生成するのが綺麗だが、ここも一旦はゴリ押しで行く。

```fsharp
let renderPost (post: Lib.LocalizedPost) =
    let published = post.published.ToString("ddd, dd MMM yyyy")
    let href = Lib.postHref post.key

    $"""
    <item>
        <title>{post.title |> SecurityElement.Escape}</title>
        <link>https://blog.pizzacat83.com{href |> SecurityElement.Escape}</link>
        <description>{post.summary |> SecurityElement.Escape}</description>
        <pubDate>{published |> SecurityElement.Escape}</pubDate>
    </item>
    """

let generate (ctx : SiteContents) (_) (_) =
    let posts =
        Lib.getPrimaryLocalizedPosts ctx
        |> Seq.sortByDescending (fun p -> p.published)

    $"""<?xml version="1.0" encoding="UTF-8" ?>
<rss version="2.0">
    <channel>
        <title>pizzacat83's blog</title>
        <link>https://blog.pizzacat83.com</link>
        <description></description>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </channel>
</rss>
    """
```

こんな感じでサイトを一通り構築できた。私は途中で Fornax 自体の実装をガチャガチャし始めた (後述) ので1日ぐらいかかったが、そういう脱線をしなければ数時間で構築できる、学習コストが低くて簡潔なフレームワークだという実感がある。

### 謎エラーに出くわし、Fornax をフォーク

上記のように F# スクリプトを書いてサイトを構築する過程で、謎のエラーに遭遇した。

```
Load Errors: [|input.fsx (1,1)-(1,60) interactive warning Accessing the internal type, method or field 'item' from a previous evaluation in F# Interactive is deprecated and may cause subsequent access errors. To enable the legacy generation of a single dynamic assembly that can access internals, use the '--multiemit-' option.|]
```

結末から述べると、Fornax を .NET 8 でビルドして .NET 8 で実行したところ解決した。`dotnet tool install fornax` で手に入るのは .NET 6 でビルドされたものだが、私のマシンには .NET 8 しかインストールされていない (.NET 6 のサポートが終了しているため)。このバージョン差異が何か悪さをしていたのかもしれない。

人間とは不思議なもので、Fornax のソースコードをダウンロードしてビルドしインストールすると、Fornax のソースコードに手を加えたいという衝動が芽生えるのである。サイト生成スクリプトを実装する過程で、Fornax の改善したい点がいくつか浮かんでいたから、Fornax の実装をいじり始めた。

### Fornax の実装を読み解く

Fornax はサイト生成のロジックをユーザーのスクリプトに委ねているので、Fornax 自体のコードベースはさほど大きくなく、核心的な処理は1000行に満たない。望む挙動を実現するにはどこのコードをどのように変えれば良いのかは、かなりすんなりと見つけることができた。

Fornax が F# スクリプトを実行する流れをざっくり見ていく。`Fornax.Generator.GeneratorEvaluator.evaluate` 関数は、generator スクリプトを実行してその結果 (生成すべきファイルのバイト列) を返す関数である。

```fsharp
let evaluate (fsi : FsiEvaluationSession) (siteContent : SiteContents) (generatorPath : string) (projectRoot: string) (page: string)  =
    // ... snip ...
    // スクリプトをロードし、スクリプト内の generate 関数を表す FsiValue を得る
    let generator =
        getGeneratorContent fsi generatorPath
        |> Result.bind (compileExpression >> Ok)
    // ... snip ...

    // generator 関数に引数を与えて呼び出す
    generator
    |> Result.bind (fun generator ->
        let result = invokeFunction generator [box siteContent; box projectRoot; box page ]
        // 関数の返り値を string 型にキャストする
        result
        |> Option.bind (tryUnbox<string>)
        // ...
```

スクリプトをロードして、スクリプト内の `generate` 関数を取り出す処理である `getGeneratorContent` は以下のように実装されている。

```fsharp
let private getGeneratorContent (fsi : FsiEvaluationSession) (generatorPath : string) =
    // ... snip ...
    let _, loadErrors = fsi.EvalInteractionNonThrowing(sprintf "#load \"%s\";;" load)
    // ... snip ...
    let _, openErrors = fsi.EvalInteractionNonThrowing(sprintf "open %s;;" filename)
    // ... snip ...
    let funType, layoutErrors = fsi.EvalExpressionNonThrowing "<@@ fun a b -> generate a b @@>"
    // ... snip ...
```

`fsi.EvalInteractionNonThrowing` メソッドが、F# Interactive で式を評価するメソッドである。例えば `generators/post.fsx` というスクリプトがあるとき、`getGeneratorContent` は以下のような式を評価して、`generate` 関数を取り出す。

1. `#load "/path/to/generators/post.fsx"`
2. `open Post;;`
3. `<@@ fun a b -> generate a b @@>`

3で評価するのがなぜ `generate` でも `<@@ generate @@>` でもなく `<@@ fun a b -> generate a b @@>` なのかはまだ深掘りする余地がありそうだが、ともかくこのようなスクリプトを実行すれば、 `generators/post.fsx` に定義された `generate` 関数を取り出せる。

続いて、この `generator` 関数に引数を与えて呼び出す `invokeFunction` 関数は以下のような実装となっている。

```fsharp
let internal invokeFunction (f : obj) (args : obj seq) =
    // Recusive partial evaluation of f, terminate when no args are left.
    let rec helper (next : obj) (args : obj list)  =
        match args with
        | head::tail ->
            let fType = next.GetType()
            if FSharpType.IsFunction fType then
                let methodInfo =
                    fType.GetMethods()
                    |> Array.filter (fun x -> x.Name = "Invoke" && x.GetParameters().Length = 1)
                    |> Array.head
                let res = methodInfo.Invoke(next, [| head |])
                helper res tail
            else None // Error case, arg exists but can't be applied
        | [] ->
            Some next
    helper f (args |> List.ofSeq )
```

なんとなくリフレクションを使って関数を適用していることがわかる。`invokeFunction` は `obj option` を返すので、`Option.bind (tryUnbox<string>)` のようにして具体的な型にキャストできる。

驚いたのは、Fornax 本体と F# スクリプトの間でオブジェクトがやり取りされていることである。Fornax は F# スクリプトをロードして取り出した `generate` 関数に対し、`siteContent` などのオブジェクトを渡している。これはプリミティブではなく、クラス `Fornax.Core.Model.SiteContents` のインスタンスである。そして F# スクリプト内では、引数 `siteContent` の `GetValues` をはじめとするメソッドを普通に呼び出せる。

```fsharp
let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.map (fun (post) ->
            // ...
```

さらに、この `generate` 関数の返り値を Fornax 本体が受け取ってサイトを出力する。このように、コンパイルされたプログラムとスクリプトの間で、共通の型定義に沿って双方向的にオブジェクトをやり取りでき、メソッドの呼び出しもできるというのは、私にとっては新鮮な体験だった。このような仕組みは静的サイトジェネレータに限らず scriptable なソフトウェアを作る際の強力な武器になると思う。

### Fornax の実装をいじる

Fornax の内部的な仕組みがざっくり掴めたところで、Fornax について感じていた改善点に手をつけていく。

まず、Fornax のローカルサーバーに「`{SERVER}/foo` にアクセスすると `foo/index.html` のコンテンツを返す」という挙動を持たせたかったので、これを[実装した](https://github.com/pizzacat83/blog/commit/8690103c7f39de03a6015778994dd6550c8934ae)。ローカルサーバーは Suave で実装されているので、ルーターをよしなに実装する。

```fsharp
let router basePath =
    let pubdir = Path.Combine(basePath, "_public")
    choose [
        Files.browse pubdir
        path "/websocket" >=> handShake ws
        (fun ctx ->
            // return ./foo/index.html when /foo is requested
            let newPath = Path.Combine(ctx.request.path, "index.html")
            Files.browseFile pubdir newPath ctx
        )
    ]
```

また、サイト生成におけるエラーハンドリングにも課題を感じていた。Fornax では `SiteContents::AddError` メソッドによってエラーを報告できる。しかしこれには以下の問題を感じていた。

- サイト再生成時に登録済みのエラーがクリアされず、蓄積されてしまう
    - → サイト再生成時にリセットするよう[修正](https://github.com/pizzacat83/blog/commit/4df1975dea0004fb2861798a76fb6cf528b16a9a)
- `AddError` でエラーを報告してもサイト生成処理が中断されず、そのまま後段の処理が実行されてしまう
    - → エラーがある場合後段の処理をスキップするよう[修正](https://github.com/pizzacat83/blog/commit/db297e6dcd871b26caa70bc9288dfb5f78d62980)
- F# スクリプトが例外をキャッチし忘れていると、Fornax 自体がクラッシュしてしまう
    - → F# スクリプト由来の例外を適切にハンドルするよう[修正](https://github.com/pizzacat83/blog/commit/dbf0545c3b052dbc2e3c30aecd9247ceab5f807b)

ただ、個人的な思想としては、`AddError` メソッドの呼び出しによってエラーを報告するのではなく、 F# スクリプトで `T -> Result<string, string>` のようなシグネチャで関数を定義することでエラーを伝達したい。この点は今後対応していきたいと思っている。

これらの変更点について、「本家に Pull Request を出さないのか？」と思う人がいるかもしれない。問題は .NET SDK のバージョンで、私の環境に入っているのはサポートが継続している .NET 8 だけだが、Fornax 本家のリポジトリは .NET 6 で構成されている。.NET 6 で動くかどうか確証のないパッチを提案するのは躊躇する。Fornax 本家の .NET バージョンを上げれば良いと思われるかもしれないが、これも簡単ではない。Fornax 本家はビルド自動化に FAKE を使っているが、FAKE は .NET 8 に対応していない。したがって FAKE を .NET 8 に対応させるか、Fornax 本家に FAKE から脱却する提案をするしかない。いずれもちょっと大変なので、Pull Request は出さずに、自分のリポジトリの中で色々実装に手を加えていくことにした。

## おわりに

紆余曲折をしまくった結果、Fornax をベースに F# で実装したブログサイトが出来上がり、こうして公開することができた。F# を書くのはほとんど初めてだったが、とても快適に実装を進めることができ、かなり好きな言語になった。

F# に入門したいが作りたいものが思いつかなくて困っているという人には、Fornax を使ってブログサイトを生成することをお勧めする、と言いたいところだが、.NET 8 で実行した時の謎エラーがあるので今はお勧めできないのが悔やまれる。謎エラーが出ても果敢に立ち向かうパッションがあるなら、Fornax をフォークしたり[私がフォークしたもの](https://github.com/pizzacat83/blog/tree/main/fornax)を参考にしたりするといいだろう。

さてブログサイトの実装については、まだまだやり残していることがいくつか浮かんでいる:

- シリーズ機能 (「〇〇やってみた 第n回」のようなシリーズものについて、それに属する記事を一覧できる)
- 記事ページに関連記事へのリンクを掲載する
- 外部サイトをブログカードとして埋め込む
- Markdown の脚注記法に対応する
- 見出しに id 属性をつけ、見出しへのリンクをコピーできるようにする
- 記事の目次を出せるようにする
- モバイル向けにスタイルを調整する
- 記事が多くなってきたらページネーションを入れる
- 諸々のリファクタリング

このように色々な方面でやりたいことがあるが、その実現方法は明らかで、「F# で実装するだけ」である。Fornax の簡潔さと自由度はありがたい。

ジェネレータの実装だけでなく、ブログを書きたいと思っているネタもいくつか溜まっている。そもそもこのブログサイトは英語記事を載せるために作ったのだが、肝心の英語記事がまだ書き終わっていない。いくらジェネレータを実装しても、コンテンツが充実しなければ意味がない。引き続き、ジェネレータとコンテンツを並行して育てていこうと思う。

では、次の記事をお楽しみに。
