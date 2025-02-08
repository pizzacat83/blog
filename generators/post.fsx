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


let layout (post: Postloader.Post) (content: Postloader.Content) =
    let language = content.language

    let published = post.published.ToString("yyyy-MM-dd")

    Lib.layout language content.title $"""
<article>

<header>
<time datetime="{published}">{published}</time>
<h1>
    {content.title}
</h1>
</header>
    {content.body}
</article>
    """ ["/assets/post.css"]

type Post = {
    language: Postloader.Language
    html: string
}

let langCode = function
    | Postloader.English -> "en"
    | Postloader.Japanese -> "ja"

let generatePost (post: Postloader.Post): Post list = 
    post.contents
    |> List.map (fun content ->
        let html = layout post content
        { language = content.language; html = html }
    )

let generate (ctx : SiteContents) (projectRoot: string) (_): list<string * string> =
    let posts =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
    
    posts
      |> Seq.map (fun post ->
            generatePost post     
            |> List.map (fun p ->
                let filename = sprintf "%s/posts/%s/index.html" (langCode  p.language) (post.key |> fun (Postloader.PostKey k) -> k) in
                (filename, p.html)
            )
      )
    |> Seq.concat
    |> List.ofSeq
