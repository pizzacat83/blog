---
published: 2025-12-25
summary: TLS 1.3 サーバーを、なるべくライブラリに頼らずに Haskell で実装している。まだ完動には至っていないものの、その過程での楽しさを共有したい。
head: |
  <meta property="og:image" content="https://blog.pizzacat83.com/ja/posts/2025-12-25-handmade-tls-1-3-server-in-haskell/eyecatch.png">
  <meta name="twitter:card" content="summary_large_image">
---
# Haskell で TLS 1.3 サーバーを実装している (現在進行形)

この記事は Haskell Advent Calendar 2025 の第 25 日目の記事です。

現在、Haskell で TLS 1.3 サーバーの実装に取り組んでいます。目標は、Chrome で `https://localhost:8443` にアクセスすると「Hello over TLS!」と表示されるような、簡単な HTTPS サーバーが動くことです。

ソースコードを [pizzacat83/tails](https://github.com/pizzacat83/tails) にて公開しています。なおタイトルの通り、**まだ動いていません** (アドカレ期間内に動かしたかった…😣)。

![Tails logo](./tails-logo.svg)

↑なんとなくロゴも作った。その結果、λ って猫だなと気づいた

このプロジェクトでは、TLS 1.3 の仕組みの理解を深められるよう、なるべくライブラリに依存せず大部分の処理をスクラッチ実装しています。なお、HMAC など暗号関連のプリミティブや、TCP でデータを送受信する処理はライブラリを使用しています。また、実装の進め方は「[エムスリーテックブック8](https://techbookfest.org/product/b94hFWewG7fVRLgqEmSjT1)」の第10章「フルスクラッチして理解するSSL/TLS」(Node.js による TLS 1.3 サーバーの実装) を参考にしています。

この記事では、実装に関する説明というよりは、実装に取り組む過程についての色々を書いていきます (動いていない実装の説明をしても仕方がないので……)。実装が完動したらまた記事を書くつもりです。

## きっかけ

ある日、「フルスクラッチして理解するSSL/TLS」をみんなで読む会をやろう、と[ひがきさん](https://x.com/higaki_program)が呼びかけていました。ちょうどその頃、なんやかんやあって「Haskell や Scala を勉強するために何か実装してみたい」とぼんやり思っていました。ということは、TLS サーバーを Haskell で実装すれば一石二鳥なのではないか！直感的に「TLS サーバーの実装は、TCP でパケットを送受信する関数と、暗号プリミティブの関数の存在を仮定すれば、あと実装するべきはその間をつなぐロジックだろうから、それは Haskell で書くのにぴったりそうだ」と予想し、その日に本を買ってリポジトリを作り、実装を始めました。

並行して自作ブラウザ on 自作 OS in Rust を進めていることを考えると、Haskell ではなく Rust で実装して「自作ブラウザ on 自作 OS で HTTPS サイトを見る」をゴールに据えるのも面白そうではありましたが、そんなことを言っているとなんでも Rust を採用したくなってしまい、Haskell を書く機会がなくなってしまうだろうということで、自作ブラウザ in Rust のことは一旦忘れ、Haskell での実装を決めました。(でも Haskell 実装が落ち着いたら Rust 実装にチャレンジしても面白そう)

結果的に、Haskell を学習する題材としてかなり良いものだったと、予想以上に感じています (まだ実装が動いていないのに……??)。確かに TLS サーバーの実装は「TCP のデータ送受信や暗号のプリミティブをつなぐロジックの実装」ではあるのですが、stateful なロジックが思った以上にあったりと、純粋関数としてナイーブに表現できるわけではない概念に色々ぶち当たってきました。こういったものをどう純粋関数に落とし込むかを考えるのが、なんというかパズル的で楽しいですね。より詳しくは、「難しくも面白い/面白そうなポイント」に書いていきます。

## 参考になる資料たち

「フルスクラッチして理解するSSL/TLS」をもとに実装のスコープを定め、[RFC 8446](https://datatracker.ietf.org/doc/html/rfc8446) を参照して実装するという進め方をしています。TLS 1.3 の仕様書だけでは「結局何を実装すれば動くものが出来上がるのか」をすぐに掴むことが難しいので、「仕様書のこの部分だけこんな感じで実装しておけば OK」みたいなゴール設定を与えてくれる「フルスクラッチして理解するSSL/TLS」は、実装言語が違うとはいえ本当に助かっています。

また実装の設計を考える際は、Haskell ライブラリの [tls](https://hackage.haskell.org/package/tls) と、Rust ライブラリの [rustls](https://crates.io/crates/rustls) を参考にしています。意外なことに、ハンドシェイクのコードを追っていると Haskell ライブラリの方は逐次的なコードの見た目をしている一方で、Rust ライブラリの方はステートマシンを型で表現していて、どちらも参考になります。DeepWiki という素晴らしいものがあるいい時代になったので、自作で詰まった時に世の中のライブラリでどう実装されているかを簡単に知ることができて捗りますね。

## 難しくも面白い/面白そうなポイント

### フレームを跨ぐフレーム

TLS ハンドシェイクのメッセージは、以下の階層構造になっています。

- TCP payload
	- TLS Record
		- TLS Handshake

つまり、TCP のヘッダを取ると TLS Record があり、TLS Record のヘッダを取ると TLS Handshake がある (そしてその中に鍵交換のデータなどがある) わけです。

ここで核心なのが、「TCP payload のデータ単位」「TLS Record のデータ単位」「TLS Handshake のデータ単位」がそれぞれ独立であることです。TCP の `recv` 関数を呼び出して出てきたバイト列は、運が良ければ1つの TLS Record の始端から終端までかもしれないですが、終端まで出てこず中途半端な TLS Record が出てくるかもしれないし、逆に1回の recv で複数の TLS Record が出てくることもありえます。さらに、TLS Record のボディ部分は必ずしもちょうど1つの TLS Handshake メッセージの始端から終端までに対応するとは限りません。1つの Handshake メッセージの途中から途中までしか入っていないこともありえますし、逆に複数の Handshake メッセージが詰め込まれていることもありえます。「まず TCP から recv して、そのバイト列を TLS Record としてパースして…」みたいなナイーブな実装方針をとってしまうと、上位の層から見た中途半端なフラグメントの扱いで苦しくなりがちです。

今考えている実装方針は以下のようなものです (`M1`, `M2` は何らかのモナド)。

- `recvHandshake :: M1 TLSHandshake` は、TLS Handshake 1 個を受信する計算。Handshake 1 個の末尾が届くまで `recvRecord` を繰り返し呼び出す。Handshake 1 個の後に続く Handshake の断片まで届いてしまった場合は状態モナドのような感じで溜めておく。
- `recvRecord :: M2 TLSRecord` は、TLS Record 1 個を受信する計算。Record 1 個の末尾が届くまで TCP の `recv` を繰り返し呼び出す。Record 1 個の後に続く Record の断片まで届いてしまった場合は状態モナドのような感じで溜めておく。

なんとなくこれでうまくいきそうな気はしていますが、ServerHello の後からは TLS Record の復号もしないといけないので、これから実装を進めないとまだわからないですね。

要は TCP のレイヤーと TLS Record のレイヤーをそれぞれバイトストリームとして表現するのがコツだろうと思うので、「`recv` 的な関数を定義する」以外にもいくらか良い方針があるだろうと思っています。

ネットワークプログラミングに親しみのある人であれば「普通のこと」かもしれないですが、私はそうでもない (ソースコードのパースとかはいくらかやってきたが) ので、これが結構新鮮でした。

### ハンドシェイクの状態を綺麗に扱う

ハンドシェイクを実装していると、状態に思いを馳せる時が色々あります。

なんといっても ClientHello, ServerHello と来てその次以降のメッセージは暗号化しなければならないので、暗号鍵を導出しなければなりません。その導出方法に関する仕様を読むと、"Key Schedule" なる概念が出てきます。

```
             0
             |
             v
   PSK ->  HKDF-Extract = Early Secret
             |
             +-----> Derive-Secret(., "ext binder" | "res binder", "")
             |                     = binder_key
             |
             +-----> Derive-Secret(., "c e traffic", ClientHello)
             |                     = client_early_traffic_secret
             |
             +-----> Derive-Secret(., "e exp master", ClientHello)
             |                     = early_exporter_master_secret
             v
       Derive-Secret(., "derived", "")
             |
             v
   (EC)DHE -> HKDF-Extract = Handshake Secret
             |
             +-----> Derive-Secret(., "c hs traffic",
             |                     ClientHello...ServerHello)
             |                     = client_handshake_traffic_secret
             |
             +-----> Derive-Secret(., "s hs traffic",
             |                     ClientHello...ServerHello)
             |                     = server_handshake_traffic_secret
             v
       Derive-Secret(., "derived", "")
             |
             v
   0 -> HKDF-Extract = Master Secret
             |
             +-----> Derive-Secret(., "c ap traffic",
             |                     ClientHello...server Finished)
             |                     = client_application_traffic_secret_0
             |
             +-----> Derive-Secret(., "s ap traffic",
             |                     ClientHello...server Finished)
             |                     = server_application_traffic_secret_0
             |
             +-----> Derive-Secret(., "exp master",
             |                     ClientHello...server Finished)
             |                     = exporter_master_secret
             |
             +-----> Derive-Secret(., "res master",
                                   ClientHello...client Finished)
                                   = resumption_master_secret
```

([RFC 8446](https://datatracker.ietf.org/doc/html/rfc8446#section-7.1) より引用)

最初に見た時は面食らいましたが、とりあえず Haskell に翻訳してみると、どうも全てのシークレットを導出するには、入力として `PSK` (pre-shared key), `(EC)DHE` (Diffie-Hellman アルゴリズムの出力) の他に、`ClientHello...ServerHello`,  `ClientHello...client Finished` など、「これまでのメッセージログ」が必要だとわかります。ただ、一番上の `binder_key` は ClientHello メッセージを構築する時に必要ですが、真ん中の `client_handshake_traffic_secret` は ClientHello と ServerHello の両方を入手してようやく手に入るものです。つまり、ハンドシェイクのプロセスが進むにつれて徐々にシークレットが導出されていく、中断可能なフローってこと[^key-schedule-state]…? と思い、ステートマシンとして表現することを考え始めました。

[^key-schedule-state]: 実は今回の実装スコープでは全ての secret を利用するわけではなく、かつサーバー側のみ実装することから、実は中断可能にしなくても動くものは作れます。ただ、仕様書に書かれている概念を Haskell にうまく写し取ろうとする試みって楽しいじゃないですか。

考えを進めていくと、そのステートマシンにおける状態とは、`Early Secret`, `Handshake Secret`, `Master Secret` そのものではないかと気づきました。すると、RFC 8446 のあの図は、以下の関数の矢印を束ねたものに見えてきます:

- `EarlySecret -> ... -> BinderKey`
- `EarlySecret -> ... -> ClientEarlyTrafficSecret`
- ...
- `EarlySecret -> DHE -> HandshakeSecret` (**状態遷移**)
- `HandshakeSecret -> ... -> ClientHandshakeTrafficSecret`
- ...
- `HandshakeSecret -> MasterSecret` (**状態遷移**)
- `MasterSecret -> ... -> ClientApplicationTrafficSecret0`
- ...

というわけでこれらの関数を実装しました。

次の問題は、先ほど `...` と省略した引数です。例えば `ClientHandshakeTrafficSecret` を得るためには、`HandshakeSecret` の他に、`ClientHello...ServerHello` つまり ClientHello から ServerHello までのメッセージ列 (のハッシュ) が必要です。

そのメッセージ列を単に `ByteString` で表すことにして `HandshakeSecret -> ByteString -> ClientHandshakeTrafficSecret` を定義するという選択肢はありますが、「ClientHello から ServerHello までのメッセージ列のみを受理し、そうでないメッセージ列は受理しない」というシグネチャにできたら、より TLS 1.3 の仕様を反映できていて嬉しそうです。

というわけで、「どこまでのメッセージが入っているか」を区別できる型を用意しました。`THContext p` は、`p` が指す範囲のメッセージログのハッシュ状態を示す型です。`makeTHUntilServerFinished` は、「CertificateRaw までのメッセージログのハッシュ状態」と「CertificateRaw より後、ServerFinished 以前」のメッセージを受け取り、「ServerFinished までのメッセージログのハッシュ状態」を返す関数です。ここでは、DataKinds 拡張と KindSignatures 拡張を使っています。いやあ、Haskell でこういうの書けるんですね。楽しい！

```haskell
data TSPhase
  = TS_EMPTY | TS_SH | TS_CR | TS_SF | TS_CF
  deriving (Show, Eq)

newtype THContext (p :: TSPhase) = THContext (Crypto.Hash.Context Crypto.Hash.Algorithms.SHA384)

transcriptHash :: THContext p -> ByteString
transcriptHash (THContext ctx) = BA.convert $ Crypto.Hash.hashFinalize ctx

makeTHUntilServerFinished :: THContext 'TS_CR -> ServerCertificateRaw -> ServerCertificateVerifyRaw -> ServerFinishedRaw -> THContext 'TS_SF
makeTHUntilServerFinished (THContext ctx) (ServerCertificateRaw cert) (CertificateVerifyRaw cv) (FinishedRaw fin) =
  THContext $ Crypto.Hash.hashUpdate (Crypto.Hash.hashUpdate (Crypto.Hash.hashUpdate ctx cert) cv) fin
```

というわけでなんとか Key Schedule を Haskell コードで表現することができました。つまり、ServerHello より後のメッセージを暗号化するための情報が手に入ったわけです。ゼエゼエ

じゃあ次のメッセージを暗号化して送ろうという話になりますが、暗号化方法の仕様を読んでいると、暗号化に必要な nonce は上記 KeySchedule だけから導出されるわけではなく、「今まで何個メッセージを送ってきたか」のシーケンス番号も必要である (おかげで、nonce が再利用されない) ことがわかります。つまり、"receive" "send"という操作をするたびにインクリメントされる状態を扱う必要も出てきました。

というわけで暗号化された Handshake メッセージの送受信は色々とやることがあるわけですが、その複雑な操作が、単なる `recvHandshake :: M Handshake`,  `sendHandshake :: Handshake -> M ()` のような関数として表現できたら、ハンドシェイクの一連の流れがすっきり記述できそうな気もします。ところで ServerHello 以前のメッセージは平文でやり取りするわけですが、それは `sendHandshakePlaintext` と `sendHandshakeEncrypted` という2つの関数を使い分けるべきでしょうか？それとも、`sendHandshake` という一つの関数があって、それがハンドシェイクのフェーズという状態に依存して平文か暗号文かを切り替えるのがよいでしょうか？

設計をする際には「TLS サーバーの仕組みがなるべく読み解きやすいようにする」「仕様上正しくないことがなるべく型検査で弾かれるようにする」の2点を意識して考えていますが、その過程で Haskell の色んな言語機能やイディオムに触れることができて刺激的ですね。

## 実装を3周ぐらいしてみたい

TLS サーバーの実装は1回完動させて終わりではなく、一度コードベースを捨てて作り直すことを3周ぐらいするとなお面白いんじゃないかと思っています (まだ1周も終わっていないのに……?)

単に同じことを3回やるわけではなく、以下のように実装の進め方を変えてやり直すことで、色々な角度から TLS サーバーの仕組みや設計の勘所を掴めるのではないかと思っています。

1. とりあえず、ライブラリをなるべく使わずに動くものを作ってみる (←今)
2. まず、全然自作せずに Haskell の一般的なライブラリに依存して HTTPS サーバーを実装する。徐々に、ライブラリ仕様箇所を自分の実装で置き換え、依存ライブラリを減らしていく
	- この過程で、先人の設計や実装を観察する
3. ここまでの学びを踏まえて、もう一度自作してみる
	- 1. ではスコープ外としていた、複数の暗号アルゴリズムに対応するための抽象化やアルゴリズムのネゴシエーションなども実装してみる

TLS サーバーの実装は簡単ではないですが分量がめちゃくちゃ多いわけでもないので、「1回捨てて作り直す」という楽しみ方ができる題材だと予想しています。実際、「フルスクラッチして理解するSSL/TLS」の著者の末永さんは、[執筆にあたりコードを2回作り直した](https://www.m3tech.blog/entry/2025/05/26/100000) (つまり3周した) ようです。

あと、TLS クライアントの実装もしてみたいですし、やっぱり Rust での実装もしてみたいですね。

## おわりに

TLS 1.3 サーバーの Haskell 実装は楽しいのでぜひやってみましょう！！
