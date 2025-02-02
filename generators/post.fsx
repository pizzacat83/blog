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


let layout (post: Postloader.Post) =
    let published = post.published.ToString("yyyy-MM-dd")

    Lib.layout  post.title $"""
<article>

<header>
<time datetime="{published}">{published}</time>
<h1>
    {post.title}
</h1>
</header>
    {post.content}
</article>
    """ ["/assets/post.css"]

let generate' (ctx : SiteContents) (projectRoot: string) (relpath: string): Result<string, string> = 

    let postKey =
        relpath
        |> System.IO.Path.GetDirectoryName
        |> System.IO.Path.GetFileName
        |> Postloader.PostKey

    let post =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.tryFind (fun n -> n.key = postKey)

    match post with
        | Some x ->  Ok (layout x)
        | None -> Error "Post not found"

let generate (ctx : SiteContents) (projectRoot: string) (postKey: string) =
    match generate' ctx projectRoot postKey with
    | Ok x -> x
    | Error e ->
        // TODO: proper error handling
        printfn "Failed to generate post %s: %s" postKey e
        ""
