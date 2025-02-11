#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#r "../packages/FSharp.Formatting/lib/netstandard2.1/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/netstandard2.1/FSharp.Formatting.Markdown.dll"
#r "../packages/YamlDotNet/lib/netstandard2.0/YamlDotNet.dll"
#r "../packages/FsToolkit.ErrorHandling/lib/netstandard2.0/FsToolkit.ErrorHandling.dll"

open System.IO
open FsToolkit.ErrorHandling

type Language = English | Japanese
let languages = [English; Japanese]

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

let readFile (path: string): string option =
    try
        Some (File.ReadAllText path)
    with
    | e -> None

let loadPost (dirpath: string): Result<Post, string> =
    let key =
        dirpath
        |> Path.GetFileName
        |> PostKey

    result {
        let! sources =
            languages
            |> List.choose (fun lang ->
                let filename = match lang with | English -> "en.md" | Japanese -> "ja.md"
                let path = Path.Combine(dirpath, filename)
                readFile path |> Option.map (fun s -> (lang, s))
            )
            |> List.traverseResultA (fun (lang, source) -> 
                result {
                    let source = splitMarkdown source
                    let! frontmatter =
                        source.frontmatter
                        |> Option.map parseFrontMatter
                        |> Option.either Ok (fun _ -> Error "Failed to parse frontmatter")

                    let body = FSharp.Formatting.Markdown.Markdown.Parse (source.body, ?parseOptions=Some FSharp.Formatting.Markdown.MarkdownParseOptions.AllowYamlFrontMatter)
                    let body = FSharp.Formatting.Markdown.Markdown.ToHtml(body)

                    return! Ok {
                        language = lang
                        frontmatter = frontmatter
                        title = source.title
                        body = body
                    }
                } |> Result.mapError (sprintf "Failed to parse post %A: %s" lang)
            )
            |> Result.mapError (String.concat "; ")

        if List.isEmpty sources then
            return! Error "No post sources found"

        let published = sources[0].frontmatter.published

        let contents: Content list =
            sources
            |> List.map (fun source ->
                {
                    language = source.language
                    title = source.title
                    summary = source.frontmatter.summary
                    body = source.body
                }
            )

        return! Ok {
            key = key
            published = published
            contents = contents
        }
    }

let loader (projectRoot: string) (siteContent: SiteContents) =
    let postsPath = Path.Combine(projectRoot, contentDir)
    Directory.GetDirectories(postsPath)
    |> Array.filter (fun n -> Path.GetFileName n <> ".obsidian")
    |> Array.iter (fun n ->
        match loadPost n with
        | Ok post -> siteContent.Add(post)
        | Error err -> siteContent.AddError({ Path = n; Message = err; Phase = Loading })
    )

    siteContent.Add({disableLiveRefresh = false})
    siteContent
