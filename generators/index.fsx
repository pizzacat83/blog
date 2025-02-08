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

let choosePrimaryContent (post: Postloader.Post) =
    // TODO: enable specifying primary language per Post
    post.contents |> List.tryFind (fun c -> c.language = Postloader.Language.Japanese)
    |> Option.defaultValue (post.contents[0])

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts =
        ctx.TryGetValues<Postloader.Post> ()
        |> Option.defaultValue Seq.empty
        |> Seq.map (fun (post) ->
            let content = choosePrimaryContent post
            {
                key = post.key
                language = content.language
                published = post.published
                title = content.title
                summary = content.summary
                body = content.body
            }: Lib.LocalizedPost
        )
    

    Lib.layout None "pizzacat83's blog" $"""
<div class="post-list">
    <div>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </div>
</div>
    """ ["/assets/index.css"]
    
