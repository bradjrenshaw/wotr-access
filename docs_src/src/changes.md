# Change Log

This page tracks notable, player-facing changes. The mod is in **alpha**, so it moves quickly; for
the exact history see the [commit log](https://github.com/bradjrenshaw/wotr-access/commits/main).

## Alpha

The current alpha already covers a large slice of the game:

- Speech through your screen reader (Prism) with a Windows SAPI fallback, and custom keyboard
  navigation in mouse mode. Speech outputs are configurable per use (rate, volume, voice — including
  OneCore), and event voices can be positioned in the world.
- Main menu, the New Game flow, and character creation / level-up.
- A mod menu (Ctrl+M) with simple and full settings, a first-run setup wizard, and an audio glossary.
- Exploration: the movement cursor (with turnable facing on Q/E), the categorized scanner and review
  cursor with sight-and-route pings, sonar and other spatial-audio overlays, wall tones, room/area
  awareness with authored room descriptions, unexplored-space cycling, and world-map travel.
- The accessible map screen (Ctrl+V): a fast map cursor, the game's markers, and met NPCs locatable
  even under fog (an opt-outable Enhancement).
- Dialogue, book events, tutorial popups, loading-screen tips, and the in-game log / barks (log
  review on Ctrl+L).
- Turn-based combat and targeting (abilities, touch spells, a settable default attack, resting on
  Ctrl+R, AoE cast previews, turn delaying).
- Service windows: inventory (with keyboard drag, off-hand equipping, and stack splitting),
  character sheet, spellbook, journal, encyclopedia.
- Vendors / trade (including partial-stack amounts), looting, resting, party selection, and the
  group manager.
- Review buffers for reading a unit's details line by line.
- Party commands: select all / by member, hold position, stop, stealth, and AI toggles.
- Spoken directions in your choice of style: compass (8/16, full or short), relative to facing, or
  clock face.
