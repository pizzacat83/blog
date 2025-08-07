---
draft: true
published: 2025-08-09
summary: あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。
---
# Go の iter.Seq, Rust の Iterator, JS の generator


<!-- TODO: 書き出し -->

Go におけるイテレータは以下のように定義されている。

```go
type Seq[V any] func(func(V) bool)
```

見慣れた書き方をするならば、 `(V -> bool) -> ()` である。

あれ、これ普段「イテレータ」と読んでいるものとちょっと形が違わないか？

私が見慣れているのは、Rust のイテレータのような流儀である。

```rust
trait Iterator {
  type Item;
  fn next(&mut self) -> Option<Self::Item>;
}
```

つまり、私の中で「イテレータ」と言えば、次の要素を返す `next: () -> Option<V>` というメソッドを持つ型であった。では、Go の `(V -> bool) -> ()` とは何なのか。私が見慣れていた「イテレータ」と本質的に同じものなのか、何かが違うのか。

`V -> bool` をグッと睨むと、これが「for 文の中身」に見えてくる。`bool` とは、その回で break するか否かを表す。つまり、for 文の `break` を `return false` に、`continue` を `return true` に書き換えてやると、それが `iter.Seq` に渡される引数である。

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

なるほどね。Go の世界におけるイテレータとは、「for 文の意味を与えるもの」だったんだね。と思いながらようやく `iter` のドキュメントを読む。

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