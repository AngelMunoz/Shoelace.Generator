namespace Sutil.Generator

open FSharp.Control.Tasks
open System

open type Text.Encoding

open System.Runtime.InteropServices
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open CliWrap
open Types
open System.Threading.Tasks


module IO =
    let private isWindows =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

    let private getBytesFromStr (strval: string) =
        let b = UTF8.GetBytes(strval)

        ReadOnlySpan b

    let downloadPackage (package: string) =
        let cmd =
            Cli
                .Wrap(if isWindows then "npx.cmd" else "npx")
                .WithArguments($"pnpm install {package}")
                .WithStandardErrorPipe(PipeTarget.ToStream(System.Console.OpenStandardError()))
                .WithStandardOutputPipe(PipeTarget.ToStream(System.Console.OpenStandardOutput()))

        cmd.ExecuteAsync()

    let private getJsonOptions () =
        let opts = JsonSerializerOptions()

        opts.AllowTrailingCommas <- true
        opts.IgnoreNullValues <- true
        opts.ReadCommentHandling <- JsonCommentHandling.Skip
        opts.Converters.Add(JsonFSharpConverter())
        opts

    let private parseShoelaceMetadata () =
        task {
            try
                let path =
                    let combined =
                        Path.Combine("./", "node_modules", "@shoelace-style", "shoelace", "dist", "metadata.json")

                    Path.GetFullPath combined

                use fileStr = File.OpenRead path

                let! serialized = JsonSerializer.DeserializeAsync<Types.ShoelaceMetadata>(fileStr, getJsonOptions ())
                return Some serialized
            with ex ->
                eprintfn "%s" ex.Message
                return None
        }

    let private writeShelaceComponentFile (root: string) (comp: SlComponent) =
        let name = comp.className.[2..]
        let path = Path.Combine(root, $"{name}.fs")
        use file = File.Create path

        let bytes =
            getBytesFromStr (Shoelace.Templates.getComponentTpl comp)

        file.Write bytes

    let private writeShoelaceLibraryFsProj (root: string) (version: string) (components: SlComponent array) =
        let library = Path.Combine(root, "Library.fs")

        let fsproj =
            Path.Combine(root, "Sutil.Shoelace.fsproj")

        use library = File.Create library

        let bytes =
            getBytesFromStr (Shoelace.Templates.getShoelaceAPIClass components)

        library.Write bytes
        use fsproj = File.Create fsproj

        let writeComponents =
            Shoelace.Templates.getFsFileReference components

        let bytes =
            getBytesFromStr (Shoelace.Templates.getFsProjTpl writeComponents version)

        fsproj.Write bytes


    let generateLibrary (componentSystem: ComponentSystem) =
        match componentSystem with 
        | ComponentSystem.Shoelace ->
            task {
                printfn "Generating Shoelace Library..."
                printfn "Downloading package @shoelace-style/shoelace"
                let! result = downloadPackage ("@shoelace-style/shoelace")
            
                if result.ExitCode <> 0 then
                    raise (Exception("Failed to Download the package"))
            
                let! metadata = parseShoelaceMetadata ()
                let path = Path.Combine("../", "Sutil.Shoelace")

                let dir = Directory.CreateDirectory(path)

                match metadata with
                | Some metadata ->
                    printfn $"Using Shoelace - {metadata.version} from {metadata.author}, {metadata.license}"
                    metadata.components
                    |> Array.Parallel.iter (writeShelaceComponentFile dir.FullName)

                    writeShoelaceLibraryFsProj dir.FullName metadata.version metadata.components
                    printfn $"Generated {metadata.components.Length} Components"
                | None ->
                    printfn "Failed to parse the metadata.json file, will not continue."
                    ()
            }
        | ComponentSystem.Fast -> Task.FromResult(())
        

