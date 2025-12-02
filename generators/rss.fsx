
#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#load "lib.fsx"

let renderPost (post: Lib.LocalizedPost) =
    let published = post.published.ToString("ddd, dd MMM yyyy")
    let href = Lib.postHref post.language post.key

    $"""
    <item>
        <title>{post.title}</title>
        <link>https://blog.pizzacat83.com{href}</link>
        <description>{post.summary}</description>
        <pubDate>{published}</pubDate>
    </item>
    """

let postIsMigratedFromOldBlog (post: Lib.LocalizedPost) =
    // Or we should use frontmatter to mark migrated posts?
    post.published < System.DateOnly(2025, 3, 1)

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts =
        Lib.getPrimaryLocalizedPosts ctx
        |> Seq.filter (fun p -> not (postIsMigratedFromOldBlog p))
        |> Seq.sortByDescending (fun p -> p.published)

    $"""<?xml version="1.0" encoding="UTF-8" ?>
<rss version="2.0">
    <channel>
        <title>pizzacat83's blog</title>
        <link>https://blog.pizzacat83.com</link>
        <description></description>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </channel>
</rss>
    """
