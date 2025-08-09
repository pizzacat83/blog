---
draft: true
published: 2025-08-09
summary: Go の iter.Seq の定義は、見慣れないもののようで、見慣れていて、見慣れていた。
---
# Go の iter.Seq, Rust の Iterator, JS の generator


**TL; DR**: The Go Blog の [Range Over Function Types](https://go.dev/blog/range-functions) を読みましょう。

---

Go におけるイテレータは以下のように定義されている。

```go
type Seq[V any] func(func(V) bool)
```

見慣れた書き方をするならば、 `(V -> bool) -> ()` である。

あれ、これ普段「イテレータ」と読んでいるものとちょっと形が違わないか？

私が見慣れているのは、Rust の Iterator のような流儀である。

```rust
trait Iterator {
  type Item;
  fn next(&mut self) -> Option<Self::Item>;
}
```

つまり、私の中で「イテレータ」と言えば、次の要素を返す `next: () -> Option<V>` というメソッドを持つ型であった。では、Go の `(V -> bool) -> ()` とは何なのか。私が見慣れていた「イテレータ」と本質的に同じものなのか、何かが違うのか。

「型は体を表す」と言う諺があるように、`V -> bool` をグッと睨むと、これが「for 文の中身」に見えてくる。`bool` とは、その回で break するか否かを表す。つまり、for 文の `break` を `return false` に、`continue` を `return true` に書き換えてやると、それが `iter.Seq` に渡される引数である。

```go
for i := range numbers {
	if i > 83 {
		break
	}
	if i % 2 != 0 {
		continue
	}
	fmt.Printf("Even number: %d\n", i)
}
```

```go
numbers(func (i int) bool {
	if i > 83 {
		return false
	}
	if i % 2 != 0 {
		return true
	}
	fmt.Printf("Even number: %d\n", i)
	return true
})
```

つまり、Go の `iter.Seq` とは「for 文で回せるやつ」である、と言える。こうしてみると、`(V -> bool) -> ()` とは、for 文の意味を定めるものと捉えることができる。

普通のイテレータは for 文にまともな意味を与えるけれど、その気になれば、「各反復が並列に実行される for 文」も、`(V -> bool) -> ()` で表現できる。[^misbehaving-iterator] ただ残念ながら (幸い?) 実行時のチェックがあるようで、このような異常なイテレータは panic を起こす。

```go
concurrentSeq := func(f func(int) bool) {
	items := []int{1, 2, 3, 4, 5}
	for i := range items {
		go f(i)
	}
}

for i := range concurrentSeq {
	fmt.Printf("Start: %d\n", i)
	time.Sleep(time.Duration(i*100) * time.Millisecond)
	fmt.Printf("End: %d\n", i)
}

// panic: runtime error: range function continued iteration after whole loop exit
```

[^misbehaving-iterator]: 正確には、「各反復が並列に実行される for 文」は「コールバックが `false` を返した場合にループを終了する」という `iter.Seq` の要請を満たしていないので、`iter.Seq` の妥当な実装ではない、と言うこともできる。

なるほど、Go の世界におけるイテレータとは、「for 文の意味を与えるもの」だったんだね、と思ったところで、Rust のような見慣れたイテレータとの差異について考えていく。

さて、物事に良い名前をつけると何かと取り回しが良い。Ranging Over Function Types では、Go の iter.Seq のような、(V -> bool) -> () によって定義されるイテレータを push iterator と呼び、Rust の Iterator のような、`next: () -> Option<V>` によって定義されるイテレータを pull iterator と呼んでいるので、ここでもこの用語を使っていく。

この2種類のイテレータ定義には、「できること」の違いはあるのだろうか。まず、push iterator に対してできることは、pull iterator に対してもできる。以下の ToPush 関数は、`Next()` メソッドの結果を for 文の本体に適用することを繰り返すことで、pull iterator から push iterator を構成する[^pull-iter-stop]。

```go
type PullIter interface { func Next() (V, bool) }


func ToPush[V any](it PullIter[V]) iter.Seq[V] {
	return func(loopBody func(V) bool) {
		for {
			v, ok := it.Next()
			if !ok {
				break
			}
			continue_ := loopBody(v)
			if !continue_ {
				break
			}
		}
	}
}
```

[^pull-iter-stop]: 後述する `iter.Pull` の実装にもあるように、Go では pull iterator は `func Stop()` メソッドも持つ定義が望ましいが、ここでは簡単のため `Next` メソッドのみを要請する。

逆に、pull iterator に対してできることは、push iterator に対してもできるだろうか。

例えば、以下の pull iterator に対する処理を、push iterator に対するものに書き換えることを考えてみよう。

```go
// [1,2, 3,4, 5,6] -> [1+2, 3+4, 5+6]
// [1,2, 3,4, 5] -> [1+2, 3+4]
func SumPair_Pull(it PullIter[int]) []int {
	var sums []int
	for {
		v0, ok := it.Next()
		if !ok { break }
		v1, ok := it.Next()
		if !ok { break }
		sums = append(sums, v0+v1)
	}
	return sums
}
```

`iter.Seq` の引数となる関数を「for 文の中身」と捉えると、要は上のプログラムを、`for v := range it { ... }` に書き換えればよい。うーむ。

```go
func SumPair_Push(it iter.Seq[int]) []int {
	var sums []int
	var v0 int
	isEvenIndex := false
	for v := range it {
		if isEvenIndex {
			v0 = v
		} else {
			sums = append(sums, v0+v)
		}
		isEvenIndex += 1
	}
	return sums
}
```

すると、`it` の引数となる関数は以下のようになる。

```go
func loopBody(v int) bool {
	if isEvenIndex {
		v0 = v
	} else {
		sums = append(sums, v0+v)
	}
	isEvenIndex += 1
	return true
}
```

これは、元の `SumPair_Pull` の処理を、「`it.Next` が呼ばれてから次に `it.Next` が呼ばれるまで」で区切って貼り合わせたもの、と捉えることができる。

<!-- TODO: 図? -->

別の言い方をするならば、元の pull iterator 版のプログラムは一続きの処理であるように見えるが、push iterator を使うためには、「元々 `it.Next` が呼ばれていた箇所で `loopBody` 関数を抜け、`iter.Seq` に処理を戻す。次の値に対して `loopBody` 関数が呼ばれると、`it.Next` の返り値を受け取った後の処理を再開する」という構造にする必要がある。元々の一続きの流れを `return` で中断、`call` で再開できるように、中断した時の状態を持っておかなければならない。この状態をより明示的に書くと以下のように表現できる。

```go
type SumPair_Push struct {
	sums []int
	v0 int
	isEvenIndex bool
}

// 気持ち的には &mut self
func (self *SumPair_Push) LoopBody(v int) bool {
	if self.isEvenIndex {
		self.v0 = v
	} else {
		self.sums = append(self.sums, v0+v)
	}
	self.isEvenIndex += 1
	return true
}
```

ということで、pull iterator を扱う関数 `SumPair_Pull` と同じ挙動をするような、push iterator に対する関数 `SumPair_Push` をなんとか書くことができた。

また別の例を考えてみよう。Range Over Function Types でも取り上げられている、2つのイテレータの要素の等しさを返す関数である。これは、pull iterator では簡単に実装できる。

```go
func Eq_Pull[V comparable](it1, it2 iter.Seq[V]) bool {
	for {
		v1, ok1 := it1.Next()
		v2, ok2 := it2.Next()
		if ok1 && ok2 { return true }
		if ok1 != ok2 { return false }
		if v1 != v2 { return false }
	}
}
```

これの push iterator 版 `Eq_Push` は実装できるだろうか？`SumPair_Push` での考え方「`Next()` の呼び出しを境界として切って貼り合わせる」ではなかなかうまくいかない。

仮に、`it1` と `it2` がスライスの自然な push iterator であるとしよう。

```go
it1 := func(loopBody func(int) bool) {
	for x in range []int{1,2,3} {
		continue_ := loopBody(x)
		if !continue_ { break }
	}
}

it2 := func(loopBody func(int) bool) {
	for x in range []int{1,100,3} {
		continue_ := loopBody(x)
		if !continue_ { break }
	}
}
```

この2つの push iterator から交互に値を取り出す処理を実現するには、`it1` と `it2` の2つの for 文を並行に動かす必要がある。並行計算をサポートしていない言語では、2つの push iterator から交互に値を取り出す処理を実装できない。

幸い Go は並行計算をサポートしているので、なんとか `Eq_Push` を実装する方法はあるのだが、それについては後述する。

このように、「イテレータを使う側」の視点では、pull iterator は push iterator の上位互換であるように見える。Push iterator は、単に `for v := range it { ... }` のようにして使いたい場合は良いのだが、そうではない場合に直感的にプログラムを書くことが難しかったり、並行計算なしでは実現できなかったりする。ここまで見てきた限りでは、pull iterator の方が「便利そう」に思えるのにも関わらず、Go が `iter.Seq` の定義に push iterator を採用したのが不思議に思えてくる。

ということで `iter` のドキュメントを開く。

```go
type Seq[V any] func(yield func(V) bool)
```

あっ、君、`yield` っていうの？

`yield`?

あっ

```js
function* generator() {
	console.log("Yielding 1")
	yield 1

	console.log("Yielding 2")
	yield 2

	console.log("Yielding 3")
	yield 3
}

const gen = generator()

console.log("next:", gen.next())
console.log("next:", gen.next())
console.log("next:", gen.next())
```

```
Yielding 1
next: { value: 1, done: false }
Yielding 2
next: { value: 2, done: false }
Yielding 3
next: { value: 3, done: false }
```

あっ

```go
func Pull[V any](seq Seq[V]) (next func() (V, bool), stop func())
```

あ〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜〜

これまで push iterator の引数 V -> bool を「for 文の中身」と捉えていたが、これに yield という名をつけると、「push iterator とは、値を順に yield していくもの」という捉え方ができる。「for 文に意味を与える」「与えられた『for 文の中身』をよしなに実行する」というメンタルモデルではなく、「とにかく自分が出したい値を yield する (受け取り手が何であるかは特に考えない)」というメンタルモデルである。

このメンタルモデルをもとに、イテレータを定義する側の視点に立ってみると、「push iterator では実装しやすいが、pull iterator では実装しにくいもの」がいくつか思い浮かぶ。

以下のようなイテレータ Triangular を考えてみよう。1が1回、2が2回、3が3回… と無限に続くイテレータである。

Triangular の push iterator は、この列を素直に yield すればよい。

```go
func Triangular_Push(yield func(int) bool) {
	n := 0
	for {
		n += 1
		for i in range n {
			continue_ := yield(n)
			if !continue_ { break }
		}
	}
}
```

一方、pull iterator では、`Next()` が呼ばれるたびに要素をちょうど1つ出力して return する必要がある。つまり、`Triangle_Push` の処理を `yield` の前後で区切って貼り合わせる。別の言い方をするならば、次の `Next()` の呼び出しの際に "yield の直後" に戻れるように、処理中の状態を持っておく必要がある。その状態をモデル化すると `Triangular_Pull` を実装できる。

```go
type Triangular_Pull struct {
	n int
	i int
}

func (self *Triangular_Pull) Next() (int, bool) {
	if self.n == self.i { self.n += 1; self.i = 0 }
	self.i += 1
	return self.n, true
}
```

ここに push iterator との対称性を感じる。Push iterator では、要素を受け取る側が処理を中断して制御をイテレータに戻せるよう、処理中の状態を定義し、要素を受け取った後の処理を再開できるようにしていた。一方 pull iterator では、要素を出力する側が処理を中断できるよう、処理中の状態を定義して再開できるようにしているのだ。

ところでここでは Triangular という人工的な例を挙げたけれど、この時私は、過去色々コーディングしていた中で pull iterator の実装がぐちゃっとなっていたときのことを思い出し、あの時 push iterator でよければ綺麗に実装できたのか、と思いを馳せていた。

例えば、[Rust 製自作ブラウザ sabatora の HTML 字句解析器](https://pizzacat83.hatenablog.com/entry/2025/01/10/172345) ([GitHub](https://github.com/pizzacat83/sabatora/blob/a5f8716452ab077028397e2587e4215ab271a506/saba_core/src/renderer/html/token.rs#L41-L45))は `yielded_tokens` という状態を持つが、これは HTML Standard の字句解析の仕様における「1ステップ」が0個以上のトークンが出力しうるのに対して、pull iterator である Rust の Iterator trait の `next` メソッドはちょうど1個の要素またはイテレータの終了を返す必要がある、というギャップを埋めるためのバッファである。

```rust
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HtmlTokenizer {
    state_machine: HtmlTokenizeStateMachine,
    eof_observed: bool,
    yielded_tokens: Vec<HtmlToken>,
}

impl Iterator for HtmlTokenizer {
    type Item = HtmlToken;
    fn next(&mut self) -> Option<Self::Item> {
        if self.eof_observed {
            return None;
        }
        loop {
	        // バッファに何か残っていたら、そこから取る
            if let Some(token) = self.take_remaining_token() {
                if token == HtmlToken::Eof {
                    self.eof_observed = true;
                }
                if let HtmlToken::StartTag { tag, .. } = &token {
                    self.state_machine.latest_start_tag_name = Some(tag.clone());
                }
                return Some(token);
            }
            // 字句解析を1ステップ進め、出力されたトークンをバッファに溜める
            if let Some(tokens) = self.state_machine.step() {
                self.yielded_tokens = tokens;
            }
        }
    }
}
```

しかし push-style でよければ、仕様書で "Emit XXX token" と定められているところで`yield(XXXToken)` すればよい。仕様書における語彙をそのまま実装に反映できるとき、心地がよい。

話を戻すと、push iterator と pull iterator に対して以下の直感が得られた。Push iterator は出力側の実装が楽で、受け取り側の実装が時々つらい。Pull iterator は受け取り側の実装が楽で、出力側の実装が時々つらい。「つらい」というのは、値を入出力する一連の流れを、受け渡しのタイミングで中断・再開できるよう、流れの途中の状態をモデル化し、1反復分ごとに return するような関数を記述する必要があることである。

流れの途中の状態は自然と導けるときもあるし、そうでないときもある。しかし実現したい処理のメンタルモデルが「一続きの流れ」であるならば、途中で中断した際の状態をモデリングすることなくメンタルモデルをそのまま記述できた方が望ましい。

ここで JS のジェネレータに思いを馳せると、ジェネレータを定義する側は push-style、使う側は pull-style であり、双方にとって楽な形態で処理を記述できるようにしている。そして JS エンジンは、この2つのスタイルのギャップを埋める glue を提供していると捉えることができる。そして Go の iter.Pull もまた、push iterator を基に pull iterator を構成するものである。

<!-- TODO -->