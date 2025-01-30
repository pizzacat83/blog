#r "../_lib/Fornax.Core.dll"
#load "lib.fsx"

let generate (ctx : SiteContents) (projectRoot: string) (page: string) =
    Lib.layout "pizzacat83's blog" "hello"
