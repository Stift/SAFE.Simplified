﻿module Program

open System
open System.Diagnostics
open System.IO
open Fake.IO
open Fake.Core
open Fake.Core.TargetOperators

// Initialize FAKE context
Setup.context()

let path xs = Path.Combine(Array.ofList xs)

let solutionRoot = Files.findParent __SOURCE_DIRECTORY__ "root.txt";
let server = path [ solutionRoot; "server" ]
let client =  path [ solutionRoot; "client" ]
let serverTests = path [ solutionRoot; "serverTests" ]
let clientTests = path [ solutionRoot; "clientTests" ]
let clientDist = path [ client; "dist" ]
let dist = path [ solutionRoot; "dist" ]
let clientOutput = path [ dist; "wwwroot" ]

Target.create "Clean" <| fun _ ->
    // sometimes files are locked by VS for a bit, retry again until they can be deleted
    Retry.retry 5 <| fun _ -> Shell.deleteDirs [
        dist
        path [ server; "bin" ]
        path [ server; "obj" ]
        path [ serverTests; "bin" ]
        path [ serverTests; "obj" ]
        path [ client; "bin" ]
        path [ client; "obj" ]
        path [ client; ".fable" ]
        path [ clientTests; "bin" ]
        path [ clientTests; "obj" ]
        path [ clientTests; ".fable" ]
    ]

Target.create "RestoreServer" <| fun _ ->
    Retry.retry 5 <| fun _ ->
        let exitCode = Shell.Exec(Tools.dotnet, "restore", server)
        if exitCode <> 0 then failwith "Could restore packages in the server project"

Target.create "Server" <| fun _ ->
    Retry.retry 5 <| fun _ ->
        let exitCode = Shell.Exec(Tools.dotnet, "build --configuration Release", server)
        if exitCode <> 0 then failwith "Could not build the server project"

Target.create "ServerTests" <| fun _ ->
    let exitCode = Shell.Exec(Tools.dotnet, "run --configuration Release", serverTests)
    if exitCode <> 0 then failwith "Failed while while running server tests"

Target.create "RestoreClient" <| fun _ ->
    let exitCode = Shell.Exec(Tools.npm, "install", client)
    if exitCode <> 0 then failwith "failed to run `npm install` in the client directory"

Target.create "Client" <| fun _ ->
    let exitCode = Shell.Exec(Tools.npm, "run build", client)
    if exitCode <> 0 then failwith "Failed to build client"

Target.create "ClientTests" <| fun _ ->
    let exitCode = Shell.Exec(Tools.npm, "test", client)
    if exitCode <> 0 then failwith "Client tests did not pass"

Target.create "LiveClientTests" <| fun _ ->
    let exitCode = Shell.Exec(Tools.npm, "run test:live", client)
    if exitCode <> 0 then failwith "Failed to run client tests"

Target.create "Pack" <| fun _ ->
    match Shell.Exec(Tools.dotnet, sprintf "publish --configuration Release --output %s" dist, server) with
    | 0 ->
        Shell.copyDir clientOutput clientDist (fun file -> true)
    | n ->
        failwith "Failed to publish server project"

Target.create "PackNoTests" <| fun _ ->
    match Shell.Exec(Tools.dotnet, sprintf "publish --configuration Release --output %s" dist, server) with
    | 0 ->
        match Shell.Exec(Tools.npm, "run build", client) with
        | 0 ->
            Shell.copyDir clientOutput clientDist (fun file -> true)
        | _ ->
            failwith "Failed to build the client project"
    | _ ->
        failwith "Failed to build the server project"

let dependencies = [
    "Clean" ==> "RestoreServer" ==> "Server" ==> "ServerTests"
    "Clean" ==> "RestoreClient" ==> "Client"
    "ServerTests" ==> "Client" ==> "ClientTests" ==> "Pack"
    "Clean" ==> "RestoreClient" ==> "PackNoTests"
]

[<EntryPoint>]
let main (args: string[]) =
    try
        match args with
        | [| "RunDefaultOr" |] -> Target.runOrDefault "Default"
        | [| "RunDefaultOr"; target |] -> Target.runOrDefault target
        | manyArguments ->
            Console.WriteLine(Swag.logo)
            Console.Write("[Interactive Mode] Run build target: ")
            let target = Console.ReadLine()
            Target.runOrDefault target
        0
    with ex ->
        printfn "%A" ex
        1