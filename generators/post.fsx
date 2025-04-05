#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#load "lib.fsx"
#r "../lib/FSharp.Formatting/src/FSharp.Formatting.Markdown2/bin/Release/net8.0/FSharp.Formatting.Markdown2.dll"

open Lib
open System.Net
open FSharp.Formatting.Markdown2.HtmlFormatting
open Html

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
    if List.isEmpty headings then
        []
    else
        let minLevel = headings |> List.map (fun h -> h.level) |> List.min
        
        // constructs ToC subtrees by consuming headings of the same or lower level.
        // Returns the list of ToC subtrees and the remaining headings (that starts with a higher level).
        let rec consumeSections (headings: RenderedHeadingInfo list) (level: int): TocItem list * RenderedHeadingInfo list =
            match headings with
            | [] -> [], []
            | h :: remaining when h.level = level ->
                let children, remaining2 = consumeSections remaining (level + 1)
                let item = { heading = h; children = children }
                let followingItems, remaining3 = consumeSections remaining2 level
                item :: followingItems, remaining3
            | h :: _ when h.level < level ->
                // We have reached a higher level heading, so we stop consuming.
                [], headings
            | h :: _ ->
                failwithf "Unexpected heading level %d (%s), where current level = %d" h.level h.html level
            
        
        let result, remaining = consumeSections headings minLevel
        
        assert (List.isEmpty remaining)

        result

// Convert ToC tree to HTML elements
let rec tocItemToHtml (item: TocItem) : Node =
    li [] (
        [a ["href", "#" + item.heading.anchor] [DangerouslyInsertRawHtml item.heading.html]]
         @ if not (List.isEmpty item.children) then
            [ul [] (item.children |> List.map tocItemToHtml)]
          else
            []
    )

let tocToHtml (toc: TocItem list) : Node =
    ul ["class", "toc"] (toc |> List.map tocItemToHtml)

let layout (post: Lib.LocalizedPost) =
    let published = post.published.ToString("yyyy-MM-dd")
    let url = $"https://blog.pizzacat83.com{Lib.postHref post.language post.key}"
    
    // Build ToC tree from headings
    let tocTree = buildTocTree post.headings
    let tocHtml = 
        if List.isEmpty tocTree then
            Text ""
        else
            div ["class", "toc-container"] [
                h2 [] [Text "Table of Contents"]
                nav [] [tocToHtml tocTree]
            ]
    
    Lib.layout (Some post.language) post.title (Some post.summary)([
        DangerouslyInsertRawHtml $"""
<div class="content-wrapper">
    <article>   
        <header>
        <time datetime="{published}">{published}</time>
        {Lib.langSelector post |> Option.defaultValue ""}
        <h1>
            {post.title}
        </h1>
        {serialize None tocHtml}
        </header>
        <div class="post-body">
            {post.body}
        </div>
    </article>
    <aside class="sidebar">
        {serialize None tocHtml}
    </aside>
</div>

<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/default.min.css" integrity="sha512-hasIneQUHlh06VNBe7f6ZcHmeRTLIaQWFd43YriJ0UND19bvYRauxthDg8E4eVNPm9bRUhr5JGeqH7FRFXQu5g==" crossorigin="anonymous" referrerpolicy="no-referrer" />
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/highlight.min.js" integrity="sha512-EBLzUL8XLl+va/zAsmXwS7Z2B1F9HUHkZwyS/VKwh3S7T/U0nF4BaU29EP/ZSf6zgiIxYAnKLu6bJ8dqpmX5uw==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/languages/fsharp.min.js" integrity="sha512-S3tgSOL0xKKsqOdbPP7AZKtb/L0bXVG/PW7RNRVXOqCWEiBRzIq9oTIinLoY11MB58l1/f++IHM+mp1Nfk2ETA==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script>hljs.configure({{languages:[]}});hljs.highlightAll();</script>
        """
    ]) ["/assets/post.css"] [DangerouslyInsertRawHtml $"""
<meta property="og:title" content="{post.title |> WebUtility.HtmlEncode}" />
<meta property="og:description" content="{post.summary |> WebUtility.HtmlEncode}" />
<meta property="og:site_name" content="pizzacat83's blog" />
<meta property="og:type" content="article" />
<meta property="og:url" content="{url}" />
<meta property="article:published_time" content="{published}" />

<link rel="canonical" href="{url}">
{post.head |> Option.defaultValue ""}
""" ] "og: http://ogp.me/ns# article: http://ogp.me/ns/article#"

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
