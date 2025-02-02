#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

let renderPost (post: Postloader.Post) =
    let published = post.published.ToString("yyyy-MM-dd")
    let href = $"/posts/{post.key |> (fun (Postloader.PostKey x) -> x)}"

    $"""
    <article>
        <time datetime="{published}">{published}</time>
        <h1><a href="{href}">{post.title}</a></h1>
        
        <p class="post-summary">
            {post.summary}
        </p>
    </article>
    """

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts = ctx.TryGetValues<Postloader.Post> () |> Option.defaultValue Seq.empty
    let posts =
        posts
        |> Seq.sortByDescending (fun p -> p.published)

    Lib.layout "pizzacat83's blog" $"""
<div class="post-list">
    <div>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </div>
</div>
    """ ["/assets/index.css"]
