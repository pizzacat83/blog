#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#load "lib.fsx"
#r "../lib/FSharp.Formatting/src/FSharp.Formatting.Markdown2/bin/Release/net8.0/FSharp.Formatting.Markdown2.dll"

open Lib
open System.Net
open FSharp.Formatting.Markdown2.HtmlFormatting

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

// Define a data structure for the ToC tree
type TocItem = {
    heading: RenderedHeadingInfo
    children: TocItem list
}

// Convert flat heading list to hierarchical ToC tree
let buildTocTree (headings: RenderedHeadingInfo list) : TocItem list =
    let rec processLevel (currentLevel: int) (items: RenderedHeadingInfo list) : (TocItem list * RenderedHeadingInfo list) =
        printfn "Processing level %d with %d items" currentLevel (List.length items)

        let mutable result = []
        let mutable remaining = items
        
        while not remaining.IsEmpty && remaining.Head.level >= currentLevel do
            let heading = remaining.Head
            remaining <- remaining.Tail
            
            if heading.level = currentLevel then
                let (children, newRemaining) = processLevel (currentLevel + 1) remaining
                result <- result @ [{heading = heading; children = children}]
                remaining <- newRemaining
                
        result, remaining
    
    let tree, _ = processLevel 1 headings
    tree

// Convert ToC tree to HTML elements
let rec tocItemToHtml (item: TocItem) : Html.Node =
    Html.li [] (
        [Html.a ["href", "#" + item.heading.anchor] [Html.DangerouslyInsertRawHtml item.heading.html]]
         @ if not (List.isEmpty item.children) then
            [Html.ul [] (item.children |> List.map tocItemToHtml)]
          else
            []
    )

let tocToHtml (toc: TocItem list) : Html.Node =
    Html.ul ["class", "toc"] (toc |> List.map tocItemToHtml)

let layout (post: Lib.LocalizedPost) =
    let published = post.published.ToString("yyyy-MM-dd")
    let url = $"https://blog.pizzacat83.com{Lib.postHref post.language post.key}"
    
    // Build ToC tree from headings
    let tocTree = buildTocTree post.headings
    let tocHtml = 
        if List.isEmpty tocTree then
            Html.Text ""
        else
            Html.div ["class", "toc-container"] [
                Html.h2 [] [Html.Text "Table of Contents"]
                tocToHtml tocTree
            ]
    
    Lib.layout (Some post.language) post.title (Some post.summary)([
        Html.DangerouslyInsertRawHtml $"""
<div class="content-wrapper">
    <aside class="sidebar">
        {Html.serialize None tocHtml}
    </aside>
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
</div>

<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/default.min.css" integrity="sha512-hasIneQUHlh06VNBe7f6ZcHmeRTLIaQWFd43YriJ0UND19bvYRauxthDg8E4eVNPm9bRUhr5JGeqH7FRFXQu5g==" crossorigin="anonymous" referrerpolicy="no-referrer" />
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/highlight.min.js" integrity="sha512-EBLzUL8XLl+va/zAsmXwS7Z2B1F9HUHkZwyS/VKwh3S7T/U0nF4BaU29EP/ZSf6zgiIxYAnKLu6bJ8dqpmX5uw==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/languages/fsharp.min.js" integrity="sha512-S3tgSOL0xKKsqOdbPP7AZKtb/L0bXVG/PW7RNRVXOqCWEiBRzIq9oTIinLoY11MB58l1/f++IHM+mp1Nfk2ETA==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script>hljs.configure({{languages:[]}});hljs.highlightAll();</script>
        """
    ]) ["/assets/post.css"] [
        Html.DangerouslyInsertRawHtml ($"""
<meta property="og:title" content="{post.title |> WebUtility.HtmlEncode}" />
<meta property="og:description" content="{post.summary |> WebUtility.HtmlEncode}" />
<meta property="og:site_name" content="pizzacat83's blog" />
<meta property="og:type" content="article" />
<meta property="og:url" content="{url}" />
<meta property="article:published_time" content="{published}" />

<link rel="canonical" href="{url}">
""" + """
<style>
.content-wrapper {
    display: flex;
    gap: 2rem;
}
.sidebar {
    width: 250px;
    flex-shrink: 0;
    position: sticky;
    top: 2rem;
    align-self: flex-start;
    max-height: calc(100vh - 4rem);
    overflow-y: auto;
}
.toc-container {
    font-size: 0.9rem;
    border-left: 3px solid #ddd;
    padding-left: 1rem;
}
.toc-container h2 {
    font-size: 1.2rem;
    margin-top: 0;
}
.toc {
    padding-left: 1rem;
}
.toc ul {
    padding-left: 1.5rem;
}
.toc li {
    margin-bottom: 0.5rem;
}
.toc a {
    text-decoration: none;
    color: #555;
}
.toc a:hover {
    text-decoration: underline;
    color: #000;
}
@media (max-width: 768px) {
    .content-wrapper {
        flex-direction: column;
    }
    .sidebar {
        width: 100%;
        position: relative;
        margin-bottom: 2rem;
    }
}
</style>""" + (post.head |> Option.defaultValue ""))] "og: http://ogp.me/ns# article: http://ogp.me/ns/article#"

let langCode = function
    | Postloader.English -> "en"
    | Postloader.Japanese -> "ja"

let generate' (ctx : SiteContents) (language: Postloader.Language): (string * byte[]) seq =
    let posts =
        Lib.getLocalizedPosts ctx language
    
    Seq.append
        (posts
        |> Seq.map (fun post ->
                let html = layout post
                let filename = sprintf "%s/posts/%s/index.html" (langCode  post.language) (post.key |> fun (Postloader.PostKey k) -> k) in
                filename, html |> System.Text.Encoding.UTF8.GetBytes
        ))
        (posts
        |> Seq.collect (fun post ->
            post.assets
            |> Seq.map (fun asset ->
                let filename = sprintf "%s/posts/%s/%s" (langCode post.language) (post.key |> fun (Postloader.PostKey k) -> k) asset.filename
                filename, asset.content
            )
        ))

let generate (ctx : SiteContents) (projectRoot: string) (_): list<string * byte[]> =
    Postloader.languages
    |> List.map (generate' ctx)
    |> Seq.concat
    |> List.ofSeq
