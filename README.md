# PerfectSpinner

A feature-rich spinner wheel desktop application built with **.NET 9** and **Avalonia UI 11**, designed for streamers and content creators. Spin the wheel live on stream via OBS Window Capture with full chroma key support, multi-wheel management, weighted slices, confetti, sounds, and more.

---

## Table of Contents

- [Features at a Glance](#features-at-a-glance)
- [Two-Window Design](#two-window-design)
- [Getting Started](#getting-started)
- [Settings Reference](#settings-reference)
  - [Spin](#spin)
  - [Appearance](#appearance)
  - [Slices](#slices)
  - [Weights](#weights)
  - [Troll Mode](#troll-mode)
  - [Winner Display & Confetti](#winner-display--confetti)
  - [Sounds](#sounds)
  - [Blackout Wheel](#blackout-wheel)
  - [Layout (Save / Load)](#layout-save--load)
- [Multi-Wheel Tabs](#multi-wheel-tabs)
- [OBS Setup](#obs-setup)
- [Spin Physics](#spin-physics)
- [Save & Load Formats](#save--load-formats)
- [Spin Log](#spin-log)
- [Publishing](#publishing)
- [Project Structure](#project-structure)

---

## Features at a Glance

- **Unlimited wheels** — multiple wheel tabs, each fully independent
- **Spin All** — fire every wheel simultaneously with one button
- **Drag-to-spin** — click and drag the wheel in the capture window to send it spinning
- **Chain triggers** — configure any slice to automatically spin a second wheel when it wins
- **Troll mode** — probability-based surprise animations that play after the wheel stops
- **Weighted slices** — probabilistic spinning with per-slice weights and undo
- **Per-slice customisation** — label, colour, image (PNG/JPG), sound, winner message override
- **Three image modes** — Static, Rotating, Upright
- **Confetti burst** — particles in five shapes, rainbow or custom colour, or animated GIF
- **Blackout mode** — hide slice contents until a winner is revealed
- **Four independent audio channels** — spin-start, tick A, tick B, winner sounds never interrupt each other
- **Master volume** — app-wide volume slider in the header controls all channels simultaneously
- **OBS-ready** — chroma key background, hard-edge clip boundary, zero-focus-steal z-ordering
- **Save / Load** — JSON (portable settings) or self-contained ZIP (bundles all assets)
- **Spin log** — timestamped CSV-style log of every result
- **30 FPS cap** — for lower-end machines

---

## Two-Window Design

PerfectSpinner opens two windows side by side:

| Window | Title | Purpose |
|--------|-------|---------|
| **Settings** | PerfectSpinner — Settings | Configure and control wheels |
| **OBS Capture** | PerfectSpinner — OBS Capture | Add as Window Capture source in OBS |

- Switching the active tab in Settings instantly changes what the capture window shows.
- Clicking either window brings both to the front without stealing keyboard focus.
- Closing either window closes both and exits the application.

---

## Getting Started

### Prerequisites

| Tool | Minimum version |
|------|----------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0 |
| Visual Studio 2022 *(optional)* | 17.8+ |
| Avalonia VS extension *(optional)* | For AXAML previewer |

### Run from source

```bash
# Clone and run
git clone https://github.com/murkantor/GoldenSpinner.git
cd GoldenSpinner
dotnet run
```

### Build check (without locking the running app)

```bash
dotnet build -o C:/Temp/gs-build-check
```

---

## Settings Reference

All settings below are per-wheel and saved with the layout.

---

### Spin

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| **SPIN!** | — | — | Starts the wheel (disabled while already spinning) |
| **↺ Reset** | — | — | Stops the wheel, clears the winner, returns to 0° |
| **Speed** | 1.0 – 30.0 s | 4.0 s | Controls how long the overall spin cycle runs |
| **Friction** | 1 – 10 | 5 | How quickly the wheel slows down in free-spin (higher = stops faster) |
| **Randomise Start** | — | — | Jumps the wheel to a random angle before spinning |
| **Randomise Order** | — | — | Shuffles the slice list (Fisher-Yates) |

> **Spin All Wheels** — the button above the editor panel fires every wheel tab's spin simultaneously.

#### Master Volume

The **🔊 volume slider** in the top-right of the header controls the playback level for all four audio channels at once (0–100%). Adjusting it takes effect immediately, including on any tick sounds currently playing.

#### Drag-to-Spin

Click and drag anywhere inside the OBS capture window to rotate the wheel by hand. When you release, the wheel coasts to a stop using the same friction physics as a normal spin. The minimum release speed to trigger a spin is 30°/s.

---

### Appearance

| Setting | Options | Default | Description |
|---------|---------|---------|-------------|
| **Slice Image Mode** | Static / Rotating / Upright | Static | How per-slice images are rendered |
| **Show Labels** | On / Off | On | Show text inside slices |
| **Show Pointer Label** | On / Off | Off | Show the current slice's name near the top pointer |
| **Label Bold** | On / Off | Off | Bold weight for label text |
| **Font** | Default, Arial, Courier New, Georgia, Impact, Tahoma, Times New Roman, Trebuchet MS, Verdana | Default | Label typeface |
| **Font Size** | 0 – 72 px | 0 | 0 = auto-scales with slice count; any other value is fixed |
| **Label Colour** | White text / Black text | White text | Applies to all labels; border is the inverse colour |
| **Borders** | White / Black | White | Outer ring and slice divider colour |
| **Pointer Position** | Top / Right | Top | Moves the pointer triangle and the win-detection point to the top or the right side of the wheel |
| **Chroma Key Colour** | Any hex colour | #00FF00 | SpinnerWindow background (set this in your OBS Chroma Key filter) |
| **Blackout Wheel** | See [Blackout Wheel](#blackout-wheel) | Off | — |

**Image Modes:**

| Mode | Behaviour |
|------|-----------|
| **Static** | Image stays fixed at the wheel centre; the rotating wedge acts as a window over it |
| **Rotating** | Image rotates with its slice (anchored at the wheel centre) |
| **Upright** | Image orbits with its slice at 50% radius but never rotates — always right-side-up |

---

### Slices

The right panel shows the slice list with a toolbar:

- **`+`** — Add a new slice (auto-coloured from a 12-colour cycling palette)
- **`−`** — Remove the selected slice
- **`▲` / `▼`** — Move the selected slice up or down

Each slice in the list shows a checkbox (active/inactive toggle), a colour swatch, and its label.

#### Per-Slice Properties

| Property | Description |
|----------|-------------|
| **Label** | Text displayed inside the slice on the wheel |
| **Winner Label** | If set, overrides the wheel's Winner Message Template when this slice wins; leave blank to use the template |
| **Colour** | Slice background colour (hex); click the swatch for a colour picker |
| **Weight** | Relative probability when weighted mode is on (0 = excluded; see [Weights](#weights)) |
| **Image** | PNG or JPG shown inside the slice; browse to assign, remove to clear |
| **Sound** | WAV or MP3 played when this slice wins; overrides the wheel-level default sound |
| **Chain Trigger** | Select another wheel from the dropdown; if this slice wins, that wheel automatically spins after a 1.5 s pause. Set to *— None —* to disable |
| **Active** | Untick to exclude this slice from the wheel entirely |

---

### Weights

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| **Use Weighted Slices** | On / Off | Off | Enable probabilistic mode |
| **Default Weight** | 1 – 100 | 3 | Used when clicking Apply |
| **Apply to All** | — | — | Sets every slice to the default weight; snapshots old values |
| **Undo Weight** | — | — | Restores the snapshot (one level; cleared after a spin) |

When weighted mode is on:
- Each slice's arc = `(weight / totalWeight) × 360°`
- Slices with weight 0 are excluded for that spin (set inactive automatically at spin start)
- After each spin the winning slice's weight is decremented by 1 (min 0)
- Weights of exactly 0 still render at a minimum 1° arc so they remain visible

---

### Troll Mode

Troll Mode adds a chance for the wheel to perform a surprise animation after it stops — changing the result just before the winner is declared. Great for keeping live audiences on their toes.

| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| **Troll Mode** | On / Off | Off | Enables the troll system for this wheel |
| **Chance** | 0 – 100 | 30 | Percentage probability (per spin) that a troll animation fires |

When a troll activates, one of nine effects is picked at random:

| Effect | What happens |
|--------|-------------|
| **Little Tick** | One last hop — pointer moves back one slice |
| **Second Thoughts** | Wheel reverses slightly then creeps forward one slice |
| **Second Wind** | Sudden burst of energy — wheel accelerates for 2+ full extra rotations |
| **Victory Lap** | Dignified extra full rotation, lands on exactly the same result |
| **The Shakes** | Earthquake oscillation, then a dramatic snap to a different slice |
| **Skip Ahead** | Smooth skip forward two slices |
| **Boomerang** | Reverses partway then springs past the original result |
| **Spin Doctors** | Three rapid full rotations with a slow dramatic finish |
| **Big Slice** | A random slice inflates to half the wheel, holds, then Second Wind fires — repeats up to three times, stacking inflated slices, before all revert |

> The final winner is always determined from wherever the wheel physically rests after all troll animations complete.

---

### Winner Display & Confetti

| Setting | Default | Description |
|---------|---------|-------------|
| **Winner Message Template** | `🎉  %t%!` | Shown in the banner; `%t%` is replaced with the winning slice's label |
| **Default Sound** | — | WAV/MP3 played on win; per-slice sounds take priority |
| **Brighten Winner** | Off | Adds a white overlay on the winning slice |
| **Darken Losers** | Off | Adds a dark overlay on all non-winning slices |
| **Invert Loser Text** | Off | Swaps label text/border colours on losing slices |
| **Show Confetti** | Off | Enables the confetti burst when a winner is decided |

#### Confetti Settings (visible when Show Confetti is on)

| Setting | Options / Range | Default | Description |
|---------|-----------------|---------|-------------|
| **Custom Image** | PNG / JPEG / GIF path | — | Use an image instead of coloured shapes; GIF will animate |
| **Particle Count** | 1 – 2000 | 120 | Number of particles in the burst |
| **Shape** | Mixed / Strips / Circles / Triangles / Stars | Mixed | Particle shape |
| **Colour Mode** | Rainbow / Custom | Rainbow | Rainbow cycles through 9 bright colours; Custom uses a single hex |
| **Custom Colour** | Hex colour | #FFD700 (gold) | Active only when Colour Mode = Custom |

**How confetti works:**
- Particles burst from the wheel centre on win, spreading to 65–98% of the wheel radius
- Size peaks at midpoint of lifetime (parabolic arc — simulates rising and falling)
- Particles range from 16–46 px base size and live 1.5–3.5 seconds
- GIF animation is supported on Windows; falls back to a static PNG on Linux/macOS

---

### Sounds

Four audio channels operate independently — none can interrupt another.

| Channel | Setting | Description |
|---------|---------|-------------|
| **Spin Start** | Spin Start Sound | Played once when a spin begins |
| **Tick A** | Tick Sound 1 | Played on odd slice-border crossings while spinning |
| **Tick B** | Tick Sound 2 | Played on even slice-border crossings while spinning |
| **Winner** | Default Sound / Per-slice Sound | Played when the wheel stops; per-slice sound takes priority |

- If only one tick sound is set, it plays on both channels.
- Tick sounds are pre-loaded into memory; each crossing just seeks to position 0 and replays (no file I/O per tick).
- Supports **WAV** and **MP3** on all platforms.

**Platform audio:**

| Platform | Method |
|----------|--------|
| Windows | NAudio (`WaveOutEvent` + `AudioFileReader`) |
| macOS | `afplay` (built-in; supports WAV and MP3) |
| Linux | `aplay` (WAV) / `mpg123` (MP3 — install separately) |

---

### Blackout Wheel

Hides slice content to build suspense during a live spin.

| Mode | Behaviour |
|------|-----------|
| **Off** | Normal rendering — all slices visible at all times |
| **Reveal winner only** | Entire wheel is blacked out during the spin. After stopping, only the winning slice is revealed; all others stay blacked out |
| **Reveal all on win** | Entire wheel is blacked out during the spin. After stopping, the blackout is removed entirely and all slices are visible |

- Blackout colour is always the inverse of the border colour (white borders → black fill; black borders → white fill).
- Slice labels on blacked-out slices are suppressed.
- The centre-pin dot inverts to match the blackout colour.

---

### Layout (Save / Load)

| Setting | Description |
|---------|-------------|
| **Save** | Opens a save dialog; choose `.json` or `.zip` |
| **Load** | Opens a file dialog; supports `.json` and `.zip` |
| **Log Spins** | Appends every spin result to `./logs/spins.log` |
| **Cap frame rate to 30 FPS** | Reduces the animation timer from ~60 fps to ~30 fps — useful for lower-end PCs |

See [Save & Load Formats](#save--load-formats) for full details.

---

## Multi-Wheel Tabs

You can create an unlimited number of independent wheels.

| Action | How |
|--------|-----|
| **Add wheel** | Click **`+`** in the tab bar |
| **Switch wheel** | Click a tab, or use **`◀`** / **`▶`** |
| **Rename** | Double-click the tab label, or right-click → Rename |
| **Clone** | Right-click → Clone (deep copy with `(2)`, `(3)` suffix) |
| **Delete** | Right-click → Delete (requires confirmation; creates a blank wheel if last) |
| **Spin All** | Click **Spin All Wheels** above the editor — fires every wheel simultaneously |

The active tab controls what appears in the OBS capture window in real-time.

---

## OBS Setup

1. Launch PerfectSpinner — both windows open side by side.
2. In OBS, add a **Window Capture** source and select **PerfectSpinner — OBS Capture**.
3. Add a **Chroma Key** filter to the source. Set the key colour to match **Settings → Appearance → Chroma Key Colour** (default: `#00FF00` bright green).
4. Adjust Similarity and Smoothness as needed.
5. Resize / crop the source in OBS to fit your scene layout.
6. The red pointer triangle always sits at 12 o'clock. The wheel spins clockwise.

> **Tip:** The outer wheel boundary is intentionally pixel-hard (aliased) for a clean chroma key edge. Interior elements are antialiased.

---

## Spin Physics

Each spin runs through five phases:

| Phase | Duration | Behaviour |
|-------|----------|-----------|
| **Wind-up** | 0–5% | Brief backwards motion (anticipation) |
| **Acceleration** | 5–10% | Ease-in forward, quadratic curve |
| **Cruise** | 10–80% + 1–5 s random | Constant peak velocity — adds unpredictability |
| **Engine off** | 80–100% | Linear drop to half speed |
| **Free-spin** | Until stopped | Exponential decay; Friction slider controls the rate |

- **Peak velocity** = `SpinDuration × 180 °/s`
- **Friction rate** = `0.20 + (Friction − 1) × 0.28` — higher friction → faster stop
- **Stops** when `|velocity| < 0.5 °/s`
- **Winner** is read from the physical rotation angle when the wheel stops — it is never pre-determined

---

## Save & Load Formats

### JSON (`.json`)

Stores all settings as a JSON document. Asset paths (images, sounds) are stored as **absolute paths on disk**. The file is portable only if the referenced assets are also accessible from the loading machine.

### ZIP (`.zip`)

A self-contained archive containing:

```
layout.json      ← all wheel settings
img/             ← all image assets
snd/             ← all audio assets
```

On load the ZIP is extracted to `%TEMP%\PerfectSpinner\<guid>\`. Asset paths in `layout.json` are relative within the archive. This format is fully portable — everything travels in one file.

### JSON Schema Overview

```json
{
  "name": "My Wheel",
  "spinDurationSeconds": 4.0,
  "friction": 5,
  "useWeightedSlices": false,
  "globalWeight": 3.0,
  "showLabels": true,
  "showPointerLabel": false,
  "labelFontIndex": 0,
  "labelFontSize": 0,
  "labelColorStyle": 0,
  "labelBold": false,
  "sliceImageMode": 0,
  "chromaKeyColor": "#00FF00",
  "borderColorStyle": 0,
  "blackoutWheelMode": 0,
  "brightenWinner": false,
  "darkenLosers": false,
  "invertLoserText": false,
  "winnerMessageTemplate": "🎉  %t%!",
  "defaultSoundPath": null,
  "spinStartSoundPath": null,
  "tickSound1Path": null,
  "tickSound2Path": null,
  "showConfetti": false,
  "confettiImagePath": null,
  "confettiCount": 120,
  "confettiShapeMode": 0,
  "confettiColorMode": 0,
  "confettiCustomColor": "#FFD700",
  "logSpins": true,
  "capTo30Fps": false,
  "trollMode": false,
  "trollChance": 30,
  "slices": [
    {
      "id": "00000000-...",
      "label": "Grand Prize",
      "colorHex": "#E74C3C",
      "weight": 1.0,
      "isActive": true,
      "imagePath": null,
      "soundPath": null,
      "winnerLabel": null,
      "triggerWheelId": null
    }
  ]
}
```

---

## Spin Log

When **Log Spins** is enabled, every result is appended to `./logs/spins.log`:

```
=== Session 2026-03-14 15:42:00 ===
2026-03-14 | 15:42:07 |  4.0 |  5 | 3.7 | Grand Prize
2026-03-14 | 15:43:11 |  6.0 |  3 | 2.1 | Prize 2
```

Columns: `Date | Time | Speed (s) | Friction | Cruise duration (s) | Winner label`

---

## Publishing

### Windows (x64)

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/win-x64
```

Output: `publish/win-x64/PerfectSpinner.exe`

### Linux (x64)

```bash
dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/linux-x64

chmod +x ./publish/linux-x64/PerfectSpinner
```

Linux audio requirements:
- WAV: `aplay` (`sudo apt install alsa-utils`)
- MP3: `mpg123` (`sudo apt install mpg123`)

### macOS

```bash
# Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/osx-arm64

# Intel
dotnet publish -c Release -r osx-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/osx-x64
```

macOS uses `afplay` (built-in) for both WAV and MP3.

> **Note:** GIF-animated confetti requires `System.Drawing.Common` which is Windows-only. On Linux/macOS the GIF silently falls back to a static image.

---

## Project Structure

```
PerfectSpinner/
├── Models/
│   ├── WheelSlice.cs              – Serialisable data model for one slice
│   └── WheelLayout.cs             – Root save/load container
├── ViewModels/
│   ├── ViewModelBase.cs           – ObservableObject base (CommunityToolkit)
│   ├── WheelSliceViewModel.cs     – Observable wrapper + cached colour + loaded bitmap
│   ├── WheelViewModel.cs          – Per-wheel state, commands, spin animation, physics
│   ├── WheelChoiceItem.cs         – Display record for chain-trigger wheel selector
│   └── MainWindowViewModel.cs     – Wheel collection, tabs, Spin All, chain routing
├── Views/
│   ├── MainWindow.axaml           – Settings window (tab bar + editor panel)
│   ├── MainWindow.axaml.cs        – Code-behind; service wiring, tab interactions
│   ├── SpinnerWindow.axaml        – OBS capture window
│   ├── SpinnerWindow.axaml.cs     – Drag-to-spin input handling
│   ├── WheelEditorPanel.axaml     – Full per-wheel settings UI
│   └── ConfirmDialog.axaml        – Modal yes/no confirmation dialog
├── Controls/
│   └── PerfectSpinnerControl.cs   – Custom Control; full wheel renderer (DrawingContext)
├── Services/
│   ├── IFilePickerService.cs      – File-picker abstraction
│   ├── WindowFilePickerService.cs – Avalonia StorageProvider implementation
│   ├── LayoutService.cs           – JSON + ZIP save/load
│   ├── AudioService.cs            – 4-channel NAudio playback (Windows) + shell fallback
│   └── LogService.cs              – Spin result logging
├── Converters/
│   ├── HexColorToBrushConverter.cs – "#RRGGBB" → SolidColorBrush (one-way)
│   └── ColorToHexConverter.cs      – Color ↔ "#RRGGBB" (two-way, for ColorView)
└── Assets/
    └── avalonia-logo.ico
```

**Stack:** .NET 9 · Avalonia 11.3.11 · CommunityToolkit.Mvvm 8.2.1 · NAudio 2.2.1
