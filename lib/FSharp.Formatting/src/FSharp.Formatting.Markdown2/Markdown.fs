// Modified by pizzacat83
// --------------------------------------------------------------------------------------
// F# Markdown (Main.fs)
// (c) Tomas Petricek, 2012, Available under Apache 2.0 license.
// --------------------------------------------------------------------------------------

namespace FSharp.Formatting.Markdown2

open System
open System.IO

/// Static class that provides methods for formatting
/// and transforming Markdown documents.
type Markdown internal () =
    /// Transform the provided MarkdownDocument into HTML
    /// format and write the result to a given writer.
    static member WriteHtml(doc: FSharp.Formatting.Markdown.MarkdownDocument, writer, ?newline) =
        let newline = defaultArg newline Environment.NewLine

        HtmlFormatting.formatAsHtml
            writer
            false
            false
            doc.DefinedLinks
            newline
            doc.Paragraphs

    /// Transform Markdown text into HTML format. The result
    /// will be written to the provided TextWriter.
    static member WriteHtml
        (
            markdownText: string,
            writer: TextWriter,
            ?newline
        ) =
        let doc = FSharp.Formatting.Markdown.Markdown.Parse(markdownText, ?newline = newline)

        Markdown.WriteHtml(
            doc,
            writer,
            ?newline = newline
        )

    /// Transform the provided MarkdownDocument into HTML
    /// format and return the result as a string.
    static member ToHtml(doc: FSharp.Formatting.Markdown.MarkdownDocument, ?newline) =
        let sb = new System.Text.StringBuilder()
        use wr = new StringWriter(sb)

        Markdown.WriteHtml(
            doc,
            wr,
            ?newline = newline
        )

        sb.ToString()

    /// Transform Markdown document into HTML format.
    /// The result will be returned as a string.
    static member ToHtml(markdownText: string, ?newline) =
        let doc = FSharp.Formatting.Markdown.Markdown.Parse(markdownText, ?newline = newline)

        Markdown.ToHtml(
            doc,
            ?newline = newline
        )
