module Fornax

open System
open System.IO
open Argu
open Suave
open Suave.Filters
open Suave.Operators

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open System.Reflection
open Logger

type FornaxExiter () =
    interface IExiter with
        member x.Name = "fornax exiter"
        member x.Exit (msg, errorCode) =
            if errorCode = ErrorCode.HelpText then
                printf "%s" msg
                exit 0
            else
                errorfn "Error with code %A received - exiting." errorCode
                printf "%s" msg
                exit 1


type [<CliPrefix(CliPrefix.DoubleDash)>] WatchOptions =
    | Port of int
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Port _ -> "Specify a custom port (default: 8080)"

type [<CliPrefix(CliPrefix.DoubleDash)>] NewOptions =
    | [<AltCommandLine("-t")>] Template of string
    | [<AltCommandLine("-o")>] Output of string
with
    interface IArgParserTemplate with
        member s.Usage = 
            match s with
            | Template _ -> "Specify a template from an HTTPS git repo or local folder"
            | Output _ -> "Specify an output folder"

type [<CliPrefix(CliPrefix.None)>] Arguments =
    | Build
    | Watch of ParseResults<WatchOptions>
    | Version
    | Clean
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Build -> "Build web site"
            | Watch _ -> "Start watch mode rebuilding "
            | Version -> "Print version"
            | Clean -> "Clean output and temp files"

/// Used to keep track of when content has changed,
/// thus triggering the websocket to update
/// any listeners to refresh.
let signalContentChanged = new Event<Choice<unit, Error>>()

let createFileWatcher dir handler =
    let fileSystemWatcher = new FileSystemWatcher()
    fileSystemWatcher.Path <- dir
    fileSystemWatcher.EnableRaisingEvents <- true
    fileSystemWatcher.IncludeSubdirectories <- true
    fileSystemWatcher.NotifyFilter <- NotifyFilters.DirectoryName ||| NotifyFilters.LastWrite ||| NotifyFilters.FileName
    fileSystemWatcher.Created.Add handler
    fileSystemWatcher.Changed.Add handler
    fileSystemWatcher.Deleted.Add handler

    /// Adding handler to trigger websocket/live refresh
    let contentChangedHandler _ =
        signalContentChanged.Trigger(Choice<unit,Error>.Choice1Of2 ())
        GeneratorEvaluator.removeItemFromGeneratorCache()

    signalContentChanged.Trigger(Choice<unit,Error>.Choice1Of2 ())
    fileSystemWatcher.Created.Add contentChangedHandler
    fileSystemWatcher.Changed.Add contentChangedHandler
    fileSystemWatcher.Deleted.Add contentChangedHandler

    fileSystemWatcher

/// Websocket function that a page listens to so it
/// knows when to refresh.
let ws (webSocket : WebSocket) (context: HttpContext) =
    informationfn "Opening WebSocket - new handShake"
    socket {
        try
            while true do
                do! Async.AwaitEvent signalContentChanged.Publish
                informationfn "Signalling content changed"
                let emptyResponse = [||] |> ByteSegment
                do! webSocket.send Close emptyResponse true
        finally
            informationfn "Disconnecting WebSocket"
    }

let getWebServerConfig port =
    match port with
    | Some port ->
        { defaultConfig with
            bindings =
                [ HttpBinding.create Protocol.HTTP Net.IPAddress.Loopback port ] }
    | None ->
        defaultConfig

let router basePath =
    let pubdir = Path.Combine(basePath, "_public")
    choose [
        path "/" >=> Redirection.redirect "/index.html"
        Files.browse pubdir
        path "/websocket" >=> handShake ws
        (fun ctx ->
            // return ./foo/index.html when /foo is requested
            let newPath = Path.Combine(ctx.request.path, "index.html")
            Files.browseFile pubdir newPath ctx
        )
    ]

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<Arguments>(programName = "fornax",errorHandler=FornaxExiter())

    let results = parser.ParseCommandLine(inputs = argv).GetAllResults()

    if List.isEmpty results then
        errorfn "No arguments provided.  Try 'fornax help' for additional details."
        printfn "%s" <| parser.PrintUsage()
        1
    elif List.length results > 1 then
        errorfn "More than one command was provided.  Please provide only a single command.  Try 'fornax help' for additional details."
        printfn "%s" <| parser.PrintUsage()
        1
    else
        let result = List.tryHead results
        let cwd = Directory.GetCurrentDirectory ()

        match result with
        | Some Build ->
            try
                let sc = SiteContents ()
                do generateFolder sc cwd false
                0
            with
            | FornaxGeneratorException message ->
                message |> stringFormatter |> errorfn
                1
            | exn ->
                errorfn "An unexpected error happend: %O" exn
                1
        | Some (Watch watchOptions) ->
            let mutable lastAccessed = Map.empty<string, DateTime>
            let waitingForChangesMessage = "Generated site with errors. Waiting for changes..."

            let sc = SiteContents ()


            let guardedGenerate () =
                try
                    do generateFolder sc cwd true
                with
                | FornaxGeneratorException message ->
                    message |> stringFormatter |> errorfn 
                    waitingForChangesMessage |> stringFormatter |> informationfn
                | exn ->
                    errorfn "An unexpected error happend: %O" exn
                    exit 1

            guardedGenerate ()

            use watcher = createFileWatcher cwd (fun e ->
                let pathDirectories = 
                    Path.GetRelativePath(cwd,e.FullPath)
                        .Split(Path.DirectorySeparatorChar)
                
                let shouldHandle =
                    pathDirectories
                    |> Array.exists (fun fragment ->
                        fragment = "_public" ||     
                        fragment = ".sass-cache" ||    
                        fragment = ".git" ||           
                        fragment = ".ionide")
                    |> not

                if shouldHandle then
                    let lastTimeWrite = File.GetLastWriteTime(e.FullPath)
                    match lastAccessed.TryFind e.FullPath with
                    | Some lt when Math.Abs((lt - lastTimeWrite).Seconds) < 1 -> ()
                    | _ ->
                        informationfn "[%s] Changes detected: %s" (DateTime.Now.ToString("HH:mm:ss")) e.FullPath
                        lastAccessed <- lastAccessed.Add(e.FullPath, lastTimeWrite)
                        guardedGenerate ())

            let webServerConfig = getWebServerConfig (watchOptions.TryPostProcessResult(<@ Port @>, uint16))
            startWebServerAsync webServerConfig (router cwd) |> snd |> Async.Start
            okfn "[%s] Watch mode started." (DateTime.Now.ToString("HH:mm:ss"))
            informationfn "Press any key to exit."
            Console.ReadKey() |> ignore
            informationfn "Exiting..."
            0
        | Some Version -> 
            let assy = Assembly.GetExecutingAssembly()
            let v = assy.GetCustomAttributes<AssemblyVersionAttribute>() |> Seq.head
            printfn "%s" v.Version
            0
        | Some Clean ->
            let publ = Path.Combine(cwd, "_public")
            let sassCache = Path.Combine(cwd, ".sass-cache")
            let deleter folder = 
                match Directory.Exists(folder) with
                | true -> Directory.Delete(folder, true)
                | _ -> () 
            try
                [publ ; sassCache] |> List.iter deleter
                0
            with
            | _ -> 1
        | None ->
            errorfn "Unknown argument"
            printfn "%s" <| parser.PrintUsage()
            1
