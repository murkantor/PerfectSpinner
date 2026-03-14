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

| Property | Type | Purpose |
|----------|------|---------|
| `Wheels` | `ObservableCollection<WheelViewModel>` | All wheels; bound to `TabList` ListBox |
| `ActiveWheelIndex` | `int` | Selected tab index; guarded against -1 (ListBox deselection artefact) |
| `ActiveWheel` | `WheelViewModel` (computed) | `Wheels[Clamp(ActiveWheelIndex, 0, Count-1)]` |
| `AddWheelCommand` | `IRelayCommand` | Adds `"Wheel N"` and selects it |

Stores all four services as fields so `AddWheelCommand` can create new `WheelViewModel` instances.

### `MainWindow.axaml`

**Custom scrollable tab bar** replaces the old `TabControl`:

```
DockPanel
  StackPanel (header + separator)
  Grid [◀ | ScrollViewer > ListBox (tabs) | ▶ | +]   ← tab bar
  ContentControl (Content=ActiveWheel, DataTemplate → WheelEditorPanel)
```

- `ListBox x:Name="TabList"` — horizontal `StackPanel` items panel; `SelectedIndex` two-way bound to `ActiveWheelIndex`.
- `ScrollViewer x:Name="TabScroller"` — wraps ListBox, scrollbar hidden; `◀`/`▶` buttons call `OnScrollLeft`/`OnScrollRight` in code-behind (±150 px offset).
- `+` button bound to `AddWheelCommand`.
- Double-click a tab label to rename (same as before).

### `MainWindow.axaml.cs`

- Subscribes to `vm.Wheels.CollectionChanged` to wire `OnWheelPropertyChanged` onto new wheels and auto-scroll `TabScroller` to the end.
- `OnScrollLeft` / `OnScrollRight` — adjust `TabScroller.Offset.X`.
- TextBox focus for rename now searches `TabList` (ListBox) instead of `WheelTabs` (TabControl).

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
    Pass 1: screen-space images (mode 0=Static, 2=Upright)
    Pass 2: PushTransform(rotation) → fills + borders + mode-1 images
    Pass 2.5: winner/loser overlays in screen space (BrightenWinner, DarkenLosers)
    Pass 3: upright labels in screen space ← MUST be here, not inside PushTransform
  end
end
Fixed decorations outside clip: outer ring, pointer, centre pin
```

**Do NOT set `EdgeMode.Antialias`** on the control — hard outer edge is essential for chroma key. Interior antialias only via `PushRenderOptions` inside the clip.

**Do NOT draw labels inside `PushTransform(rotMatrix)`** — labels must stay upright.

**Weighted arc in renderer:** weight floored at 1.0 so a weight-0 slice still shows:
```csharp
double totalWeight = useWeightedSlices ? active.Sum(s => Math.Max(1.0, s.Weight)) : active.Count;
```

**Label outline:** 8-direction 2px offset technique (Avalonia `FormattedText` has no `BuildGeometry`).

### `LayoutService.cs`
- **JSON:** absolute asset paths.
- **ZIP:** self-contained (`layout.json` + `img/` + `snd/`), extracted to `%TEMP%\GoldenSpinner\<guid>\` on load. Uses `System.IO.Compression` (no extra packages).
- ZIP saves and restores all sound paths: `DefaultSoundPath`, `SpinStartSoundPath`, `TickSound1Path`, `TickSound2Path`.

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
New fields added:
```csharp
public string? SpinStartSoundPath { get; set; }
public string? TickSound1Path     { get; set; }
public string? TickSound2Path     { get; set; }
```

### `LogService.cs`
Format: `YYYY-MM-DD | HH:MM:SS | {speed,4:F1} | {friction,2} | {cruise:F1} | Result`

Example: `2026-03-14 | 15:42:07 |  4.0 |  5 | 3.7 | Prize 3`

- Speed: always 4 chars wide (`xx.x`)
- Friction: always 2 chars wide (space-padded)
- Cruise: random cruise duration in seconds (1 decimal place)
- Session separator written before first spin only; file is append-only.

### `WheelEditorPanel.axaml`
- **"Sounds" expander** (was "Tick Sounds") contains:
  - Spin Start Sound — Browse + Remove buttons
  - Separator
  - Tick Sound 1 — Browse + Remove
  - Tick Sound 2 — Browse + Remove
- **Layout expander** has a red `SaveError` TextBlock below Save/Load buttons (only visible when `SaveError` is non-empty).

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
