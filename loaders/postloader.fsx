#r "../_lib/Fornax.Core.dll"
#r "../_lib/Markdig.dll"

open System.IO
open Markdig

type PostKey = PostKey of string

type Post = {
    key: PostKey
    title: string
    content: string
}

type PostConfig = {
    disableLiveRefresh: bool
}


let markdownPipeline =
    MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .Build()

type PostSource = {
    title: string
    body: string
}

let splitMarkdown (markdown: string): PostSource  =
    let title = 
        markdown.Split('\n')
        |> Array.tryFind (fun x -> x.StartsWith("# "))
        |> Option.map (fun x -> x.Substring(2))
        |> Option.defaultValue "No title"

    let body =
        markdown.Split('\n')
        |> Array.filter (fun x -> not (x.StartsWith("# ")))
        |> String.concat "\n"

    {
        title = title
        body = body
    }

let renderMarkdown (markdown: string) =
    Markdown.ToHtml(markdown, markdownPipeline)

let contentDir = "posts"

let loadFile (projectRoot: string) (abspath: string): Post =
    let markdown = File.ReadAllText abspath

    let chopLength =
        if projectRoot.EndsWith(Path.DirectorySeparatorChar) then projectRoot.Length
        else projectRoot.Length + 1

    let dirPart =
        abspath
        |> Path.GetDirectoryName
        |> fun x -> x.[chopLength .. ]

    let relpath = Path.Combine(dirPart, (abspath |> Path.GetFileNameWithoutExtension) + ".md").Replace("\\", "/")

    let source = splitMarkdown markdown

    {
        key = PostKey relpath
        title = source.title
        content = renderMarkdown source.body
    }

let loader (projectRoot: string) (siteContent: SiteContents) =
    let postsPath = Path.Combine(projectRoot, contentDir)
    let options = EnumerationOptions(RecurseSubdirectories = true)
    let files = Directory.GetFiles(postsPath, "*", options)
    files
    |> Array.filter (fun n -> n.EndsWith ".md")
    |> Array.map (loadFile projectRoot)
    |> Array.iter siteContent.Add

    siteContent.Add({disableLiveRefresh = false})
    siteContent
