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

module Html =
    type Tag = Tag of string with
        override this.ToString() =
            let (Tag value) = this
            value

    let tag (s: string) = Tag (s.ToLower())

    type Attribute = string * string

    type
        Node =
        | Element of Element
        | Text of string
        | DangerouslyInsertRawHtml of string
    and
        Element = {
            tag: Tag
            attributes: Attribute list
            children: Node list
        }

    type Document = Document of Node

    let shouldEscapeContent (tag: Tag) =
        match tag with
        | Tag ("style" | "script" | "xmp" | "iframe" | "noembed" | "noframes" | "plaintext") -> true
        | _ -> false

    // https://spec.whatwg.org/multipage/parsing.html#serialising-html-fragments
    let rec serialize (parent: Element option) (node: Node): string = 
        match node with
        | Element e ->
            let tag = e.tag.ToString()
            let attrs =
                e.attributes
                |> List.map (fun (name, value) ->
                    $" {name}=\"{value |> WebUtility.HtmlEncode}\"")
                |> String.concat ""
            let children =
                e.children
                |> List.map (serialize (Option.Some e))
                |> String.concat ""
            $"<{tag}{attrs}>{children}</{tag}>"
        | Text text ->
            match parent with
            | Some element when shouldEscapeContent element.tag ->
                text
            | _ ->
                WebUtility.HtmlEncode text
        | DangerouslyInsertRawHtml html -> html
        

    let serializeDocument (doc: Document): string =
        let (Document child) = doc
        $"<!DOCTYPE html>{serialize None child}"

    let a (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "a"
            attributes = attributes
            children = children
        }
    
    let div (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "div"
            attributes = attributes
            children = children
        }
    let h1 (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "h1"
            attributes = attributes
            children = children
        }
    let h2 (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "h2"
            attributes = attributes
            children = children
        }

    let h3 (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "h3"
            attributes = attributes
            children = children
        }

    let span (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "span"
            attributes = attributes
            children = children
        }
    
    let p (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "p"
            attributes = attributes
            children = children
        }

    let html (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "html"
            attributes = attributes
            children = children
        }
    
    let head (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "head"
            attributes = attributes
            children = children
        }

    let body (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "body"
            attributes = attributes
            children = children
        }

    let meta (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "meta"
            attributes = attributes
            children = children
        }

    let title (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "title"
            attributes = attributes
            children = children
        }

    let link (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "link"
            attributes = attributes
            children = children
        }

    let nav (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "nav"
            attributes = attributes
            children = children
        }

    let header (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "header"
            attributes = attributes
            children = children
        }

    let main (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "main"
            attributes = attributes
            children = children
        }

    let footer (attributes: Attribute list) (children: Node list) =
        Element {
            tag = tag "footer"
            attributes = attributes
            children = children
        }

    let (!!) (text: string) = Text text


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

open Html

let layout (language: Postloader.Language option) (title_text: string) (description: string option) (children: Node list) (stylesheets: string list) (head_contents: Node list) (head_prefix: string) =

    let logoHref =
        match language with
        | Some lang -> $"/{topPath lang}"
        | None -> "/"
    
    let meta_description =
        description
        |> Option.map (fun d ->
            meta [
                "name", "description"
                "content", d
            ] [])
        |> Option.toList

    Document(
        html ["lang", "en"] [
            head ["prefix", head_prefix] (
                [
                    meta ["charset", "UTF-8"] []
                    meta ["name", "viewport"; "content", "width=device-width, initial-scale=1.0"] []
                    title [] [!! title_text]
                ]
                @ meta_description
                @ head_contents
                @ [
                    link ["rel", "stylesheet"; "href", "/assets/style.css"] []
                ]
                @ (stylesheets |> List.map (fun s ->
                    link ["rel", "stylesheet"; "href", s] []))
                @ [
                    link ["rel", "alternate"; "type", "application/rss+xml"; "title", "posts"; "href", "/rss.xml"] []
                ]
                @ if is_watch then [DangerouslyInsertRawHtml (let (RawHtml x) = websocketScript in x)] else []
            )
            body [] [
                header [] [
                    nav [] [
                        div ["class", "blog-title"] [
                            a ["href", logoHref] [!! "pizzacat83's blog"]
                        ]
                        div [] [
                            a ["href", "https://pizzacat83.com"] [!! "About"]
                        ]
                    ]
                ]
                main [] children
                footer [] [
                    p [] [
                        !! "© 2025 pizzacat83 • "
                        a ["href", "/rss.xml"] [!! "Feed"]
                    ]
                ]
            ]
        ]
    ) |> serializeDocument

type LocalizedPost = {
    key: Postloader.PostKey
    language: Postloader.Language
    languages: Set<Postloader.Language>
    published: System.DateOnly
    title: string
    summary: string
    body: string

    assets: Postloader.Asset list

    head: string option
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

                assets = content.assets

                head = content.head
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

            assets = content.assets

            head = content.head
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
