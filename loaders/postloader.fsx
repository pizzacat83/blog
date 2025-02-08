#r "../_lib/Fornax.Core.dll"
#r "../_lib/Markdig.dll"
#r "../packages/YamlDotNet/lib/netstandard2.0/YamlDotNet.dll"
#r "../packages/FsToolkit.ErrorHandling/lib/netstandard2.0/FsToolkit.ErrorHandling.dll"

open System.IO
open Markdig
open FsToolkit.ErrorHandling

type Language = English | Japanese

type PostKey = PostKey of string

type Content = {
    language: Language
    title: string
    summary: string
    body: string
}

type Post = {
    key: PostKey
    published: System.DateOnly
    contents: Content list
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

type SegmentedSource = {
    frontmatter: string option
    title: string
    body: string
}

let splitMarkdown (markdown: string): SegmentedSource  =
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

type ParsedSource = {
    language: Language
    frontmatter: FrontMatter
    title: string
    body: string
}

let readFile (path: string): Result<string, string> =
    try
        Ok (File.ReadAllText path)
    with
    | e -> Error e.Message


let loadPost (dirpath: string): Result<Post, string> =
    let key =
        dirpath
        |> Path.GetFileName
        |> PostKey

    let enpath = Path.Combine(dirpath, "en.md")
    let jppath = Path.Combine(dirpath, "jp.md")

    result {
        let! source = readFile enpath

        let source = splitMarkdown source
        let! frontmatter = source.frontmatter
                            |> Option.map parseFrontMatter
                            |> Option.either Ok (fun _ -> Error "Failed to parse frontmatter")

        let source: ParsedSource = {
            language = English
            frontmatter = frontmatter
            title = source.title
            body = renderMarkdown source.body
        }

        return! Ok {
            key = key
            published = source.frontmatter.published
            contents = [{
                language = source.language
                title = source.title
                summary = source.frontmatter.summary
                body = source.body
            }]
        }
    }

let loader (projectRoot: string) (siteContent: SiteContents) =
    let postsPath = Path.Combine(projectRoot, contentDir)
    Directory.GetDirectories(postsPath)
    |> Array.iter (fun n ->
        match loadPost n with
        | Ok post -> siteContent.Add(post)
        | Error err -> siteContent.AddError({ Path = n; Message = err; Phase = Loading })
    )

    siteContent.Add({disableLiveRefresh = false})
    siteContent
