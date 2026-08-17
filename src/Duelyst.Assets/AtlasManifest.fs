/// PURE: types + parse atlases.json / resources.json. No IO, no Raylib.
module Duelyst.Assets.AtlasManifest

open System.Text.Json

type Descriptor =
    | Texture of img: string
    | Sprite of img: string * frame: string
    | Animation of img: string * framePrefix: string * frameDelay: float
    | Audio of path: string

type ResourcesManifest = Map<string, Descriptor>

type Frame =
    { Name: string
      X: int
      Y: int
      W: int
      H: int
      Rotated: bool
      // Cocos2d trim offsets can be fractional (subpixel), e.g. 1.5.
      OffsetX: float
      OffsetY: float
      SrcW: int
      SrcH: int }

type Atlas = { Image: string; Frames: Map<string, Frame> }

type AtlasTable = Map<string, Atlas>

type ManifestError =
    | InvalidJson of message: string
    | InvalidShape of message: string

let private tryGetString (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.String -> Option.ofObj (v.GetString())
    | _ -> None

let private tryGetNumber (el: JsonElement) (name: string) =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetDouble())
    | _ -> None

let private tryGetInt (el: JsonElement) (name: string) =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt32())
    | _ -> None

let private tryGetBool (el: JsonElement) (name: string) =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.True || v.ValueKind = JsonValueKind.False -> Some(v.GetBoolean())
    | _ -> None

let private parseDescriptor (alias: string) (el: JsonElement) : Result<Descriptor, ManifestError> =
    match tryGetString el "kind" with
    | None -> Error(InvalidShape $"missing 'kind' for alias '{alias}'")
    | Some "texture" ->
        match tryGetString el "img" with
        | Some img -> Ok(Texture img)
        | None -> Error(InvalidShape $"texture '{alias}' missing 'img'")
    | Some "sprite" ->
        match tryGetString el "img", tryGetString el "frame" with
        | Some img, Some frame -> Ok(Sprite(img, frame))
        | _ -> Error(InvalidShape $"sprite '{alias}' missing 'img'/'frame'")
    | Some "animation" ->
        match tryGetString el "img", tryGetString el "framePrefix", tryGetNumber el "frameDelay" with
        | Some img, Some prefix, Some delay -> Ok(Animation(img, prefix, delay))
        | _ -> Error(InvalidShape $"animation '{alias}' missing 'img'/'framePrefix'/'frameDelay'")
    | Some "audio" ->
        match tryGetString el "audio" with
        | Some path -> Ok(Audio path)
        | None -> Error(InvalidShape $"audio '{alias}' missing 'audio'")
    | Some other -> Error(InvalidShape $"unknown kind '{other}' for alias '{alias}'")

/// Parse assets/resources.json (contracts/resources.schema.json) into a ResourcesManifest.
let parseResources (json: string) : Result<ResourcesManifest, ManifestError> =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(InvalidShape "resources.json root must be an object")
        else
            root.EnumerateObject()
            |> Seq.fold
                (fun acc prop ->
                    match acc with
                    | Error _ -> acc
                    | Ok map ->
                        match parseDescriptor prop.Name prop.Value with
                        | Ok d -> Ok(Map.add prop.Name d map)
                        | Error e -> Error e)
                (Ok Map.empty)
    with :? JsonException as ex ->
        Error(InvalidJson ex.Message)

let private parseFrame (el: JsonElement) : Result<Frame, ManifestError> =
    match
        tryGetString el "name",
        tryGetInt el "x",
        tryGetInt el "y",
        tryGetInt el "w",
        tryGetInt el "h",
        tryGetBool el "rotated",
        tryGetNumber el "offsetX",
        tryGetNumber el "offsetY",
        tryGetInt el "srcW",
        tryGetInt el "srcH"
    with
    | Some name, Some x, Some y, Some w, Some h, Some rotated, Some offsetX, Some offsetY, Some srcW, Some srcH ->
        Ok
            { Name = name
              X = x
              Y = y
              W = w
              H = h
              Rotated = rotated
              OffsetX = offsetX
              OffsetY = offsetY
              SrcW = srcW
              SrcH = srcH }
    | _ -> Error(InvalidShape "malformed frame entry (missing/mistyped field)")

let private parseFrames (el: JsonElement) : Result<Map<string, Frame>, ManifestError> =
    el.EnumerateArray()
    |> Seq.fold
        (fun acc fEl ->
            match acc with
            | Error _ -> acc
            | Ok map ->
                match parseFrame fEl with
                | Ok f -> Ok(Map.add f.Name f map)
                | Error e -> Error e)
        (Ok Map.empty)

/// Parse assets/atlases.json (contracts/atlases.schema.json) into an AtlasTable.
let parseAtlases (json: string) : Result<AtlasTable, ManifestError> =
    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error(InvalidShape "atlases.json root must be an object")
        else
            root.EnumerateObject()
            |> Seq.fold
                (fun acc prop ->
                    match acc with
                    | Error _ -> acc
                    | Ok map ->
                        let el = prop.Value

                        match tryGetString el "image" with
                        | None -> Error(InvalidShape $"atlas '{prop.Name}' missing 'image'")
                        | Some image ->
                            match el.TryGetProperty("frames") with
                            | true, framesEl when framesEl.ValueKind = JsonValueKind.Array ->
                                match parseFrames framesEl with
                                | Ok frames -> Ok(Map.add prop.Name { Image = image; Frames = frames } map)
                                | Error e -> Error e
                            | _ -> Error(InvalidShape $"atlas '{prop.Name}' missing 'frames' array"))
                (Ok Map.empty)
    with :? JsonException as ex ->
        Error(InvalidJson ex.Message)
