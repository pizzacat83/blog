# pizzacat83's blog

## Development

### Generator scripts

Run `fornax watch`.

### Fornax CLI (`fornax/`)

Run `./fornax/src/Fornax/resetTool.sh` to reinstall fornax.

### Dependencies of generator scripts (`lib/`)

Run `dotnet build --configuration Release lib/FSharp.Formatting/src/FSharp.Formatting.Markdown2` to rebuild the project. Then, the generator scripts will use the rebuilt one.


## License

Code inside `fornax` is licensed under the MIT License. See [the LICENSE file](./fornax/LICENSE.md) for the full license text.

Code inside `lib/FSharp.Formatting` is licensed under the Apache License 2.0. See [the LICENSE file](./lib/FSharp.Formatting/LICENSE.md) for the full license text.
