# CLAUDE.md — GoldenSpinner / SpinnerWheel

## Identity
- Solution/project: `GoldenSpinner.sln` / `GoldenSpinner.csproj`
- Namespace: `GoldenSpinner` | App title: **SpinnerWheel** | Target: `net9.0`
- UI: Avalonia 11.3.11 + FluentTheme | MVVM: CommunityToolkit.Mvvm 8.2.1 | Audio: NAudio 2.2.1

---

## Critical project settings

**Compiled bindings ON** (`AvaloniaUseCompiledBindingsByDefault=true`):
- Every `.axaml` file must declare `x:DataType` on root/DataTemplate — build fails without it.
- Typos in binding paths are build errors, not runtime failures.
- Bool negation works: `{Binding !IsActive}`.

**`Avalonia.Controls.ColorPicker` requires explicit style import in `App.axaml`:**
```xml
<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />
```
Without this, `ColorView` renders as a grey rectangle — no error thrown.

**Do not add ReactiveUI.** CommunityToolkit only.

---

## Architecture

Two windows, one shared `MainWindowViewModel`:
- `MainWindow` ("SpinnerWheel — Settings") — streamer control panel with a scrollable custom tab bar (one tab per wheel, unlimited wheels).
- `SpinnerWindow` ("SpinnerWheel — OBS Capture") — OBS Window Capture target, background = `ActiveWheel.ChromaKeyColor` (default `#00FF00`).

Switching tabs → `ActiveWheelIndex` → `ActiveWheel` → SpinnerWindow updates instantly.

```
Views (pure UI)
  └── binds to
ViewModels
  MainWindowViewModel  → Wheels : ObservableCollection<WheelViewModel>, ActiveWheelIndex, ActiveWheel
  WheelViewModel       → ObservableCollection<WheelSliceViewModel> + all commands + spin physics
  WheelSliceViewModel  → wraps WheelSlice (model)
  └── depends on (constructor injection)
Services: IFilePickerService, LayoutService (JSON+ZIP), AudioService (NAudio/Win), LogService
Controls: SpinnerWheelControl — custom Control, renders via Render(DrawingContext)
Models: WheelSlice, WheelLayout — plain C#, JSON-serialisable
Converters: HexColorToBrushConverter (one-way), ColorToHexConverter (two-way, for ColorView)
```

Data flows **down**: Models → ViewModels → Views. Views never touch Models.
Services created in `MainWindow.axaml.cs` (needs `this` for `WindowFilePickerService`), injected into VM.

**Window lifecycle:**
- `MainWindow` is lifetime window; closing it exits app.
- Closing either window closes both: `MainWindow.Closing` → `_spinnerWindow.Close()`; `_spinnerWindow.Closed` → `Close()`. Uses `Closed` (not `Closing`) to avoid re-entrancy.
- Windows positioned side-by-side on `Opened` (deferred — `Screens.Primary` only available post-show).

**Mutual z-raise:** `Activated` on either window calls `BringToFrontNoActivate(other)` via Win32 `SetWindowPos(SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`. **Never use `Activate()`** — it steals focus mid-click and causes controls to require multiple clicks.

---

## Key file notes

### `MainWindowViewModel.cs`

Thin container owning an **unbounded** list of wheels. Users add wheels with the `+` button.

| Property/Method | Type | Purpose |
|----------------|------|---------|
| `Wheels` | `ObservableCollection<WheelViewModel>` | All wheels; bound to `TabList` ListBox |
| `ActiveWheelIndex` | `int` | Selected tab index; guarded against -1 (ListBox deselection artefact) |
| `ActiveWheel` | `WheelViewModel` (computed) | `Wheels[Clamp(ActiveWheelIndex, 0, Count-1)]` |
| `AddWheelCommand` | `IRelayCommand` | Adds `"Wheel N"` and selects it |
| `CloneWheel(source)` | `void` | Deep-copies source via `ToLayout()`/`ApplyLayout()`, names clone with `(2)` suffix, inserts after source, selects it |
| `DeleteWheel(wheel)` | `void` | Removes wheel; if last, adds a blank "Wheel 1"; adjusts `ActiveWheelIndex` |
| `GenerateCloneName(name)` | `string` | Strips existing `(N)` suffix via `Regex.Replace`, appends next available `(N)` |

Stores all four services as fields so `AddWheelCommand` and `CloneWheel` can create new `WheelViewModel` instances.

### `MainWindow.axaml`

**Custom scrollable tab bar** replaces the old `TabControl`:

```
DockPanel
  StackPanel (header + separator)
  Grid [◀ | ScrollViewer > ListBox (tabs) | ▶ | +]   ← tab bar
  ContentControl (Content=ActiveWheel, DataTemplate → WheelEditorPanel)
```

- `ListBox x:Name="TabList"` — horizontal `StackPanel` items panel; `SelectedIndex` two-way bound to `ActiveWheelIndex`.
- `ScrollViewer x:Name="TabScroller"` — wraps ListBox, scrollbar hidden.
- `◀`/`▶` buttons **navigate between wheel tabs** (decrement/increment `ActiveWheelIndex`), then call `ScrollSelectedTabIntoView()` which posts a `Dispatcher.UIThread.Post` to call `TabList.ScrollIntoView(vm.ActiveWheel)` after layout. This keeps the selected tab visible without exposing the scrollbar.
- `+` button bound to `AddWheelCommand`.
- **Right-click context menu** on each tab item:
  - **Rename** → calls `wheel.BeginRenameCommand` (puts tab into edit mode)
  - **Clone** → calls `vm.CloneWheel(wheel)` — deep copy with `(2)` suffix
  - **Delete** → shows `ConfirmDialog` async; on confirm calls `vm.DeleteWheel(wheel)`
- Double-click a tab label to rename (triggers `BeginRenameCommand`).

### `MainWindow.axaml.cs`

- Subscribes to `vm.Wheels.CollectionChanged` to wire `OnWheelPropertyChanged` onto new wheels and call `ScrollSelectedTabIntoView()`.
- `OnScrollLeft` / `OnScrollRight` — change `ActiveWheelIndex` by ±1, then scroll tab into view.
- `OnTabRenameClick`, `OnTabCloneClick`, `OnTabDeleteClick` — extract `WheelViewModel` from `sender is MenuItem { DataContext: WheelViewModel wheel }`.
- `OnTabDeleteClick` is `async void` — awaits `new ConfirmDialog(message).ShowAsync(this)` before acting.
- TextBox focus for rename searches `TabList` (ListBox).

### `Views/ConfirmDialog.axaml` + `.axaml.cs`

Minimal modal confirmation window. Pattern:
```csharp
// Two constructors — parameterless (Avalonia XAML loader) + message constructor.
public ConfirmDialog() { InitializeComponent(); }
public ConfirmDialog(string message) { InitializeComponent(); MessageText.Text = message; }
public async Task<bool> ShowAsync(Window owner) { await ShowDialog(owner); return _result; }
private void OnYesClick(...) { _result = true;  Close(); }
private void OnNoClick(...)  { _result = false; Close(); }
```
Always provide a public parameterless constructor (AVLN3001 warning if missing).

### `WheelViewModel.cs`
All per-wheel state, commands, and animation logic. `partial class`.

**CommunityToolkit naming:** `SpinWheelAsync` → `SpinWheelCommand` (strips `Async`, appends `Command`).

**Spin physics — velocity-based, not fixed-target:**
Five phases (all time fractions of `SpinDurationSeconds`):
1. Wind-up (0–2–5%): ease-in backwards to −60°/s
2. Acceleration (→10%): `Lerp(−windUp, peakVelocity, t²)`
3. Cruise (10–80%, random 1–5 s extra randomness baked in): constant `_peakVelocity = SpinDurationSeconds × 180 °/s`
4. Engine off (80–100%): linear drop to `peakVelocity / 2`
5. Free spin (>100%): exponential decay via `frictionRate = 0.20 + (Friction−1) × 0.28`; stops at `Math.Abs(velocity) < 0.5°/s`

`_cruiseDuration` field stores the random cruise time for the spin log.

Winner is read from `CurrentRotation` when the wheel physically stops — not pre-determined.

**Winner math:** Slices start at −90° (12 o'clock), clockwise. After stop at angle R:
```
pointerAngle = ((360 − R mod 360) mod 360 + 360) mod 360
```
Walk cumulative slice arcs; first slice where `pointerAngle < cumDeg` wins.
- Weighted: arc = `(weight / totalWeight) × 360`; active filter = `IsActive && Weight > 0`
- Unweighted: equal arcs; active filter = `IsActive` only

`WinnerIndex` is index into the **full** `Slices` collection, not the filtered list.

**`IsActive` rule:** Only post-spin logic (guarded by `UseWeightedSlices`) should set `IsActive=false`. Weight changes alone are inert. Do not auto-deactivate from `OnWeightChanged`.

**`_weightSnapshot`:** Saved by `ApplyWeightToAll`, used by `UndoWeightCommand`, cleared after spin.

**Design-time constructor** (parameterless): sets services to `null!`, calls `AddDefaultSlices()`. Used by `Design.DataContext` in `WheelEditorPanel.axaml`. Never invoke commands in design mode.

**Drag-to-spin:**
- `public bool IsSpinning => _animTimer != null;`
- `StartInertialSpinAsync(double velocityDegreesPerSecond)` — skips all phases, enters free-spin directly with the given initial velocity; plays spin-start sound; calls `FinalizeSpinAsync` on completion. Minimum threshold 30°/s. Called fire-and-forget from `SpinnerWindow.axaml.cs`.
- `FinalizeSpinAsync()` — shared post-spin logic extracted from `SpinWheelAsync`: winner determination, banner, logging, weight deduction, winner sound.

**Tick sound detection:** `OnAnimationTick` computes the current pointer-to-slice index each frame. On a change it plays the next tick sound, alternating between channel A (Tick Sound 1) and channel B (Tick Sound 2) via `_audioService.PlayTickSound(path, channelB: _tickSoundToggle)`.

**Save error handling:** `[ObservableProperty] private string? _saveError;` — set in the catch block of `SaveLayoutAsync` instead of letting an exception crash the app. Displayed as red text in the Layout expander.

**Sound properties:**
- `SpinStartSoundPath` — played once when spin begins; dedicated audio channel
- `TickSound1Path` — played on odd slice crossings
- `TickSound2Path` — played on even slice crossings

**`ToLayout()` / `ApplyLayout()` are public** — used by both save/load and `CloneWheel`. When adding new ViewModel state, always update both methods. `ToLayout()` returns a new `WheelLayout`; `ApplyLayout()` replaces all current state and calls `SpinWheelCommand.NotifyCanExecuteChanged()`.

**Confetti properties:**
| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `ShowConfetti` | `bool` | `false` | Enable confetti burst on win |
| `ConfettiImagePath` | `string?` | null | Custom image (PNG/JPEG = single frame; GIF = animated) |
| `ConfettiCount` | `int` | 120 | Number of particles (1–2000) |
| `ConfettiShapeMode` | `int` | 0 | 0=Mixed, 1=Strips, 2=Circles, 3=Triangles, 4=Stars |
| `ConfettiColorMode` | `int` | 0 | 0=Rainbow, 1=Custom colour |
| `ConfettiCustomColor` | `string` | `"#FFD700"` | CSS hex used when `ConfettiColorMode=1` |
| `IsCustomConfettiColor` | `bool` (computed) | — | `ConfettiColorMode == 1`; used for picker visibility |

**Blackout property:**
| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `BlackoutWheelMode` | `int` | 0 | 0=off, 1=reveal winner only, 2=reveal all on win |
| `BorderColorStyle` | `int` | 0 | 0=white borders, 1=black borders |

### `SpinnerWindow.axaml.cs`

**Drag-to-spin:**
- `OnPointerPressed` — if `!wheel.IsSpinning`, clears winner state, begins drag tracking. No pointer capture (avoids blocking other windows / file dialogs).
- `OnPointerMoved` — computes angular delta with wrap normalization, EMA smoothing (α = 0.65), updates `wheel.CurrentRotation`.
- `OnPointerReleased` — fires `_ = wheel.StartInertialSpinAsync(_dragVelocity)` (fire-and-forget). Minimum 30°/s enforced inside `StartInertialSpinAsync`.
- `AngleDeg(Point)` — `Math.Atan2(y − h/2, x − w/2) × (180/π)`.
- `NormalizeDelta(double)` — normalizes to (−180, 180].

### `SpinnerWheelControl.cs`
Custom `Control`. All drawing in `Render(DrawingContext)`.

**Rendering pipeline:**
```
PushGeometryClip(wheelClip)       ← aliased context → hard outer edge for OBS chroma key
  PushRenderOptions(Antialias)    ← interior is smooth
    Pass 1:   screen-space images (mode 0=Static, 2=Upright)
    Pass 2a:  PushTransform(rotation) → colour fills + mode-1 images (no borders)
    Pass 2.3: blackout fill (before borders so borders show on top)
    Pass 2b:  PushTransform(rotation) + aliased sub-options → borders only
    Pass 2.5: winner/loser overlays in screen space (BrightenWinner, DarkenLosers)
    Pass 3:   upright labels in screen space ← MUST be here, not inside PushTransform
    Pass 4:   pointer label (ShowPointerLabel, inside clip so it can't overflow)
    Pass 5:   confetti burst (drawn last, on top of everything)
  end
end
Fixed decorations outside clip (aliased, hard edges): outer ring, pointer, centre pin
```

**Blackout rendering (Pass 2.3):**
- Blackout colour = inverse of border: white borders → black fill; black borders → white fill.
- Mode 1 (reveal winner only): entire wheel blacked out until winner set; then non-winner slices blacked out.
- Mode 2 (reveal all on win): entire wheel blacked out until winner set; then removed entirely.
- Centre pin dot is also inverted when blackout is active (does not track the current slice colour).
- Labels on blacked-out slices are suppressed in Pass 3.

**Confetti particle system (Pass 5):**
- `SpawnConfetti()` — called when `WinnerIndex` transitions from -1 to ≥0 and `ShowConfetti=true`. Reads `ConfettiCount`, `ConfettiShapeMode`, `ConfettiColorMode`, `ConfettiCustomColor` at spawn time. Starts `DispatcherTimer` at 16 ms.
- `OnConfettiTick()` — advances particle positions; advances GIF frame index using accumulated ms vs per-frame delay; stops timer when all particles dead.
- `StopConfetti()` — called when `WinnerIndex` resets to -1 or `ShowConfetti` set to false.
- **Parabolic arc:** `drawSize = baseSize × 4t(1−t)` — zero at start/end (flat on wheel), full size at `t=0.5` (peak, closest to camera). Gives 3D rising-and-falling illusion.
- **Spread:** `maxSpread = radius × (0.65 + rng.NextDouble() × 0.33)` → particles land 65–98% from centre. `speed = maxSpread / lifetime` guarantees no particle exceeds its spread.
- **Particle size:** `baseSize = 16 + rng.NextDouble() × 30` px (16–46 px).
- **Shapes:** 0=strip rect (w×0.35 aspect), 1=circle, 2=triangle (`UnitTriangle`), 3=star (`UnitStar`). Triangles and stars use static `StreamGeometry` instances scaled via `Matrix.CreateScale(drawSize/2) * Matrix.CreateTranslation(px,py)` inside the rotation push.
- **`UnitTriangle`** — equilateral, circumradius=1, centred at origin, built once in static initializer.
- **`UnitStar`** — 5-pointed, outer r=1, inner r=0.382, built once in static initializer.
- **GIF frames:** loaded via `System.Drawing.Common` (Windows-only; CA1416 warnings expected). Frames decoded to `List<Bitmap>` with `List<int>` ms delays from property `0x5100`. Static PNG/JPEG uses a single `_confettiBitmap`. GIF falls back silently (try/catch) on non-Windows.
- **Cleanup:** `OnDetachedFromVisualTree` stops timer and disposes all bitmaps/frames.

**Do NOT set `EdgeMode.Antialias`** on the control — hard outer edge is essential for chroma key. Interior antialias only via `PushRenderOptions` inside the clip.

**Do NOT draw labels inside `PushTransform(rotMatrix)`** — labels must stay upright.

**Weighted arc in renderer:** weight floored at 1.0 so a weight-0 slice still shows:
```csharp
double totalWeight = useWeightedSlices ? active.Sum(s => Math.Max(1.0, s.Weight)) : active.Count;
```

**Label outline:** 8-direction 2px offset technique (Avalonia `FormattedText` has no `BuildGeometry`).

### `LayoutService.cs`
- **JSON (`SaveAsync`/`LoadAsync`):** serialises the entire `WheelLayout` object — all fields are always included automatically.
- **ZIP (`SaveZipAsync`/`LoadZipAsync`):** self-contained (`layout.json` + `img/` + `snd/`), extracted to `%TEMP%\GoldenSpinner\<guid>\` on load. Uses `System.IO.Compression` (no extra packages).
- **⚠️ ZIP save manually copies every field into a new `WheelLayout`.** Unlike JSON save, new model properties are NOT automatically included — they must be explicitly added to the `zipLayout` initialiser in `SaveZipAsync`. Forgetting a field silently drops it from ZIP saves.
- Asset paths saved in ZIP: `DefaultSoundPath`, `SpinStartSoundPath`, `TickSound1Path`, `TickSound2Path`, `ConfettiImagePath`.
- Non-asset fields that must be listed: all appearance, weight, blackout, confetti (count/shape/colour mode/custom colour), winner display fields.

### `AudioService.cs` (Windows)
NAudio `WaveOutEvent` + `AudioFileReader`. **Four independent channels** — none ever interrupts another:

| Channel | Field pair | Used for |
|---------|-----------|---------|
| Main/winner | `_waveOut` / `_reader` | Winner sound, default fallback sound |
| Spin-start | `_spinStartWaveOut` / `_spinStartReader` | Sound played when spin begins |
| Tick A | `_tickWaveOutA` / `_tickReaderA` | Tick Sound 1 (odd border crossings) |
| Tick B | `_tickWaveOutB` / `_tickReaderB` | Tick Sound 2 (even border crossings) |

```csharp
public void PlaySpinStartSound(string path)               // dedicated channel
public void PlayTickSound(string path, bool channelB)     // false→A, true→B
```

Each channel self-cleans via `PlaybackStopped` lambda (disposes reader). All four channels disposed in `Dispose()`.

### `WheelLayout.cs`
All current fields (beyond the original slices/spin/label/chroma/weight/log):
```csharp
public int     BorderColorStyle      { get; set; } = 0;       // 0=white, 1=black
public int     BlackoutWheelMode     { get; set; } = 0;       // 0=off,1=winner only,2=reveal all
public string? SpinStartSoundPath    { get; set; }
public string? TickSound1Path        { get; set; }
public string? TickSound2Path        { get; set; }
public bool    ShowConfetti          { get; set; } = false;
public string? ConfettiImagePath     { get; set; }
public int     ConfettiCount         { get; set; } = 120;
public int     ConfettiShapeMode     { get; set; } = 0;       // 0=Mixed,1=Strips,2=Circles,3=Triangles,4=Stars
public int     ConfettiColorMode     { get; set; } = 0;       // 0=Rainbow,1=Custom
public string  ConfettiCustomColor   { get; set; } = "#FFD700";
```

### `LogService.cs`
Format: `YYYY-MM-DD | HH:MM:SS | {speed,4:F1} | {friction,2} | {cruise:F1} | Result`

Example: `2026-03-14 | 15:42:07 |  4.0 |  5 | 3.7 | Prize 3`

- Speed: always 4 chars wide (`xx.x`)
- Friction: always 2 chars wide (space-padded)
- Cruise: random cruise duration in seconds (1 decimal place)
- Session separator written before first spin only; file is append-only.

### `WheelEditorPanel.axaml`
- **Appearance expander** — includes Blackout Wheel ComboBox (No / Reveal winner only / Reveal all on win) and Borders ComboBox (White / Black).
- **Winner Display expander** — includes confetti section (visible when `ShowConfetti=true`):
  - Confetti enable checkbox
  - Custom image browse/remove (PNG/JPEG/GIF)
  - Particle count `NumericUpDown` (1–2000, step 10)
  - Shape `ComboBox` (Mixed / Strips / Circles / Triangles / Stars)
  - Colour `ComboBox` (Rainbow / Custom colour)
  - Custom colour hex `TextBox` + swatch picker (only visible when `IsCustomConfettiColor=true`)
- **Sounds expander** — Spin Start Sound + Tick Sound 1 + Tick Sound 2.
- **Layout expander** — red `SaveError` TextBlock below Save/Load buttons.

### `Converters/`
- `HexColorToBrushConverter` — one-way, key `"HexColorToBrush"`, registered in `App.axaml`.
- `ColorToHexConverter` — two-way, key `"ColorToHex"`, binds `ColorView.Color` ↔ hex string, forces alpha=255.

---

## Build

**App locks `bin\Debug\net9.0\GoldenSpinner.dll` while running.** Always compile to temp:
```bash
dotnet build -o C:/Temp/gs-build-check
```
Only ask user to restart when they want to test changes. Never chain `taskkill` + build.

---

## Common change recipes

**Add a slice property:**
1. `Models/WheelSlice.cs` — add property
2. `ViewModels/WheelSliceViewModel.cs` — add `[ObservableProperty]` field
3. `WheelSliceViewModel.ToModel()` — map it
4. `WheelSliceViewModel(WheelSlice)` constructor — restore it
5. `Views/WheelEditorPanel.axaml` slice properties section — add binding row if UI-visible

**Add a ViewModel command:**
1. Write private method (`void` or `async Task`)
2. `[RelayCommand(CanExecute = nameof(...))]`
3. If CanExecute depends on `SelectedSlice`, add `[NotifyCanExecuteChangedFor(nameof(XyzCommand))]` to `_selectedSlice` field
4. Bind in AXAML: `Command="{Binding XyzCommand}"`

**Tune spin feel:** `WheelViewModel.cs` — `_windUpSpeed`, `_accelEndTime` (0.10), `_fullSpeedEndTime` (0.80), friction formula, stop threshold (0.5°/s).

**Drawing changes:** `Controls/SpinnerWheelControl.cs` — `DrawSlices`, `DrawPieSlice`, `DrawSliceLabel`, `DrawSliceImage`, `DrawPointer`.

---

## What NOT to do

- **No `WrapPanel` with spacing** — it has none. Use `StackPanel Orientation="Horizontal" Spacing="..."`.
- **No `x:CompileBindings="False"`** without strong reason — type safety is the point.
- **No `_fieldName` direct access** when an `[ObservableProperty]` field exists — use the generated PascalCase property (MVVMTK0034).
- **No ReactiveUI** — CommunityToolkit only.
- **No binary assets in JSON** — `WheelLayout` stores file paths only.
- **No `InvalidateVisual()` from background thread** — UI thread only.
- **No `Activate()` for z-raise** — use `SetWindowPos(SWP_NOACTIVATE)`. `Activate()` steals focus mid-click.
- **No auto-deactivate from `OnWeightChanged`** — only post-spin weighted logic may set `IsActive=false`.
- **No `DataContext="{Binding X}"` on a `TabItem`** with compiled-binding UserControl content — use `ContentTemplate` with `x:DataType`. (Now moot — `TabControl` replaced with `ListBox` + `ContentControl`.)
- **No `dotnet build` without `-o C:/Temp/gs-build-check` while app is running** — DLL is locked.
- **No `EdgeMode.Antialias` on `SpinnerWheelControl`** — outer edge must be pixel-hard for chroma key.
- **No labels inside `PushTransform(rotMatrix)`** — always draw labels in screen space (Pass 3).
- **No pointer capture in `SpinnerWindow`** — `e.Pointer.Capture(this)` blocks file dialogs and other window interactions. Drag works without it.
- **No single tick audio channel** — ticks need two independent channels (A/B) so alternating sounds don't cut each other off at high spin speed.
- **Never forget to update `SaveZipAsync` when adding new `WheelLayout` fields** — JSON save is automatic, but ZIP save manually lists every field. A missing field silently drops on ZIP round-trip with no error.
- **Do not add `System.Drawing.Common` GIF code paths without a try/catch** — the library is Windows-only since .NET 6 and throws `PlatformNotSupportedException` on Linux/Mac. The confetti GIF loader already handles this; keep the pattern.
- **`ConfirmDialog` requires a public parameterless constructor** — Avalonia's XAML loader needs it; omitting it raises AVLN3001 at runtime.
