
#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#load "lib.fsx"

open System.Xml.Linq

let renderPost (post: Lib.LocalizedPost) =
    let published = post.published.ToString("ddd, dd MMM yyyy")
    let href = Lib.postHref post.language post.key

    XElement("item",
        XElement("title", post.title),
        XElement("link", $"https://blog.pizzacat83.com{href}"),
        XElement("description", post.summary),
        XElement("pubDate", published)
    )

let postIsMigratedFromOldBlog (post: Lib.LocalizedPost) =
    // Or we should use frontmatter to mark migrated posts?
    post.published < System.DateOnly(2025, 3, 1)

type Utf8StringWriter() =
    inherit System.IO.StringWriter()
    
    override this.Encoding :  System.Text.Encoding = new System.Text.UTF8Encoding(false)

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let posts =
        Lib.getPrimaryLocalizedPosts ctx
        |> Seq.filter (fun p -> not (postIsMigratedFromOldBlog p))
        |> Seq.sortByDescending (fun p -> p.published)
    
    let feed =
        XElement("rss",
            XAttribute("version", "2.0"),
            XElement("channel",
                XElement("title", "pizzacat83's blog"),
                XElement("link", "https://blog.pizzacat83.com"),
                XElement("description", ""),
                posts |> Seq.map renderPost
            )
        )

    let doc = XDocument feed

    let buf = new Utf8StringWriter()
    let xmlWriter = System.Xml.XmlWriter.Create(buf, System.Xml.XmlWriterSettings(
        Indent = true,
        OmitXmlDeclaration = false,
        Encoding = System.Text.Encoding.UTF8
    ))

    doc.Save xmlWriter
    xmlWriter.Close()

    buf.ToString()
