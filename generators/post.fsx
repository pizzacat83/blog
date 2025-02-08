#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

open Lib

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


let layout (post: Lib.LocalizedPost) =
    let language = post.language

    let published = post.published.ToString("yyyy-MM-dd")

    Lib.layout language post.title $"""
<article>   

<header>
<time datetime="{published}">{published}</time>
<h1>
    {post.title}
</h1>
</header>
    {post.body}
</article>
    """ ["/assets/post.css"]

type Post = {
    language: Postloader.Language
    html: string
}

let langCode = function
    | Postloader.English -> "en"
    | Postloader.Japanese -> "ja"

let generate' (ctx : SiteContents) (language: Postloader.Language): (string * string) seq =
    let posts =
        Lib.getLocalizedPosts ctx language
    
    posts
    |> Seq.map (fun post ->
            let html = layout post
            let filename = sprintf "%s/posts/%s/index.html" (langCode  post.language) (post.key |> fun (Postloader.PostKey k) -> k) in
            (filename, html)
    )

let generate (ctx : SiteContents) (projectRoot: string) (_): list<string * string> =
    Postloader.languages
    |> List.map (generate' ctx)
    |> Seq.concat
    |> List.ofSeq
