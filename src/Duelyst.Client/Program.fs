module Duelyst.Client.Program

// Raylib-cs returns its native CBool from WindowShouldClose(); the implicit conversion to
// bool is exactly what we want at the `while not ...` call site below.
#nowarn "3391"

open System
open System.IO
open Raylib_cs
open Duelyst.Assets.AtlasManifest
open Duelyst.Assets.Manifest
open Duelyst.Assets.AtlasLoader
open Duelyst.Assets.SpriteAnimator

/// Demo alias from the committed vertical slice (Lyonar unit idle animation).
let private demoAlias = "f1AzuriteLionIdle"

type Model =
    { Sprite: ResolvedSprite
      ElapsedTime: float
      Texture: Texture2D }

type Msg =
    | Tick of dt: float
    | Close

/// PURE: Tick advances ElapsedTime; Close is handled by the run loop exiting (WindowShouldClose).
let update (msg: Msg) (model: Model) : Model =
    match msg with
    | Tick dt -> { model with ElapsedTime = model.ElapsedTime + dt }
    | Close -> model

/// PURE render logic (only the final DrawTexturePro call is impure Raylib IO): derive the frame to
/// draw and its destination rect from the Model, no retained/stateful widgets (Constitution IV).
let view (model: Model) : unit =
    let frame = frameAt model.Sprite model.ElapsedTime
    let scale = 4.0f
    let w = float32 frame.W * scale
    let h = float32 frame.H * scale

    let dest =
        Rectangle(float32 (Raylib.GetScreenWidth()) / 2.0f, float32 (Raylib.GetScreenHeight()) / 2.0f, w, h)

    Raylib.BeginDrawing()
    Raylib.ClearBackground(Color.Black)
    draw model.Texture frame dest model.Sprite.Rotated
    Raylib.EndDrawing()

/// Walk up from the running assembly's directory to find the repo root (marked by Duelyst.sln),
/// so `assets/` resolves the same way whether launched via `dotnet run` or the built binary.
let rec private findRepoRoot (dir: string) : string =
    if File.Exists(Path.Combine(dir, "Duelyst.sln")) then
        dir
    else
        match Directory.GetParent(dir) with
        | null -> failwith "Duelyst.sln not found in any ancestor directory of the running assembly"
        | parent -> findRepoRoot parent.FullName

let private assetsRoot () =
    Path.Combine(findRepoRoot AppContext.BaseDirectory, "assets")

[<EntryPoint>]
let main _argv =
    let root = assetsRoot ()
    let resourcesJson = File.ReadAllText(Path.Combine(root, "resources.json"))
    let atlasesJson = File.ReadAllText(Path.Combine(root, "atlases.json"))

    match parseResources resourcesJson, parseAtlases atlasesJson with
    | Error e, _ ->
        eprintfn "Failed to parse resources.json: %A" e
        1
    | _, Error e ->
        eprintfn "Failed to parse atlases.json: %A" e
        1
    | Ok resources, Ok atlases ->
        match resolve resources atlases demoAlias with
        | Error e ->
            eprintfn "Failed to resolve alias '%s': %A" demoAlias e
            1
        | Ok sprite ->
            Raylib.InitWindow(1280, 720, "Duelyst")
            Raylib.SetTargetFPS(60)

            let texture = loadTexture root sprite.Image
            let mutable model = { Sprite = sprite; ElapsedTime = 0.0; Texture = texture }

            while not (Raylib.WindowShouldClose()) do
                model <- update (Tick(float (Raylib.GetFrameTime()))) model
                view model

            Raylib.UnloadTexture(texture)
            Raylib.CloseWindow()
            0
