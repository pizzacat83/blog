[<AutoOpen>]
module Model

open System.Collections.Generic

type SiteErrors = string list

type GenerationPhase =
| Loading
| Generating
type SiteError = {
    Path : string
    Message : string
    Phase : GenerationPhase
}

type SiteContents () =
    let container = new System.ComponentModel.Design.ServiceContainer()
    let errors = new Dictionary<string, SiteError>()

    member __.AddError error =
        errors.Add(error.Path, error)

    member __.TryGetError path =
        match errors.TryGetValue path with
        | true, v -> Some v
        | _ -> None

    member __.Errors () =
        List.ofSeq errors.Values

    member __.Add(value:'a) =
        let key = typeof<List<'a>>
        match container.GetService(key) with
        | :? List<'a> as result ->
            result.Add(value)
        | _ ->
            container.AddService(key, List<'a>([|value|]))

    member __.GetValues<'a> () : seq<'a> =
        let key = typeof<List<'a>>
        let result = container.GetService(key)
        result :?> IEnumerable<'a>

    member this.TryGetValues<'a> () =
        let key = typeof<List<'a>>
        if container.GetService(key) |> isNull then
            None
        else this.GetValues<'a>() |> Some

    member this.TryGetValue<'a> () =
        this.TryGetValues<'a> ()
        |> Option.bind (Seq.tryHead)

module Config =

    type GeneratorTrigger =
        ///Generator runs once, globally.
        | Once
        ///Generator runs once, for given filename (file name is relative to project root, for example `post/post.md`). It runs only if the given file exist.
        | OnFile of filename : string
        ///Generator runs for any file with given extension (for example `md`).
        | OnFileExt of extension: string
        ///Generator runs for any file matching predicate. Parameters of predicate are absolute path to project root, and path to the file relative to project root.
        | OnFilePredicate of predicate: ((string * string) -> bool)

    type GeneratorOutput =
        ///Generates a file with the same name.
        | SameFileName
        ///Generates a file with the same base name but with the extension changed.
        | ChangeExtension of newExtension: string
        ///Generates a file with name `newFileName`.
        | NewFileName of newFileName: string
        ///Generates a file with the name as the result of `mapper orignalFileName`.
        | Custom of mapper: (string -> string)
        ///Generates multiple files with each name being the result of applying the mapper to the first string of the generator output.
        ///The generator must have a type `SiteContents -> string -> string -> list<string * string>`
        | MultipleFiles of mapper: (string -> string)

    type GeneratorConfig = {
        Script: string
        Trigger: GeneratorTrigger
        OutputFile: GeneratorOutput
    }

    type Config = {
        Generators: GeneratorConfig list
    }
