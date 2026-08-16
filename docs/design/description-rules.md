# Description authoring rules

The checklist for writing environmental descriptions and room curation
(`assets/descriptions/<AreaBlueprintName>.json` + `desc.*` locale entries). Hand this file to any
authoring agent verbatim. Hard-won rules — each one exists because breaking it shipped a real
error. Design background: `environmental-descriptions.md` (same folder).

## Ground truth

- **Author from the LIVE game only.** Screenshots via the dev server (`DevSurvey.Frame` +
  `/screenshot`), contents via `DevSurvey.Contents(roomId)`, interactables via live
  `State.MapObjects`. **Never** from offline scene extraction — asset dumps include inactive
  duplicates and produced phantom content ("buttons that aren't here").
- **Every claim needs a CLOSE capture of that exact room.** Never infer from the corner of a wide
  shot (the "entrance porch" that turned out to be a doorless interior landing). Verify the camera
  actually moved before trusting a capture — `Frame()` targets clamp to camera bounds and can
  silently frame something else.
- `Labels()` output is a **hint, never truth** — a 30 m label radius reaches through walls. Verify
  contested claims against `State.MapObjects` and the capture.
- Directions come from **coordinates, not eyeballing**: at the canonical capture yaw 135, north is
  upper-right, east lower-right. If prose states a direction, derive it from world coords.

## What goes in

- The **permanent stage**: geometry, furniture, floors and rugs, props, decals, bloodstains, light
  sources, atmosphere.
- **Scanner-invisible ambient life and flavor** — decorative critters (the guest-bedroom cat),
  ambient scene dressing. This is *especially* valuable: the descriptions are the only channel that
  carries what the scanner cannot list. (User decision, 2026-08-14.)
- **Unflinching detail.** Mature dark-fantasy game; blind adults deserve the full picture — gore,
  cruelty, wreckage included. Never sanitize. (Only a true usage-policy conflict is flagged to the
  user — never silently omitted.)

## What stays out

- **Units** — NPCs, enemies, combat corpses. They are dynamic actors the scanner reports live;
  prose describes the stage, not the cast ("benches around bloodstained ground", not "two
  corpses"). Distinguish by DATA (map object vs unit), not by appearance.
- **Anything with lifecycle state** — traps, loot, openable/one-shot mechanisms — out of prose AND
  titles ("a trap lies hidden" read forever after the trap was disarmed). The scanner speaks them
  while they matter.
- **Nothing invented to fit a label or fill a gap.** If the capture doesn't show it and the map
  objects don't contain it, it doesn't exist.

## Structure and anchoring

- Anchors are **world coordinates, never room ids** (segmentation drifts; coordinates survive).
  Place anchors on open floor, not under furniture (sub-clearance cells can fail to resolve).
- Prose lives in the locale table: `desc.<key>.title` (short noun phrase — it names exits and
  where-am-I) + `desc.<key>.body` (2–5 sentences). Keys carry an area prefix (`dh_`,
  `shieldmaze_`). The JSON holds coordinates + keys only.
- Asset descriptions dedupe by **normalized GameObject name** (`luxery_chest_02`) in
  `_assets.json` — one entry covers every instance.

## Room curation (same file)

- `walls`: polylines (`[x,z]` points, optional `y` floor gate) seeded as zero-clearance lines —
  the watershed splits there. Expect knock-on re-splits nearby; fix them with `merges`, don't
  fight the wall geometry.
- `merges`: groups of `[x,y,z]` anchors whose rooms union after segmentation.
- Verify geometry offline first (`DevSurvey.DumpRooms` + `tools/rooms_render.py --curation`),
  then live: copy the json into the deployed assets, reset `RoomMap._builtFor`, call
  `RoomMap.Tick()`, and check every room resolves a title (an untitled "Room N" next to curated
  rooms is a bug in the curation, not a style choice).

## Workflow notes

- Fog for captures: disable the `FogOfWarArea` itself (photo-mode path) — `IsCheatOffFog` alone
  leaves unexplored terrain black. Snapshot the `All` set before disabling; `Restore()` afterwards.
- Load areas story-correctly with `/loadsave area:<Blueprint>`; teleport (`EnterArea`) only for
  areas with no save, and have the user restart the game before normal play afterwards.
- Authoring agents work from **files only** — never let them drive the live game.
- Locale hot-reload without restart: copy `ui.json` into the deployed assets, then via `/eval`
  reload the **fallback** tables (`LocalizationManager.LoadLanguage("enGB", _fallbackTables)`) —
  English IS the fallback, and the per-frame poll only watches the game's language setting.
- Offline validation is the real safety net: locale coverage (every key has title/body), duplicate
  keys across batches, orphaned anchors.
