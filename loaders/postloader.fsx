#r "../_lib/Fornax.Core.dll"
#r "../_lib/Markdig.dll"

open System.IO
open Markdig

type PostKey = PostKey of string

type Post = {
    key: PostKey
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

    {
        key = PostKey relpath
        content = renderMarkdown markdown
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
