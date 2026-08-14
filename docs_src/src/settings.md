# Settings

Press **Ctrl+M** anywhere to open the **mod menu**. It holds **Settings**, lets you re-run the
**setup wizard**, opens **Help** — with **Read documentation** (this book, in your browser) and the
**Audio glossary**, which plays every mod sound on demand with an explanation of what it means —
and links to the Discord and Patreon.

## The setup wizard

The first launch runs a setup wizard automatically, and you can re-run it from the mod menu at any
time. It walks you through the most important choices as a series of steps with a roadmap header you
can jump around in:

- **Speech** — which speech output to use (your screen reader, or Windows SAPI) and its rate /
  volume.
- **Movement** — continuous vs. tiled cursor movement, speed, and tile size.
- **Wall tones** and **sonar** — whether they play, and which kinds of things the sonar pings.
- **Event feedback** — whether combat and world events (damage, healing, spellcasting, …) are spoken
  aloud, and how, including distinct voices for enemies, allies, neutrals, and sourceless events.

The wizard's presets configure many settings at once; anything it sets can still be fine-tuned
afterward.

## The simple settings screen

The mod menu's **Settings** entry opens a small, curated screen with the controls most people
actually want, grouped as:

- **Speech** — the speech output (Auto = your screen reader, a specific screen reader, SAPI, or
  Clipboard) and the tuning that applies to it: rate, volume, and voice.
- **Audio cues** — the master volume, then one node per sound system (sonar, slope, wall tones, fog,
  objects): when it's active (off / when moving / continuous) and its volume. The sonar node also
  carries per-type **Play sound** switches — which kinds of things ping.
- **Announcements** — "how much does it talk" presets: menu verbosity, scanner detail, the
  **direction type** (compass in 8 or 16 points, short spoken-letter forms, relative to your facing,
  or clock face), log detail, and event speech level.
- **Cursor** — movement speed, room-change announcements, and the path/AoE speech toggles.
- **Input** — every mod action as a rebindable binding, with a clash warning on capture.

Everything here is a view over the same settings the full screen edits — nothing is duplicated, and
hand-tuning in the full screen is never lost (a preset that no longer matches reads as "Custom").

## All settings

The **All settings** button at the bottom opens the full tabbed browser:

- **Audio** — master volume and every system volume.
- **Enhancements** — deliberate, opt-outable departures from what a sighted player gets, where that
  materially helps a blind player. First entry: **Neutral NPCs ignore fog of war** — people you have
  met stay locatable on the map even while fog currently hides them (on by default; turn it off for
  strict sighted parity).
- **Events** — one entry per event the mod can announce (damage, healing, buffs, deaths, …), with
  per-source settings (party / enemy / neutral / sourceless) and the speech configuration each uses;
  plus **additional speech configurations** so events can speak in different voices, positioned in
  the world where the engine supports it.
- **Exploration** — the shared cursor behaviour (speed, wall slide, first-held direction priority,
  review-cycle reset) and each sensing system's defaults (grid, spatial, slope, wall tones, object
  and fog cues, path info).
- **Input** — the same rebindable bindings as the simple screen.
- **Log** — which game-log message types are spoken, with presets (Default RTWP / Default Turn
  Based / Nothing).
- **Overlays** — the spatial-audio overlay roster: each overlay's composition and per-system
  overrides, plus add / rename / remove.
- **Scanner** — what the scanner lists (per-type Listed switches), the sonar sound assigned to each
  entity type, **scan announcements** (which parts of an item's line are spoken — name, type, health,
  location and its distance / direction / height sub-toggles) with per-type overrides, and the
  **review cursor sounds**: which sound each of the four sight-and-route outcomes plays (any can be
  Silent).
- **Speech** — the default speech output and its tuning, plus the additional speech configurations
  used by events.
- **UI** — announcement verbosity for menus and controls, with per-element-type overrides.

Every tab has a **Reset to defaults** for just that tab, and a reset-everything below it.

## Notes

- Your Wrath Access settings are stored separately from the game's, so **updating the mod never
  resets them**, and you can reset any single category back to its defaults without touching the
  rest.
- The first-launch flag is independent of the settings, so a full "reset all" won't re-trigger the
  setup wizard.
