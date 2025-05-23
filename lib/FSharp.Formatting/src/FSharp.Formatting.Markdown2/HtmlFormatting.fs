﻿// Modified by pizzacat83
// --------------------------------------------------------------------------------------
// F# Markdown (HtmlFormatting.fs)
// (c) Tomas Petricek, 2012, Available under Apache 2.0 license.
// --------------------------------------------------------------------------------------

module FSharp.Formatting.Markdown2.HtmlFormatting

open System
open System.IO
open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Collections
open FSharp.Formatting.Markdown

/// Information about a rendered heading
type RenderedHeadingInfo = {
    level: int
    html: string
    anchor: string
}

// --------------------------------------------------------------------------------------
// Formats Markdown documents as an HTML file
// --------------------------------------------------------------------------------------

/// Basic escaping as done by Markdown
let internal htmlEncode (code: string) =
    code.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

/// Basic escaping as done by Markdown including quotes
let internal htmlEncodeQuotes (code: string) =
    (htmlEncode code).Replace("\"", "&quot;")

/// Lookup a specified key in a dictionary, possibly
/// ignoring newlines or spaces in the key.
let internal (|LookupKey|_|) (dict: IDictionary<_, _>) (key: string) =
    [ key; key.Replace("\r\n", ""); key.Replace("\r\n", " "); key.Replace("\n", ""); key.Replace("\n", " ") ]
    |> List.tryPick (fun key ->
        match dict.TryGetValue(key) with
        | true, v -> Some v
        | _ -> None)

/// Generates a unique string out of given input
type internal UniqueNameGenerator() =
    let generated = new System.Collections.Generic.Dictionary<string, int>()

    member _.GetName(name: string) =
        let ok, i = generated.TryGetValue name

        if ok then
            generated.[name] <- i + 1
            sprintf "%s-%d" name i
        else
            generated.[name] <- 1
            name

/// Context passed around while formatting the HTML
type internal FormattingContext =
    { LineBreak: unit -> unit
      Newline: string
      Writer: TextWriter
      Links: IDictionary<string, string * string option>
      WrapCodeSnippets: bool
      GenerateHeaderAnchors: bool
      UniqueNameGenerator: UniqueNameGenerator
      ParagraphIndent: unit -> unit
      DefineSymbol: string
      Footnotes: Dictionary<string, string * MarkdownSpans> }

let internal bigBreak (ctx: FormattingContext) () = ctx.Writer.Write(ctx.Newline)

let internal smallBreak (ctx: FormattingContext) () = ctx.Writer.Write(ctx.Newline)

let internal noBreak (_ctx: FormattingContext) () = ()

/// Write MarkdownSpan value to a TextWriter
let rec internal formatSpan (ctx: FormattingContext) span =
    match span with
    | LatexDisplayMath(body, _) ->
        // use mathjax grammar, for detail, check: http://www.mathjax.org/
        ctx.Writer.Write("<span class=\"math\">\\[" + (htmlEncode body) + "\\]</span>")
    | LatexInlineMath(body, _) ->
        // use mathjax grammar, for detail, check: http://www.mathjax.org/
        ctx.Writer.Write("<span class=\"math\">\\(" + (htmlEncode body) + "\\)</span>")

    | AnchorLink(id, _) -> ctx.Writer.Write("<a name=\"" + id + "\">&#160;</a>")
    | EmbedSpans(cmd, _) -> formatSpans ctx (cmd.Render())
    | Literal(str, _) -> ctx.Writer.Write(str)
    | HardLineBreak(_) -> ctx.Writer.Write("<br />" + ctx.Newline)
    | IndirectLink(body, _, key, _) when key.StartsWith("^") ->
        // Process footnote reference
        let footnoteId = if key.StartsWith("^") then key.Substring(1) else key
        let index = 
            if ctx.Footnotes.ContainsKey(footnoteId) then
                ctx.Footnotes.Count
            else
                // Store the footnote content for later rendering
                match ctx.Links.TryGetValue(key) with
                | true, (content, _) -> 
                    // Content is the footnote text from the markdown
                    ctx.Footnotes.Add(footnoteId, (content, body))
                | _ -> ()
                ctx.Footnotes.Count

        // Render the footnote reference as a superscript with link
        ctx.Writer.Write(sprintf "<sup class=\"footnote-ref\"><a href=\"#fn%d\" id=\"fnref%d\">[%d]</a></sup>" index index index)    
    | IndirectLink(body, _, LookupKey ctx.Links (link, title), _)
    | DirectLink(body, link, title, _) ->
        ctx.Writer.Write("<a href=\"")
        ctx.Writer.Write(htmlEncode link)

        match title with
        | Some title ->
            ctx.Writer.Write("\" title=\"")
            ctx.Writer.Write(htmlEncodeQuotes title)
        | _ -> ()

        ctx.Writer.Write("\">")
        formatSpans ctx body
        ctx.Writer.Write("</a>")

    | IndirectLink(body, original, _, _) ->
        ctx.Writer.Write("[")
        formatSpans ctx body
        ctx.Writer.Write("]")
        ctx.Writer.Write(original)

    | IndirectImage(body, _, LookupKey ctx.Links (link, title), _)
    | DirectImage(body, link, title, _) ->
        ctx.Writer.Write("<img src=\"")
        ctx.Writer.Write(htmlEncodeQuotes link)
        ctx.Writer.Write("\" alt=\"")
        ctx.Writer.Write(htmlEncodeQuotes body)

        match title with
        | Some title ->
            ctx.Writer.Write("\" title=\"")
            ctx.Writer.Write(htmlEncodeQuotes title)
        | _ -> ()

        ctx.Writer.Write("\" />")
    | IndirectImage(body, original, _, _) ->
        ctx.Writer.Write("[")
        ctx.Writer.Write(body)
        ctx.Writer.Write("]")
        ctx.Writer.Write(original)

    | Strong(body, _) ->
        ctx.Writer.Write("<strong>")
        formatSpans ctx body
        ctx.Writer.Write("</strong>")
    | InlineCode(body, _) ->
        ctx.Writer.Write("<code>")
        ctx.Writer.Write(htmlEncode body)
        ctx.Writer.Write("</code>")
    | Emphasis(body, _) ->
        ctx.Writer.Write("<em>")
        formatSpans ctx body
        ctx.Writer.Write("</em>")

/// Write list of MarkdownSpan values to a TextWriter
and internal formatSpans ctx = List.iter (formatSpan ctx)

/// generate anchor name from Markdown text
let internal formatAnchor (ctx: FormattingContext) (spans: MarkdownSpans) =
    let extractWords (text: string) =
        Regex.Matches(text, @"\w+") |> Seq.cast<Match> |> Seq.map (fun m -> m.Value)

    let rec gather (span: MarkdownSpan) : string seq =
        seq {
            match span with
            | Literal(str, _) -> yield! extractWords str
            | Strong(body, _) -> yield! gathers body
            | Emphasis(body, _) -> yield! gathers body
            | DirectLink(body, _, _, _) -> yield! gathers body
            | _ -> ()
        }

    and gathers (spans: MarkdownSpans) = Seq.collect gather spans

    spans
    |> gathers
    |> String.concat "-"
    |> fun name -> if String.IsNullOrWhiteSpace name then "header" else name
    |> ctx.UniqueNameGenerator.GetName

// Render footnotes section at the end of the document
let internal renderFootnotes (ctx: FormattingContext) =
    if ctx.Footnotes.Count > 0 then
        ctx.Writer.Write("<hr>" + ctx.Newline)
        ctx.Writer.Write("<section>" + ctx.Newline)
        ctx.Writer.Write("<ol>" + ctx.Newline)
        
        // Sort footnotes by their order in the document
        let orderedFootnotes = 
            ctx.Footnotes 
            |> Seq.mapi (fun i kvp -> (i+1, kvp.Key, kvp.Value))
            |> Seq.sortBy (fun (i,_,_) -> i)

        for (i, id, (content, originalRef)) in orderedFootnotes do
            ctx.Writer.Write(sprintf "<li id=\"fn%d\"><p>" i)
            
            // Output the footnote content
            ctx.Writer.Write(content)
            
            // Add a back link to the reference
            ctx.Writer.Write(sprintf " <a href=\"#fnref%d\">↩︎</a>" i)
            
            ctx.Writer.Write("</p>" + ctx.Newline)
            ctx.Writer.Write("</li>" + ctx.Newline)
        
        ctx.Writer.Write("</ol>" + ctx.Newline)
        ctx.Writer.Write("</section>" + ctx.Newline)

let internal withInner ctx f =
    use sb = new StringWriter()
    let newCtx = { ctx with Writer = sb }
    f newCtx
    sb.ToString()

/// Write a MarkdownParagraph value to a TextWriter as HTML
let rec internal formatParagraph (ctx: FormattingContext) paragraph =
    match paragraph with
    | LatexBlock(_env, lines, _) ->
        // use mathjax grammar, for detail, check: http://www.mathjax.org/
        let body = String.concat ctx.Newline lines

        ctx.Writer.Write("<p><span class=\"math\">\\[" + (htmlEncode body) + "\\]</span></p>")

    | EmbedParagraphs(cmd, _) -> formatParagraphs ctx (cmd.Render())
    | Heading(n, spans, _) ->
        ctx.Writer.Write("<h" + string<int> n + ">")

        formatSpans ctx spans
        if ctx.GenerateHeaderAnchors then
            let anchorName = formatAnchor ctx spans
            ctx.Writer.Write(sprintf """<a name="%s" class="anchor" href="#%s">#</a>""" anchorName anchorName)

        ctx.Writer.Write("</h" + string<int> n + ">")
    | Paragraph(spans, _) ->
        ctx.ParagraphIndent()
        ctx.Writer.Write("<p>")

        for span in spans do
            formatSpan ctx span

        ctx.Writer.Write("</p>")
    | HorizontalRule _ -> ctx.Writer.Write("<hr />")
    | CodeBlock(code, _, _fence, language, _, _) ->
        if ctx.WrapCodeSnippets then
            ctx.Writer.Write("<table class=\"pre\"><tr><td>")

        if String.IsNullOrWhiteSpace(language) then
            ctx.Writer.Write(sprintf "<pre><code>")
        else
            let langCode = sprintf "language-%s" language
            ctx.Writer.Write(sprintf "<pre><code class=\"%s\">" langCode)

        ctx.Writer.Write(htmlEncode code)
        ctx.Writer.Write("</code></pre>")

        if ctx.WrapCodeSnippets then
            ctx.Writer.Write("</td></tr></table>")
    | OutputBlock(code, "text/html", _) -> ctx.Writer.Write(code)
    | OutputBlock(code, _, _) ->
        if ctx.WrapCodeSnippets then
            ctx.Writer.Write("<table class=\"pre\"><tr><td>")

        ctx.Writer.Write(sprintf "<pre><code>")
        ctx.Writer.Write(htmlEncode code)
        ctx.Writer.Write("</code></pre>")

        if ctx.WrapCodeSnippets then
            ctx.Writer.Write("</td></tr></table>")
    | TableBlock(headers, alignments, rows, _) ->
        let aligns =
            alignments
            |> List.map (function
                | AlignLeft -> " align=\"left\""
                | AlignRight -> " align=\"right\""
                | AlignCenter -> " align=\"center\""
                | AlignDefault -> "")

        ctx.Writer.Write("<table>")
        ctx.Writer.Write(ctx.Newline)

        match headers with
        | None -> ()
        | Some headers ->
            ctx.Writer.Write("<thead>" + ctx.Newline + "<tr class=\"header\">" + ctx.Newline)

            for cell, align in Seq.zip headers aligns do
                ctx.Writer.Write("<th" + align + ">")

                for paragraph in cell do
                    formatParagraph { ctx with LineBreak = noBreak ctx } paragraph

                ctx.Writer.Write("</th>" + ctx.Newline)

            ctx.Writer.Write("</tr>" + ctx.Newline + "</thead>" + ctx.Newline)

        ctx.Writer.Write("<tbody>" + ctx.Newline)

        for id, row in rows |> List.mapi (fun i r -> (i + 1, r)) do
            ctx.Writer.Write("<tr class=\"" + (if id % 2 = 1 then "odd" else "even") + "\">" + ctx.Newline)

            for cell, align in Seq.zip row aligns do
                ctx.Writer.Write("<td" + align + ">")

                for paragraph in cell do
                    formatParagraph { ctx with LineBreak = noBreak ctx } paragraph

                ctx.Writer.Write("</td>" + ctx.Newline)

            ctx.Writer.Write("</tr>" + ctx.Newline)

        ctx.Writer.Write("</tbody>" + ctx.Newline)
        ctx.Writer.Write("</table>")
        ctx.Writer.Write(ctx.Newline)

    | ListBlock(kind, items, _) ->
        let tag = if kind = Ordered then "ol" else "ul"
        ctx.Writer.Write("<" + tag + ">" + ctx.Newline)

        for body in items do
            ctx.Writer.Write("<li>")

            match body with
            // Simple Paragraph
            | [ Paragraph([ MarkdownSpan.Literal(s, _) ], _) ] when not (s.Contains(ctx.Newline)) -> ctx.Writer.Write s
            | _ ->
                let inner =
                    withInner ctx (fun ctx ->
                        body
                        |> List.iterInterleaved (formatParagraph { ctx with LineBreak = noBreak ctx }) (fun () ->
                            ctx.Writer.Write(ctx.Newline)))

                let wrappedInner =
                    if inner.Contains(ctx.Newline) then
                        ctx.Newline + inner + ctx.Newline
                    else
                        inner

                ctx.Writer.Write(wrappedInner)

            ctx.Writer.Write("</li>" + ctx.Newline)

        ctx.Writer.Write("</" + tag + ">")
    | QuotedBlock(body, _) ->
        ctx.ParagraphIndent()
        ctx.Writer.Write("<blockquote>" + ctx.Newline)

        formatParagraphs
            { ctx with
                ParagraphIndent = fun () -> ctx.ParagraphIndent() (*; ctx.Writer.Write("  ")*) }
            body

        ctx.ParagraphIndent()
        ctx.Writer.Write("</blockquote>")
    | Span(spans, _) -> formatSpans ctx spans
    | InlineHtmlBlock(code, _, _) -> ctx.Writer.Write(code)
    | OtherBlock(lines, _) ->
        if ctx.WrapCodeSnippets then
            ctx.Writer.Write("<table class=\"pre\"><tr><td>")

        ctx.Writer.Write(sprintf "<pre><code>")

        for (code, _) in lines do
            ctx.Writer.Write(htmlEncode code)

        ctx.Writer.Write("</code></pre>")
    | YamlFrontmatter(_lines, _) -> ()

    ctx.LineBreak()

/// Write a list of MarkdownParagraph values to a TextWriter
and internal formatParagraphs ctx paragraphs =
    let length = List.length paragraphs
    let smallCtx = { ctx with LineBreak = smallBreak ctx }
    let bigCtx = { ctx with LineBreak = bigBreak ctx }

    for last, paragraph in paragraphs |> Seq.mapi (fun i v -> (i = length - 1), v) do
        formatParagraph (if last then smallCtx else bigCtx) paragraph

/// Format Markdown document and write the result to
/// a specified TextWriter. Parameters specify newline character
/// and a dictionary with link keys defined in the document.
let formatAsHtml writer generateAnchors wrap links newline paragraphs =
    // Create context for HTML formatting
    let formattingCtx = 
        { Writer = writer
          Links = links
          Newline = newline
          LineBreak = ignore
          WrapCodeSnippets = wrap
          GenerateHeaderAnchors = generateAnchors
          UniqueNameGenerator = new UniqueNameGenerator()
          ParagraphIndent = ignore
          DefineSymbol = "HTML"
          Footnotes = new Dictionary<string, string * MarkdownSpans>() }
    
    // Format paragraphs
    formatParagraphs formattingCtx paragraphs
    
    // Render footnotes at the end if any exist
    renderFootnotes formattingCtx

/// Write a MarkdownParagraph value to a TextWriter as HTML
let rec internal headingOfParagraph ctx (paragraph: MarkdownParagraph): RenderedHeadingInfo option =

    match paragraph with
    | Heading(n, spans, _) ->
        // capture the rendered content
        let ctx = { ctx with Writer = new StringWriter() }
        formatSpans ctx spans
        let anchorName = formatAnchor ctx spans
        
        Some {
            level = n
            html = ctx.Writer.ToString()
            anchor = anchorName }
    | _ -> None

let internal collectHeadingsFromParagraphs ctx paragraphs =
    paragraphs
    |> Seq.map (headingOfParagraph ctx)
    |> Seq.choose id

let collectHeadings  wrap links newline paragraphs =
    let ctx = 
        { Writer = new StringWriter()
          Links = links
          Newline = newline
          LineBreak = ignore
          WrapCodeSnippets = wrap
          GenerateHeaderAnchors = true
          UniqueNameGenerator = new UniqueNameGenerator()
          ParagraphIndent = ignore
          DefineSymbol = "HTML"
          Footnotes = new Dictionary<string, string * MarkdownSpans>() }

    collectHeadingsFromParagraphs ctx paragraphs
