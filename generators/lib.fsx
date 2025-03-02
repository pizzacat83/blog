#if !FORNAX
#load "../loaders/postloader.fsx"
#endif

open System.Net

type RawHtml = RawHtml of string

// TODO: fornax should provide this value as arguments, not directives
let is_watch =
#if WATCH
    true
#else
    false
#endif


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


let layout (language: Postloader.Language option) (title: string) (description: string option) (children: string) (stylesheets: string list) (head: string) (head_prefix: string) =
    let logoHref =
        match language with
        | Some lang -> $"/{topPath lang}"
        | None -> "/"
    
    let meta_description =
        description
        |> Option.map (fun d -> $"""<meta name="description" content="{d |> WebUtility.HtmlEncode}">""")
        |> Option.defaultValue ""

    $"""
<!DOCTYPE html>
<html lang="en">
<head prefix="{head_prefix}">
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{title}</title>
{meta_description}
{head}

<link rel="stylesheet" href="/assets/style.css">
{
    stylesheets
    |> List.map (fun s -> sprintf "<link rel=\"stylesheet\" href=\"%s\">" s)
    |> String.concat "\n"
}
<link rel="alternate" type="application/rss+xml" title="posts" href="/rss.xml">

{if is_watch then websocketScript |> (fun (RawHtml x) -> x) else ""}
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
   <p>© 2025 pizzacat83 • <a href="/rss.xml">Feed</a></p>
</footer>

</body>
</html>
    """

type LocalizedPost = {
    key: Postloader.PostKey
    language: Postloader.Language
    languages: Set<Postloader.Language>
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
                languages = post.contents |> List.map (fun c -> c.language) |> Set.ofList
                published = post.published
                title = content.title
                summary = content.summary
                body = content.body
            }
        )
    )

let choosePrimaryContent (post: Postloader.Post) =
    // TODO: enable specifying primary language per Post
    post.contents |> List.tryFind (fun c -> c.language = Postloader.Language.Japanese)
    |> Option.defaultValue (post.contents[0])

let getPrimaryLocalizedPosts (ctx: SiteContents): LocalizedPost seq =
    ctx.TryGetValues<Postloader.Post> ()
    |> Option.defaultValue Seq.empty
    |> Seq.map (fun (post) ->
        let content = choosePrimaryContent post
        {
            key = post.key
            language = content.language
            languages = post.contents |> List.map (fun c -> c.language) |> Set.ofList
            published = post.published
            title = content.title
            summary = content.summary
            body = content.body
        }: LocalizedPost
    )


let postHref (language: Postloader.Language) (key: Postloader.PostKey) =
    $"/{topPath language}/posts/{key |> (fun (Postloader.PostKey x) -> x)}"

let langSelector (post: LocalizedPost): string option =
    post.languages
    |> Seq.sortBy (function
        | Postloader.Language.English -> 1
        | Postloader.Language.Japanese -> 2
    )
    |> List.ofSeq
    |> (fun l -> if List.length l > 1 then Some l else None)
    |> Option.map (List.map (fun lang ->
        let langCode =
            match lang with
            | Postloader.Language.English -> "en"
            | Postloader.Language.Japanese -> "ja"
        $"""<a href="{postHref lang post.key}">{langCode}</a>"""
    ))
    |> Option.map (String.concat " ")
    |> Option.map (fun s ->
        $"""<span class="lang-selector">{s}</span>"""
    )
