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
    }
    window.addEventListener("load", init, false);
    </script>
    """


let layout (title: string) (children: string) (spreadsheets: string list) =
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
        <a href="/">pizzacat83's blog</a>
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
