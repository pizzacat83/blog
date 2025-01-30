#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

type RawHtml = RawHtml of string

let websocketScript =
    RawHtml """
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


let layout (post: Postloader.Post) =
    $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{post.title}</title>

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
<h1>
    {post.title}
</h1>
</header>
    {post.content}

</article>

</main>

<footer>
   <p>© 2025 pizzacat83 • <a href="/rss">Feed</a></p>
</footer>

</body>
</html>
    """

let generate (ctx : SiteContents) (projectRoot: string) (postKey: string) =
    let postKey = Postloader.PostKey postKey

    let post =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.find (fun n -> n.key = postKey)

    layout post
