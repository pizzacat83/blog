#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"
#r "../packages/FSharp.Formatting/lib/netstandard2.1/FSharp.Formatting.Common.dll"
#r "../packages/FSharp.Formatting/lib/netstandard2.1/FSharp.Formatting.Markdown.dll"
#r "../packages/YamlDotNet/lib/netstandard2.0/YamlDotNet.dll"
#r "../packages/FsToolkit.ErrorHandling/lib/netstandard2.0/FsToolkit.ErrorHandling.dll"

open System.IO
open FsToolkit.ErrorHandling
open FSharp.Formatting.Markdown

type Language = English | Japanese
let languages = [English; Japanese]

type PostKey = PostKey of string

type Asset = {
    filename: string
    content: byte[]
}

type Content = {
    language: Language
    title: string
    summary: string
    body: string

    assets: Asset list

    head: string option
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
    head: string option
}

[<CLIMutable>]
type FrontMatterSerialized = {
    published: string
    summary: string
    head: string option
}

let parseFrontMatter (frontmatter: string): FrontMatter =
    let yaml = YamlDotNet.Serialization.DeserializerBuilder().Build()
    let fm = yaml.Deserialize<FrontMatterSerialized> frontmatter

    {
        published = System.DateOnly.Parse(fm.published)
        summary = fm.summary
        head = fm.head
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

let parseSource (lang:  Language) (source: string): Result<ParsedSource, string> =
    result {
        let doc = Markdown.Parse (source, ?parseOptions=Some MarkdownParseOptions.AllowYamlFrontMatter)

        let! frontmatter =
            doc.Paragraphs
            |> List.tryPick (function YamlFrontmatter(fm, _) -> Some(fm) | _ -> None)
            |> Option.map (String.concat "\n")
            |> Option.either Ok (fun () -> Error "Missing frontmatter")
            |> Result.map parseFrontMatter

        let! title =
            doc.Paragraphs
            |> List.tryPick (function Heading(1, t, _) -> Some(t) | _ -> None)
            |> Option.either Ok (fun () -> Error "Missing h1")
        let! title = 
            match List.tryHead title with
            | Some(Literal(l, _)) -> Ok l
            | _ -> Error (sprintf "Unsupported title contents: %A" title)
    
        let body = Markdown.ToHtml (MarkdownDocument(
            doc.Paragraphs
            |> List.filter (
                function
                | YamlFrontmatter _ -> false
                | Heading(1, _, _) -> false
                | _ -> true
            ),
            doc.DefinedLinks
        ))

        return! Ok {
            language = lang
            frontmatter = frontmatter
            title = title
            body = body
        }
    }

let loadAssets (dirpath: string): Asset list =
    Directory.GetFiles(dirpath)
    |> Array.filter (fun n -> 
        let ext = Path.GetExtension n
        ext = ".png" || ext = ".jpg" || ext = ".jpeg" || ext = ".gif" || ext = ".svg"
    )
    |> Array.map (fun n ->
        {
            filename = Path.GetFileName n
            content = File.ReadAllBytes n
        }
    )
    |> Array.toList

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
                parseSource lang source |> Result.mapError (sprintf "Failed to parse post %A: %s" lang)
            )
            |> Result.mapError (String.concat "; ")

        if List.isEmpty sources then
            return! Error "No post sources found"

        let published = sources[0].frontmatter.published

        let assets = loadAssets dirpath

        let contents: Content list =
            sources
            |> List.map (fun source ->
                {
                    language = source.language
                    title = source.title
                    summary = source.frontmatter.summary
                    body = source.body

                    assets = assets

                    head = source.frontmatter.head
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
