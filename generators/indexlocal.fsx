#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#load "lib.fsx"

let renderPost (post: Lib.LocalizedPost) =
    let published = post.published.ToString("yyyy-MM-dd")
    let href = Lib.postHref post.language post.key

    $"""
    <article>
        <time datetime="{published}">{published}</time>
        {Lib.langSelector post |> Option.defaultValue ""}
        <h1><a href="{href}">{post.title}</a></h1>
        
        <p class="post-summary">
            {post.summary}
        </p>
    </article>
    """

let generate' (ctx : SiteContents) (projectRoot: string) (language: Postloader.Language)=
    let posts =
        Lib.getLocalizedPosts ctx language
        |> Seq.sortByDescending (fun p -> p.published)

    let languageFilter =
        Postloader.languages
        |> Seq.map (fun lang ->
            let text =
                match lang with
                | Postloader.Language.English -> "English"
                | Postloader.Language.Japanese -> "日本語"
            if lang = language then
                $"""<span>{text}</span>"""
            else
                $"""<a href="/{Lib.topPath lang}">{text}</a>"""
        )
        |> String.concat " / "

    Lib.layout (Some language) "pizzacat83's blog" None $"""
<div class="index-main">
    <div class="lang-filter">
        <a href="/">All languages</a> / {languageFilter}
    </div>

    <div class="post-list">
        <div>
            { posts
                |> Seq.map renderPost
                |> String.concat ""
            }
        </div>
    </div>

</div>
    """ ["/assets/index.css"] "" ""

let generate (ctx : SiteContents) (projectRoot: string) (page: string): list<string * string> =
    Postloader.languages
    |> List.map (fun lang ->
        let filename = sprintf "%s/index.html" (Lib.topPath lang)
        let html = generate' ctx projectRoot lang
        (filename, html)
    )

    
