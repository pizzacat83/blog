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

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts =
        Lib.getPrimaryLocalizedPosts ctx
        |> Seq.sortByDescending (fun p -> p.published)

    Lib.layout None "pizzacat83's blog" $"""
<div class="index-main">
    <div class="lang-filter">
    All languages / <a href="/en">English</a> / <a href="/ja">日本語</a>
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
    """ ["/assets/index.css"]
    
