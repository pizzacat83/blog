
#r "../_lib/Fornax.Core.dll"
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

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts =
        Lib.getPrimaryLocalizedPosts ctx
        |> Seq.sortByDescending (fun p -> p.published)

    $"""
<?xml version="1.0" encoding="UTF-8" ?>
<rss version="2.0">
    <channel>
        <title>pizzacat83's blog</title>
        <link>https://blog.pizzacat83.com</link>
        <description></description>
        <language>en-us</language>
        { posts
            |> Seq.map renderPost
            |> String.concat ""
        }
    </channel>
</rss>
    """
