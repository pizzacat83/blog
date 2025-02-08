#if !FORNAX
#load "../loaders/postloader.fsx"
#endif

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

let topPath (language: Postloader.Language) = 
    match language with
    | Postloader.English -> "en"
    | Postloader.Japanese -> "ja"


let layout (language: Postloader.Language option) (title: string) (children: string) (spreadsheets: string list) =
    let logoHref =
        match language with
        | Some lang -> $"/{topPath lang}"
        | None -> "/"
    
    $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{title}</title>

<link rel="stylesheet" href="/assets/style.css">
{
    spreadsheets
    |> List.map (fun s -> sprintf "<link rel=\"stylesheet\" href=\"%s\">" s)
    |> String.concat "\n"
}
<link rel="alternate" type="application/rss+xml" title="posts" href="/rss">

{websocketScript |> (fun (RawHtml x) -> x)}
</head>
<body>

<header>
<nav>
    <div class="blog-title">
        <a href="{logoHref}">pizzacat83's blog</a>
    </div>
    <div>
        <a href="https://pizzacat83.com">About</a>
    </div>
</nav>
</header>
<main>

{children}

</main>

<footer>
   <p>© 2025 pizzacat83 • <a href="/rss">Feed</a></p>
</footer>

</body>
</html>
    """

type LocalizedPost = {
    key: Postloader.PostKey
    language: Postloader.Language
    published: System.DateOnly
    title: string
    summary: string
    body: string
}

let getLocalizedPosts (ctx: SiteContents) (language: Postloader.Language): LocalizedPost seq =
    let posts = ctx.TryGetValues<Postloader.Post> () |> Option.defaultValue Seq.empty
    posts
    |> Seq.choose (fun post ->
        post.contents |> List.tryFind (fun c -> c.language = language)
        |> Option.map (fun content ->
            {
                key = post.key
                language = content.language
                published = post.published
                title = content.title
                summary = content.summary
                body = content.body
            }
        )
    )
