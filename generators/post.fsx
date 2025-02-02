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
    Lib.layout  post.title $"""
<article>

<header>
<time datetime="2022-05-21">2022-05-21</time>
<h1>
    {post.title}
</h1>
</header>
    {post.content}
</article>
    """ ["/assets/post.css"]

let generate (ctx : SiteContents) (projectRoot: string) (postKey: string) =
    let postKey = Postloader.PostKey postKey

    let post =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.find (fun n -> n.key = postKey)

    layout post
