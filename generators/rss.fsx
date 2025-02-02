
#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

let renderPost (post: Postloader.Post) =
    let published = post.published.ToString("ddd, dd MMM yyyy")
    let href = $"/posts/{post.key |> (fun (Postloader.PostKey x) -> x)}"

    $"""
    <item>
        <title>{post.title}</title>
        <link>https://blog.pizzacat83.com{href}</link>
        <description>{post.summary}</description>
        <pubDate>{published}</pubDate>
    </item>
    """

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts = ctx.TryGetValues<Postloader.Post> () |> Option.defaultValue Seq.empty
    let posts =
        posts
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
