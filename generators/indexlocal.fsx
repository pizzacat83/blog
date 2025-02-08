#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

let renderPost (post: Lib.LocalizedPost) =
    let published = post.published.ToString("yyyy-MM-dd")
    let href = $"/{Lib.topPath post.language}/posts/{post.key |> (fun (Postloader.PostKey x) -> x)}"

    $"""
    <article>
        <time datetime="{published}">{published}</time>
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

    Lib.layout (Some language) "pizzacat83's blog" $"""
<div class="post-list">
    <div>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </div>
</div>
    """ ["/assets/index.css"]

let generate (ctx : SiteContents) (projectRoot: string) (page: string): list<string * string> =
    Postloader.languages
    |> List.map (fun lang ->
        let filename = sprintf "%s/index.html" (Lib.topPath lang)
        let html = generate' ctx projectRoot lang
        (filename, html)
    )

    
