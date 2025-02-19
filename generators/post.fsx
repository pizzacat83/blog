#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
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
    let published = post.published.ToString("yyyy-MM-dd")

    Lib.layout (Some post.language) post.title $"""
<article>   

<header>
<time datetime="{published}">{published}</time>
{Lib.langSelector post |> Option.defaultValue ""}
<h1>
    {post.title}
</h1>
</header>
    {post.body}
</article>

<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/default.min.css" integrity="sha512-hasIneQUHlh06VNBe7f6ZcHmeRTLIaQWFd43YriJ0UND19bvYRauxthDg8E4eVNPm9bRUhr5JGeqH7FRFXQu5g==" crossorigin="anonymous" referrerpolicy="no-referrer" />
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/highlight.min.js" integrity="sha512-EBLzUL8XLl+va/zAsmXwS7Z2B1F9HUHkZwyS/VKwh3S7T/U0nF4BaU29EP/ZSf6zgiIxYAnKLu6bJ8dqpmX5uw==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script>hljs.highlightAll();</script>
    """ ["/assets/post.css"]

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
