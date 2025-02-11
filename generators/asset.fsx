#r "../fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"

open System.IO

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    let inputPath = Path.Combine(projectRoot, page)
    File.ReadAllBytes inputPath  
