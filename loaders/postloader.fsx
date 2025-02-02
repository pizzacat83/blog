#r "../_lib/Fornax.Core.dll"
#r "../_lib/Markdig.dll"
#r "../packages/YamlDotNet/lib/netstandard2.0/YamlDotNet.dll"
#r "../packages/FsToolkit.ErrorHandling/lib/netstandard2.0/FsToolkit.ErrorHandling.dll"

open System.IO
open Markdig
open FsToolkit.ErrorHandling

type PostKey = PostKey of string

type Post = {
    key: PostKey
    title: string
    published: System.DateOnly
    summary: string
    content: string
}

type PostConfig = {
    disableLiveRefresh: bool
}


let markdownPipeline =
    MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseYamlFrontMatter()
        .Build()

type PostSource = {
    frontmatter: string option
    title: string
    body: string
}

let splitMarkdown (markdown: string): PostSource  =
    let lines = markdown.Split('\n')

    let frontmatter =
        if lines[0] = "---" then
            lines[1..]
                |> Array.takeWhile (fun x -> x <> "---")
                |> String.concat "\n"
                |> Some
        else
            None

    let title = 
        lines
        |> Array.tryFind (fun x -> x.StartsWith("# "))
        |> Option.map (fun x -> x.Substring(2))
        |> Option.defaultValue "No title"

    let body =
        lines
        |> Array.filter (fun x -> not (x.StartsWith("# ")))
        |> String.concat "\n"

    {
        frontmatter = frontmatter
        title = title
        body = body
    }

let renderMarkdown (markdown: string) =
    Markdown.ToHtml(markdown, markdownPipeline)

let contentDir = "posts"

type FrontMatter = {
    published: System.DateOnly
    summary: string
}

[<CLIMutable>]
type FrontMatterSerialized = {
    published: string
    summary: string
}

let parseFrontMatter (frontmatter: string): FrontMatter =
    let yaml = YamlDotNet.Serialization.DeserializerBuilder().Build()
    let fm = yaml.Deserialize<FrontMatterSerialized> frontmatter

    {
        published = System.DateOnly.Parse(fm.published)
        summary = fm.summary
    }

let loadFile (projectRoot: string) (abspath: string): Result<Post, string> =
    let markdown = File.ReadAllText abspath

    let chopLength =
        if projectRoot.EndsWith(Path.DirectorySeparatorChar) then projectRoot.Length
        else projectRoot.Length + 1

    let dirPart =
        abspath
        |> Path.GetDirectoryName
        |> fun x -> x.[chopLength .. ]

    let relpath: string = Path.Combine(dirPart, (abspath |> Path.GetFileNameWithoutExtension) + ".md").Replace("\\", "/")

    let source = splitMarkdown markdown

    let frontmatter =
        source.frontmatter
        |> Option.map parseFrontMatter
        |> Option.either Ok (fun _ -> Error "Failed to parse frontmatter")

    result {
        let! fm = frontmatter
        return! Ok {
            key = PostKey relpath
            title = source.title
            published = fm.published
            summary = fm.summary
            content = renderMarkdown source.body
        }
    }

let loader (projectRoot: string) (siteContent: SiteContents) =
    let postsPath = Path.Combine(projectRoot, contentDir)
    let options = EnumerationOptions(RecurseSubdirectories = true)
    let files = Directory.GetFiles(postsPath, "*", options)
    files
    |> Array.filter (fun n -> n.EndsWith ".md")
    |> Array.iter (fun n ->
        match loadFile projectRoot n with
        | Ok post -> siteContent.Add(post)
        | Error err -> siteContent.AddError({ Path = n; Message = err; Phase = Loading })
    )


    siteContent.Add({disableLiveRefresh = false})
    siteContent
