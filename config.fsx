#r "fornax/src/Fornax.Core/bin/Release/net8.0/Fornax.Core.dll"

open Config
open System.IO

let postPredicate (projectRoot: string, pagePath: string) =
    let ext = Path.GetExtension pagePath
    ext = ".md" && pagePath.StartsWith "posts/"

let assetsPredicate (projectRoot: string, page: string) =
    page.StartsWith "assets/"

let config = {
    Generators = [
        // {Script = "less.fsx"; Trigger = OnFileExt ".less"; OutputFile = ChangeExtension "css" }
        // {Script = "sass.fsx"; Trigger = OnFileExt ".scss"; OutputFile = ChangeExtension "css" }
        {Script = "post.fsx"; Trigger = Once; OutputFile = MultipleFiles id }
        {Script = "asset.fsx"; Trigger = OnFilePredicate assetsPredicate; OutputFile = SameFileName }
        {Script = "indexlocal.fsx"; Trigger = Once; OutputFile = MultipleFiles id }
        {Script = "index.fsx"; Trigger = Once; OutputFile = NewFileName "index.html" }
        {Script = "rss.fsx"; Trigger = Once; OutputFile = NewFileName "rss.xml" }
        // {Script = "about.fsx"; Trigger = Once; OutputFile = NewFileName "about.html" }
        // {Script = "contact.fsx"; Trigger = Once; OutputFile = NewFileName "contact.html" }
    ]
}
