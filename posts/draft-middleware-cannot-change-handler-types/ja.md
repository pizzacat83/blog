---
draft: true
published: 2026-07-15
summary: 認証ミドルウェアを書くたびに req.user の型で悩んでいた。Haskell の Web フレームワーク Servant は「ハンドラの型を変えられるのに合成可能」という設計でこの悩みを解いている。関数型まつり 2026 の LT の完全版 + 続編で内部実装を深掘りしていくシリーズの第 1 回。
---
# ミドルウェアはハンドラの型を変えられない: Servant の世界観

この記事は、関数型まつり 2026 の LT「[Haskell/Servant を通して Web ミドルウェアを捉え直す](https://fortee.jp/2026fp-matsuri/proposal/a114cef5-c18d-4ad5-ad34-ec7c8b6d5583)」([スライド](https://speakerdeck.com/pizzacat83/servantwotong-sitewebmidoruueawozhuo-ezhi-su)) を、10 分の尺で話しきれなかった行間も含めて書き直したシリーズの第 1 回です。
LT の主旨はこの第 1 回だけで伝わるように書いています。
第 2 回以降では、LT では触れられなかった Servant の内部の仕組みを読んでいく予定です。

想定読者は、普段 TypeScript や Go で Web アプリケーションを書いていて、型や関数型プログラミングが好きな人です。
Haskell の経験は仮定しません。
Haskell の記法は、登場したその場で説明していきます。

## きっかけ

仕事で Go で Web API を書いていて、認証ミドルウェアの型設計に悩んでいました。
やりたいのは「認証で判明したユーザー情報を、実行時チェックに頼らず型で保証してハンドラに渡す」、それだけです。
それだけのことが、どうもうまくいかないのです。

同じ問題を Haskell の Web フレームワーク [Servant](https://www.servant.dev/) がどう扱っているのか覗いてみたところ、面白いことをやっていました。
この記事では、まず TypeScript (Express) でこの悩みを再現し、それから Servant の解き方を眺めます。

## `req.user` の型パズル

認証つきの API を Express で実装してみましょう。
認証ミドルウェアがトークンを検証し、判明したユーザー情報を `req.user` に代入してハンドラに伝える、というよくある構成です。

```ts
// middleware/auth.ts
export const authenticate = (
  req: Request,
  res: Response,
  next: NextFunction,
) => {
  const header = req.header("Authorization");
  if (!header?.startsWith("Bearer ")) {
    return res.sendStatus(401);
  }

  const token = header.slice(7);

  try {
    const payload = jwt.verify(token, JWT_SECRET) as JWTPayload;

    req.user = {
      id: payload.sub,
      email: payload.email,
    };

    next();
  } catch {
    return res.sendStatus(401);
  }
};
```

```ts
// routes.ts
router.get(
  "/whoami",
  authenticate,
  (req, res) => {
    res.json({
      id: req.user.id,
      email: req.user.email,
    });
  },
);
```

これはコンパイルが通りません。
`req.user` に赤い波線が出ます。

```
Property 'user' does not exist on type 'Request<ParamsDictionary, any, any, ParsedQs, Record<string, any>>'.ts(2339)
```

Express の `Request` 型に `user` というフィールドはないので、当然のエラーです。
こういうときの定番は、型定義の上書きで `Request` に `user` フィールドを生やすことです。

```ts
// express.d.ts
declare global {
  namespace Express {
    interface Request {
      user?: AuthedUser;
    }
  }
}
```

これで解決のはずでした。
ところが、ミドルウェア側のエラーが消えた代わりに、今度はハンドラ側で別のエラーが出ます。

```
'req.user' is possibly 'undefined'.
```

`user` はオプショナルなフィールドなのだから、`req.user.id` と書くには undefined でないことを示せ、というわけです。
言い分はわかります。
しかしこのハンドラは認証ミドルウェアを通った後にしか呼ばれないので、`req.user` は絶対にセットされています。
プログラマにはそれがわかっているのに、型チェッカを納得させる手段がありません。

型チェッカに納得してもらう手は 2 つ考えられます。
1 つ目は、実行時チェックを書くことです。

```ts
(req, res) => {
  if (!req.user) {
    throw new Error("unreachable");
  }
  // これで req.user は AuthedUser 型
}
```

この if 文は絶対に踏まれません。
踏まれないとわかっているコードを、型チェッカのためだけに全ハンドラへ書いて回るのは屈辱的ですし、「認証を通ったからここに来た」という大事な事実がコード上はただの実行時チェックに化けてしまいます。

2 つ目は、いっそ `user` を必須フィールドにしてしまうことです。

```ts
declare global {
  namespace Express {
    interface Request {
      user: AuthedUser;
    }
  }
}
```

エラーは全部消えます。
しかし今度は、認証ミドルウェアを噛ませていないエンドポイントにまで `user` が生えてしまいます。

```ts
router.get(
  "/signup",
  (req, res) => {
    // 認証していないのにエラーにならない。あれ？
    console.log(req.user.id);
  },
);
```

認証前の `req.user` は実際には undefined なので、これは型が嘘をついている状態 (unsound) です。
型チェッカが通っているのに実行時に落ちる、という型の恩恵と真逆の事態を招きます。

## 本当は型で何を主張したいのか

2 つの回避策は、嫌さの向きこそ違いますが、根は同じです。
本当に主張したい性質を、型で表現できていないのです。
主張したいのは「**認証を噛ませたハンドラは、認証成功時のみ呼ばれる**」という性質です。
セキュリティ的にも大事な性質なので、できれば型チェッカに守ってほしいところです。

もしハンドラのシグネチャが `(user: AuthedUser, req: Request, res: Response) => ...` だったらどうでしょう。
このハンドラを呼び出せるのは、`AuthedUser` 型の値を用意できたとき、つまり認証に成功したときだけです。
`AuthedUser` を「認証成功の証拠」として引数で受け取る形にできれば、望みの性質が型そのものに現れます。

ここで問題の正体が見えてきます。
認証ミドルウェアを噛ませると、ハンドラの契約が変わります (認証済みユーザーを前提にしてよい)。
だからハンドラの型も変わってほしいのです。
ところが、よくあるフレームワークでは、ミドルウェアはハンドラの型を変えられません。

## ミドルウェアの型をじっと見る

「変えられない」とはどういうことか、ミドルウェアの型を見てみます。
Go や TypeScript のフレームワークで、ハンドラとミドルウェアの型はだいたいこんな形をしています。

```ts
type Handler = (req: Req, resp: RespWriter) => Promise<void>;

type Middleware = (
  req: Req,
  resp: RespWriter,
  next: (req: Req, resp: RespWriter) => Promise<void>,
) => Promise<void>;
```

この `Middleware` の型をじっと見ると、別の形が見えてきます。
引数の順番を入れ替えて、

```ts
type Middleware = (
  next: (req: Req, resp: RespWriter) => Promise<void>,
  req: Req,
  resp: RespWriter,
) => Promise<void>;
```

`next` の部分だけカリー化 (引数を 1 つ受け取った時点で残りの引数を待つ関数を返す形に変形) すると、

```ts
type Middleware = (next: (req: Req, resp: RespWriter) => Promise<void>)
  => (req: Req, resp: RespWriter)
  => Promise<void>;
```

つまり `Middleware` とは `Handler => Handler`、ハンドラを受け取ってハンドラを返す関数です。

このモデル化には美点があります。
入力と出力が同じ型なので、ミドルウェアはいくつでも関数合成でつなげられて、つないだ結果もまた `Handler => Handler` になります。
みんなが同じ形をしているおかげで、誰かの書いた認証ミドルウェアと別の誰かの書いた CORS ミドルウェアを、好きな順で 1 つのアプリケーションに組み込めるわけです。
実際、Express はアプリケーションをミドルウェアの合成そのものだと説明しています[^wai]。

> An Express application is essentially a series of middleware function calls.
>
> ([Using middleware - Express](https://expressjs.com/en/5x/guide/using-middleware/))

[^wai]: この捉え方は Haskell にもあります。Servant の土台になっている低レベルライブラリ WAI では、ミドルウェアは `type Middleware = Application -> Application` と定義されていて、まさに「型を保つミドルウェア」です。

まとめると、ミドルウェアは「ハンドラの型を保つ変換」としてモデル化されがちで、それは合成可能性という利点があるからです。
一方で、ミドルウェアはハンドラの契約を変えうるのに、型を保つモデルでは契約の変化を型に反映できません。
`req.user` のパズルはこの制約の現れだったのです。

## ハンドラの型を変えるミドルウェアを考えてみる

では、ミドルウェアがハンドラの型を変えてよいことにしたら、何が起きるでしょうか。
認証ミドルウェアを、素朴に「`AuthedUser` 付きハンドラを受け取って、ふつうのハンドラを返す変換」としてモデル化してみます。
一方で、CORS のように型を変えないミドルウェアもあります。

```ts
type AuthMiddleware = (handler: (user: AuthedUser, req: Req, resp: Resp) => Promise<void>)
  => (req: Req, resp: Resp)
  => Promise<void>;

type CorsMiddleware = (handler: (req: Req, resp: Resp) => Promise<void>)
  => (req: Req, resp: Resp)
  => Promise<void>;
```

この 2 つを組み合わせてみると、順序によって合成コードの形が変わってしまいます。
CORS を外側に置く (CORS チェックの後に認証する) 場合は、関数適用を重ねるだけです。

```ts
const handler = (user: AuthedUser, req: Req, resp: Resp) => { /* ... */ };

const app = corsMiddleware(authMiddleware(handler));
```

逆に認証を外側に置きたい場合、`corsMiddleware(handler)` とは書けません。
`corsMiddleware` は `AuthedUser` 付きハンドラを受け取れないからです。
`user` を手作業で内側に配線する必要があります。

```ts
const app = authMiddleware((user, req, resp) =>
  corsMiddleware((req2, resp2) => handler(user, req2, resp2))(req, resp),
);
```

2 つの部品を「つなぐだけ」では済まなくなりました。
今回は引数が 1 つ増えるだけのミドルウェアだったからこの程度の配線で済みましたが、もっと激しく型を変えるミドルウェアが現れたら、合成はどう定義すればよいのでしょうか。

「ミドルウェアは自由にハンドラの型を変えてよい」という無秩序を許すと、部品の形が揃わなくなり、2 つの部品を合成する統一的な方法が非自明になります。
かといって「ハンドラの型を保つ」という制約を課すと、冒頭の `req.user` パズルに戻ってしまいます。
フレームワークの設計とは、部品に課す制約と自由度のトレードオフの設計です。
「型を保つ制約は課さない、それでいて合成はきちんと定まる」という枠組みは、果たして作れるのでしょうか。

## Servant ではどう書くのか

Servant は、これをうまくやっています。
`GET /orders` で「ログインユーザー自身の注文一覧」を返す API を Servant で書くと、こうなります[^cookbook]。
認証方式は、デモが簡単な Basic 認証です。
まずは細部を読まず、全体の構成だけ眺めてください。

[^cookbook]: このコードは servant 公式 cookbook の [Basic Authentication](https://docs.servant.dev/en/latest/cookbook/basic-auth/BasicAuth.html) の例を下敷きにしています。そのまま動く完全なコード (題材は注文一覧ではなく Web サイト一覧ですが) はそちらを参照してください。

```haskell
-- (0) アプリケーションのドメイン型など (JSON への変換の定義などは省略)
data User = User { userName :: Text, userPass :: Text }
data Order = Order { orderId :: Int, orderItem :: Text }

type UserDB = Map.Map Text User

userDB :: UserDB
userDB = ... -- お試し用のインメモリ DB

listOrders :: User -> IO [Order]
listOrders user = ... -- DB からそのユーザーの注文を引いてくる

-- (1) API の仕様: 型レベル DSL で記述
type API = BasicAuth "my-realm" User :> "orders" :> Get '[JSON] [Order]

api :: Proxy API
api = Proxy

-- (2) サーバーの実装: ビジネスロジックだけを書く
server :: Server API
server user = do
  orders <- liftIO (listOrders user)
  pure orders

-- (3) 認証ロジックの実装: ユーザー名とパスワードを照合する
checkBasicAuth :: UserDB -> BasicAuthCheck User
checkBasicAuth db = BasicAuthCheck $ \basicAuthData ->
  let username = decodeUtf8 (basicAuthUsername basicAuthData)
      password = decodeUtf8 (basicAuthPassword basicAuthData)
  in case Map.lookup username db of
       Nothing -> pure NoSuchUser
       Just u  -> if userPass u == password
                  then pure (Authorized u)
                  else pure BadPassword

-- (4) 起動
main :: IO ()
main = run 8080 (serveWithContext api ctx server)
  where ctx = checkBasicAuth userDB :. EmptyContext
```

出発点は (1) です。
Servant では、まず API の仕様を **型レベル DSL** で記述します。

```haskell
type API = BasicAuth "my-realm" User :> "orders" :> Get '[JSON] [Order]
```

この 1 行が言っているのは次のことです。

- `GET /orders` というエンドポイントがあり、レスポンスは `Order` のリストを JSON で返す
- Basic 認証がかかっていて (`"my-realm"` は Basic 認証の realm 名)、ログイン成功時に得られるユーザー情報の型は `User`

`type` というキーワードで書かれてはいますが、`type API` 自体はリクエストハンドラの型ではありません。
型レベルの項で記述された、API 仕様の構文木のようなものです。

Servant はこの構文木から、サーバーのハンドラが満たすべき型、ルーティングを含む穴あきのサーバー実装、この API を叩くクライアント関数、OpenAPI ドキュメントなどを **型レベルの計算で** 導出します。
1 つの DSL に対して複数の解釈 (インタプリタ) が居る、と捉えることもできます。
開発者のやることは、導出された型に合う `server` 関数 (穴 = ビジネスロジック) を書いて、`run 8080 (serveWithContext api ctx server)` を呼ぶことだけです。

「型レベルで API 仕様を記述、そこから実装を導出」と聞くと不慣れな概念だらけに見えるかもしれませんが、構図としては OpenAPI とコード生成によるスキーマ駆動開発と同じです。
スキーマを書くと、型と穴あき実装が生成され、開発者は穴だけを埋める。
この開発体験は oapi-codegen などとそのまま重なります。
違いは 2 点で、「生成」がコード生成器ではなく GHC の型検査の一部として走ること、そして仕様記述言語が合成可能な部品でできていることです。
この 2 点こそ本シリーズの主題です (スキーマ駆動開発との比較そのものは番外編で改めて扱います)。

## 導出されたハンドラの型を覗く

さて、(2) の `server` をよく見ると、`user` を引数に取っています。

```haskell
server :: Server API
server user = do
  orders <- liftIO (listOrders user)
  pure orders
```

シグネチャに書かれている `Server API` は、「API 仕様 `API` に対応するハンドラの型」を計算する型レベルの関数適用です。
この計算の結果は、どんな型なのでしょうか。
ghci (Haskell の REPL) には、型レベルの計算を最後まで実行して見せてくれる `:kind!` というコマンドがあります。

```
ghci> :kind! Server API
Server API :: *
= User -> Handler [Order]
```

`Server API` の計算結果は `User -> Handler [Order]`、つまり「`User` を受け取って、`Order` のリストを返す処理」でした。
`Handler` の定義も見てみます[^version]。

```haskell
newtype Handler a = Handler {runHandler' :: ExceptT ServerError IO a}
```

[^version]: この記事で引用する Servant のコードは servant および servant-server 0.20.3.0 のものです。

`newtype` は既存の型に別名の皮を 1 枚かぶせる宣言で、中身は `ExceptT ServerError IO a` です。
この型はおおよそ `IO (Either ServerError a)` と読めます (きちんと開く作業は第 2 回でやります)。
つまり `Server API` を最後まで開くと、実質こうなります。

```haskell
Server API ≒ User -> IO (Either ServerError [Order])
```

「認証済みユーザーを受け取り、IO を伴う処理をして、エラーか注文一覧を返す関数」。
これは、認証つき API のハンドラと聞いて多くの人が素朴に思い浮かべる型そのものではないでしょうか。
型レベル DSL などという仰々しい機構から導出されてきたのに、着地点は直感どおりなのです。

そして Express で欲しかった性質がここに現れています。
`server` は `User` を引数に受け取らないと呼び出せません。
「認証を噛ませたハンドラは認証成功時のみ呼ばれる」という契約が、実行時チェックなしで、ハンドラの型そのものに反映されています。

では、この引数はどの規則から生えてきたのでしょうか。
servant-server の中に、次の宣言があります (2 行目はもう少し大きな定義の中に書かれている行の抜き出しです。また `type Server api` は単なる型シノニムで、規則の本体は `ServerT` の方です)。

```haskell
type Server api = ServerT api Handler

type ServerT (BasicAuth realm usr :> api) m = usr -> ServerT api m
```

2 行目が「引数が生える」規則です。
API 仕様の先頭に `BasicAuth realm usr :>` が付いていたら、対応するハンドラの型は「残りの仕様 `api` に対応するハンドラの型に、引数 `usr ->` を 1 本足したもの」になる、と読めます。
`m` の意味や、この規則がどんな仕組みで「計算」されるのかは第 2 回に譲ります。
ここでは「BasicAuth が付くと引数が 1 つ足される、という規則がこうやって宣言されている」と読めれば十分です。

では、もう 1 つの課題だった合成可能性はどうなっているのでしょうか。
API 仕様をつないでいた `:>` の定義を見てみます。

```haskell
data (path :: k) :> (a :: Type)

infixr 4 :>
```

`:>` は型レベルの中置演算子で、左辺には何でも (パス文字列、`BasicAuth ...` など)、右辺には続きの API 仕様が入ります。
ここで `BasicAuth "my-realm" User :>` までをひとかたまりの部品と見てください。
この部品は「API 仕様を 1 つ受け取って、新しい API 仕様を返す」ものになっています。
値の世界に型があるように、型の世界にも「型の型」があり、これを **kind** と呼びます。
Servant の部品 (**コンビネータ** と呼びます) は、ハンドラの型を保つ代わりに、「API 仕様 → API 仕様」という kind を保っているのです。

形が揃っているので、コンビネータはいくつでも重ねられます。
そして各コンビネータがハンドラの型に及ぼす作用は、先ほどの `ServerT` の規則としてコンビネータごとに宣言されています。
「合成の単位をハンドラではなく API 仕様に移すことで、ハンドラの型を変える自由と合成可能性を両立する」。
これが Servant の解でした。

## パスパラメータも同じ枠組み

「リクエストから値を取り出してハンドラに渡す」機能は、認証のほかにもう 1 つ、Web フレームワークの定番にあります。
パスパラメータです。
Express や Go のフレームワークでは、認証はミドルウェア、パスパラメータはルーティング機能の一部と、まったく別の場所に住んでいます。
Servant ではどうなっているのでしょうか。
`GET /orders/:id` は Servant ではこう書きます。

```haskell
type API2 = "orders" :> Capture "id" Int :> Get '[JSON] Order
```

対応するハンドラの型を計算すると、

```
ghci> :kind! Server API2
Server API2 :: *
= Int -> Handler Order
```

パスパラメータに対応する引数 `Int ->` が生えました。
`Capture` の `ServerT` 規則を BasicAuth のものと並べてみます (どちらも定義の該当行の抜き出しです)。

```haskell
type ServerT (BasicAuth realm usr :> api) m = usr -> ServerT api m

type ServerT (Capture' mods capture a :> api) m =
  If (FoldLenient mods) (Either String a) a -> ServerT api m
```

`Capture` の規則は一見ごちゃっとしていますが、これは `Capture` が実は `Capture' '[]` の略記 (`mods` はパースの挙動を変えるオプションのリスト) だからです。
`Capture "id" Int` のようにオプションなしで使うと、`If (FoldLenient mods) (Either String a) a` の部分は `a` に簡約されます (この簡約も第 2 回で追います)。
つまりオプションなしの `Capture` の規則は `a -> ServerT api m` で、BasicAuth の規則と同じ形です。

別の場所に住んでいたはずの 2 つが、Servant では「ハンドラに引数を 1 本足す API 仕様のコンビネータ」という同じ枠組みの住人でした。
「リクエストから何かを取り出して、その証拠をハンドラに引数として渡す」という共通の本質が、フレームワークの構造にそのまま現れています。

## おわりに

TypeScript や Go で認証ミドルウェアを書くときのあの苦しみは、ミドルウェアが「ハンドラの型を保つ変換」としてモデル化されていることに由来していました。
型を保つ制約を外しつつ合成可能性を保つフレームワーク設計は自明ではありません。
Servant は、合成の単位をハンドラから API 仕様へ移すことでこれをやってのけています。
コンビネータは「API 仕様 → API 仕様」という kind を保つので自由に合成でき、ハンドラの型は API 仕様から導出されるので、認証のような契約の変化が型に反映されます。

なお、「仕様を型レベルの項で書き、その解釈をインタプリタとして与える」という設計には必然性があります。
値レベルの DSL から出発して、壊れては直しを繰り返してこの設計に辿り着く過程が、公式ブログの [Why is servant a type-level DSL?](https://www.servant.dev/posts/2018-07-12-servant-dsl-typelevel.html) (2018) で読めます。
面白いのでおすすめです。

さて、この記事では `Server API` の計算について「結果」(開き切ると `User -> IO (Either ServerError [Order])`) と「部品」(BasicAuth が引数を 1 本足す規則) だけを見ました。
結末と局所の規則はわかりました。
では、`Server API` がその結果へ至る計算過程の全体は、どんな仕組みで動いているのでしょうか。
`type ServerT ... = ...` という規則は誰がいつ適用するのか。
`server` の型が合わなかったら、どこで、どんなカラクリで怒られるのか。
次回、ghci を片手に型レベル計算の中身を追いかけます。
