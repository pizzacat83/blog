---
draft: true
published: 2025-11-24
summary: あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。
---
# 手を動かして Pin を理解する

async Rust 周りのエコシステムを実装し、その過程で色々なコンパイル時エラーを踏むことを通して、Pin の理解を得ようとする記事です。

Pin に関しては Rust の公式資料が最もわかりやすい (AI に聞くよりも！) と感じており、この記事を読むよりも公式資料を読む方が断然オススメなのですが、実際に色々なコードを書いてみてコンパイル時エラーを喰らうフィードバックループを回して直感を養うことも一定の有用性があると思っており、この面から公式資料に対する補完的な価値を提供できればと書いています。

## 前提: async Rust 以外を忘れる

async Rust 以外の Pin のユースケースは、この記事においては一旦忘れます。

Pin は「async Rust にしか使えない概念」ではありませんが、「Pin はなぜこのような仕様なのか」を理解するにあったって、async Rust を念頭に置くと納得しやすいことがあります。まずは async Rust のユースケースのみを考えて Pin のメンタルモデルを掴んでから、async Rust 以外への Pin の応用について考え始めると、混乱が小さいかもしれません。 

> It's worth noting that pinning is a low-level building block designed specifically for the implementation of async Rust. Although it is not directly tied to async Rust and can be used for other purposes, it was not designed to be a general-purpose mechanism, and in particular is not an out-of-the-box solution for self-referential fields. Using pinning for anything other than async code generally only works if it is wrapped in thick layers of abstraction, since it will require lots of fiddly and hard to reason about unsafe code.
> ([Pinning - Asynchronous Programming in Rust](https://rust-lang.github.io/async-book/part-reference/pinning.html#footnote-design))

## まず苦しんでみよう

Pin とは何か。とりあえず、Pin と格闘せざるを得ない環境に身を置いてみましょう。人が Pin に出くわすきっかけとして最も多いのは、やはり async Rust 周りではないかと思います。というわけで、async Rust executor を作ってみましょう。

Future を実行してその output を返す関数 `execute` を実装していきます。`async fn` の呼び出しや `async {}` の型は `impl Future` ですから、これらを `execute` に渡すことで、その async な処理が実行され、結果が得られます。

```rust
/// 与えられた Future を実行し、その output を返す
fn execute<F: Future>(fut: F) -> F::Output { // execute 自身は async fn ではない
	todo!()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn it_works() {
        assert_eq!(1, execute(async { 1 }));

        let add = async |x: i32, y: i32| x + y;
        let mul = async |x: i32, y: i32| x * y;
        assert_eq!(
            9,
            execute(async {
                let sum = add(1, 2).await;
                mul(sum, 3).await
            })
        );
    }
```

さて `execute` に与えられている引数 `fut` を使って何ができるのでしょう。`fut` は Future trait を実装していますから、[この trait の定義](https://doc.rust-lang.org/std/future/trait.Future.html)を見てみましょう。

```rust
pub trait Future {
    type Output;

    // Required method
    fn poll(self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<Self::Output>;
}
```

## Pin は何ではないか
