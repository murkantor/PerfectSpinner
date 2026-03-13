# CLAUDE.md — GoldenSpinner / SpinnerWheel

This file is the primary reference for Claude Code sessions working on this codebase.
Read it in full before making any changes.

---

## Project identity

| Property | Value |
|----------|-------|
| Solution file | `GoldenSpinner.sln` |
| Project file | `GoldenSpinner.csproj` |
| Root namespace | `GoldenSpinner` |
| Target framework | `net9.0` |
| UI framework | Avalonia UI 11.3.11 |
| MVVM library | CommunityToolkit.Mvvm 8.2.1 |
| App name shown to user | **SpinnerWheel** |

The folder on disk is named `GoldenSpinner` but the application title shown in the window is
"SpinnerWheel". Keep both names in mind — the internal namespace is always `GoldenSpinner`.

---

## Critical project settings

### `GoldenSpinner.csproj`

```xml
<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
```

**Compiled bindings are ON by default.** This has several consequences:

1. Every `.axaml` file that uses `{Binding}` expressions must declare `x:DataType` on the root
   element (or the `DataTemplate` element) so the Avalonia compiler knows the exact type to bind
   against. Without it the build will fail with a binding resolution error.
2. Bindings are resolved at compile time — typos in property names become build errors, not
   silent runtime failures.
3. Converters still work with `{Binding ..., Converter={StaticResource MyConverter}}` or
   `{Binding ..., Converter={x:Static SomeConverters.Property}}`.
4. The `x:DataType` must exactly match the C# type (including nullability) of whatever object
   will be the `DataContext` at runtime.
5. Bool negation in bindings is supported: `{Binding !IsActive}`.

### NuGet packages in use

| Package | Version | Purpose |
|---------|---------|---------|
| `Avalonia` | 11.3.11 | Core UI framework |
| `Avalonia.Desktop` | 11.3.11 | Desktop platform support (Win/Linux/Mac) |
| `Avalonia.Themes.Fluent` | 11.3.11 | Microsoft Fluent Design theme |
| `Avalonia.Fonts.Inter` | 11.3.11 | Inter font (used by `.WithInterFont()`) |
| `Avalonia.Diagnostics` | 11.3.11 | Dev-tools overlay (Debug builds only) |
| `Avalonia.Controls.ColorPicker` | 11.3.11 | `ColorView` control used in colour flyouts |
| `CommunityToolkit.Mvvm` | 8.2.1 | Source-generated MVVM boilerplate |
| `NAudio` | 2.2.1 | In-process WAV + MP3 playback on Windows |

Do **not** add ReactiveUI — the project deliberately uses CommunityToolkit only.

`Avalonia.Controls.ColorPicker` is a **separate package** from core Avalonia. Its styles
must be explicitly included in `App.axaml`:
```xml
<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />
```
Without this line `ColorView` renders as a grey rectangle (no error is thrown).

---

## Two-window architecture (OBS streaming design)

The app runs as **two separate windows** that share one `MainWindowViewModel`:

```
┌──────────────────────────────┐     ┌──────────────────────────────────┐
│  MainWindow  (Settings)      │     │  SpinnerWindow  (OBS Capture)    │
│  Title: "SpinnerWheel —      │     │  Title: "SpinnerWheel —          │
│          Settings"           │     │          OBS Capture"            │
│  Contains:                   │     │  Contains:                       │
│  • SPIN + ↺ Reset buttons    │     │  • SpinnerWheelControl (full)    │
│  • Speed + Friction controls │     │  • Winner banner overlay         │
│  • Chroma key colour picker  │     │  • Background = ChromaKeyColor   │
│  • Weights section           │     │                                  │
│  • Save / Load layout        │     │  In OBS: Window Capture source   │
│  • Full slice editor         │     │  + Chroma Key filter             │
└──────────────┬───────────────┘     └──────────────────────────────────┘
               │ creates + owns                   ▲
               │ DataContext = vm                 │ DataContext = same vm
               └──────────────────────────────────┘
```

**Why this works for streaming:**
- `SpinnerWindow.Background` is bound to `ChromaKeyColor` (default `#00FF00` broadcast green).
- `SpinnerWheelControl` draws only the circle and its contents — no background fill — so
  the corners and the area outside the wheel show through to the solid chromakey colour.
- OBS Window Capture on `SpinnerWindow` + a Chroma Key filter removes the solid colour,
  leaving a perfectly circular wheel + winner text floating over the stream scene.
- The streamer interacts only with `MainWindow`. `SpinnerWindow` can be sized/positioned
  independently to match their stream layout.

**Window lifecycle:**
- `MainWindow` is the app's lifetime window (`desktop.MainWindow`). Closing it exits the app.
- Closing **either** window closes both. `MainWindow.Closing` calls `_spinnerWindow.Close()`;
  `_spinnerWindow.Closed` (past-tense, fires after the window is gone) calls `Close()` on
  `MainWindow`. Using `Closed` rather than `Closing` avoids re-entrancy.
- On startup both windows are positioned **side by side, centred on the primary display**
  in the `Opened` event (deferred because `Screens.Primary` is only queryable after the
  window is visible). Logical sizes are converted to physical pixels via `screen.Scaling`.
- **Mutual z-raise** — clicking either window brings both to the front via Win32
  `SetWindowPos(SWP_NOACTIVATE)`. This raises the other window's Z-order without stealing
  focus, which was the root cause of controls requiring multiple clicks (old `Activate()`
  call stole focus mid-click before `PointerReleased` completed).

## Architecture diagram

```
┌─────────────────────────────────────────────┐
│  Views / AXAML  (pure UI, no logic)         │
│  MainWindow  – Settings UI                  │
│  SpinnerWindow – OBS capture UI             │
│    Both: DataContext = MainWindowViewModel  │
└────────┬────────────────────────────────────┘
         │ binds to
┌────────▼────────────────────────────────────┐
│  ViewModels  (all application logic)        │
│  MainWindowViewModel                        │
│    owns → ObservableCollection<WheelSliceViewModel>
│  WheelSliceViewModel                        │
│    wraps → WheelSlice (Model)               │
└────────┬────────────────────────────────────┘
         │ depends on (constructor injection)
┌────────▼────────────────────────────────────┐
│  Services                                   │
│  IFilePickerService / WindowFilePickerService│
│  LayoutService                              │
│  AudioService  (NAudio on Windows)          │
└─────────────────────────────────────────────┘

Controls/SpinnerWheelControl  ← custom Avalonia Control
  receives Slices + RotationAngle + WinnerIndex + UseWeightedSlices via StyledProperties
  calls InvalidateVisual() → Render(DrawingContext) every ~16 ms during spin

Models/WheelSlice + WheelLayout  ← plain C#, JSON-serialisable

Converters/HexColorToBrushConverter  ← IValueConverter for AXAML (one-way)
Converters/ColorToHexConverter       ← IValueConverter for AXAML (two-way, for ColorView)
```

Data always flows **down**: Models → ViewModels → View. The View never touches
Models directly. Services are created in `MainWindow.axaml.cs` (because
`WindowFilePickerService` needs the `Window` reference) and injected into the ViewModel
via its constructor.

---

## File-by-file reference

---

### `Program.cs`

**Entry point.** A sealed class with a single `[STAThread] Main` method that calls
`BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.

`BuildAvaloniaApp()` configures:
- `AppBuilder.Configure<App>()` — points to the `App` class
- `.UsePlatformDetect()` — selects Win32 / X11 / macOS backend automatically
- `.WithInterFont()` — registers the Inter font that Fluent theme uses
- `.LogToTrace()` — routes Avalonia's internal logs to `System.Diagnostics.Trace`

Do **not** add `.UseReactiveUI()` here — we use CommunityToolkit, not ReactiveUI.

---

### `App.axaml`

**Application-level resources and styles.** Three important things happen here:

1. **FluentTheme** is declared — this provides all default control styles (buttons,
   text boxes, list boxes, etc.). Without it the app renders unstyled.

2. **Global converters** are registered as application-level resources:
   ```xml
   <conv:HexColorToBrushConverter x:Key="HexColorToBrush"/>
   <conv:ColorToHexConverter      x:Key="ColorToHex"/>
   ```
   These make both converters available via `{StaticResource ...}` in every `.axaml` file.

3. **ColorPicker styles** are explicitly included:
   ```xml
   <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />
   ```
   Without this, `ColorView` renders as a grey rectangle.

4. **`ViewLocator`** is in `Application.DataTemplates` — leftover from the Avalonia MVVM
   template. Harmless; kept for potential future navigation use.

---

### `App.axaml.cs`

**Application bootstrap.** The `OnFrameworkInitializationCompleted` override:

1. Calls `DisableAvaloniaDataAnnotationValidation()` to prevent duplicate validation errors.
2. Creates `new MainWindow()` — does **not** set `DataContext`. `MainWindow`'s constructor
   builds its own ViewModel because `WindowFilePickerService(this)` needs `this` first.

---

### `ViewLocator.cs`

**Convention-based View resolver.** Implements `IDataTemplate`. `ViewLocator.Match`
returns `true` for any `ViewModelBase` subclass; `Build` swaps `"ViewModel"` → `"View"`
and activates the View type via reflection. Not actively used (single-window app) but
kept for future navigation.

---

### `Models/WheelSlice.cs`

**Pure data model — no Avalonia or UI dependencies.**

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Id` | `string` (GUID) | new GUID | Stable identity across save/load |
| `Label` | `string` | `"Slice"` | Text displayed inside the slice |
| `ImagePath` | `string?` | null | Absolute path to PNG/JPG, or null |
| `SoundPath` | `string?` | null | Absolute path to WAV/MP3, or null |
| `ColorHex` | `string` | `"#E74C3C"` | CSS hex colour, e.g. `"#E74C3C"` |
| `Weight` | `double` | `1.0` | Relative weight for proportional spinning |
| `IsActive` | `bool` | `true` | Whether the slice participates in the wheel |

Serialised by `LayoutService` using `System.Text.Json` with `camelCase` naming policy.
Asset paths are stored as absolute paths — they break if the user moves files.

---

### `Models/WheelLayout.cs`

**Root container for a saved wheel.** Serialised to a single `.json` file.

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Name` | `string` | `"My Wheel"` | Display name |
| `Slices` | `List<WheelSlice>` | empty | Ordered list of all slices |
| `SpinDurationSeconds` | `double` | `4.0` | Spin animation length (maps to "Speed" in UI) |

---

### `ViewModels/ViewModelBase.cs`

**Abstract base for all ViewModels.** Inherits `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`.
All ViewModel classes must inherit `ViewModelBase` so `ViewLocator.Match` can identify them.

---

### `ViewModels/WheelSliceViewModel.cs`

**Observable wrapper around `WheelSlice`.** A `partial class` required for CommunityToolkit
source generation.

**Generated properties** (from `[ObservableProperty]` fields):

| Field | Generated Property | Type | Notes |
|-------|--------------------|------|-------|
| `_label` | `Label` | `string` | Shown in the slice and the list |
| `_colorHex` | `ColorHex` | `string` | CSS hex, e.g. `"#3498DB"` |
| `_imagePath` | `ImagePath` | `string?` | Setting this triggers `OnImagePathChanged` |
| `_soundPath` | `SoundPath` | `string?` | Path to audio file |
| `_loadedBitmap` | `LoadedBitmap` | `Bitmap?` | Set internally by `OnImagePathChanged` |
| `_weight` | `Weight` | `double` | Relative weight; used when `UseWeightedSlices` is on |
| `_isActive` | `IsActive` | `bool` | User-toggled; tick = on wheel, untick = excluded |

**`IsActive` behaviour:**
- Purely user-controlled via the checkbox in the slice list (tick = active).
- Auto-set to `false` by `MainWindowViewModel` post-spin when `UseWeightedSlices` is ON
  and the winner's weight deducts to 0.
- Weight editing alone does **not** auto-change `IsActive` — that is intentional so that
  turning off `UseWeightedSlices` makes weights completely inert.

**`OnImagePathChanged`:**
Called automatically whenever `ImagePath` is set. Disposes previous bitmap, loads new one
from disk into `LoadedBitmap`. Logs on failure; does not throw.

**`ToModel()`:** Converts to plain `WheelSlice` for serialisation. `LoadedBitmap` is excluded.

**Constructors:**
- Parameterless — used when the user clicks "Add Slice".
- `WheelSliceViewModel(WheelSlice model)` — used when loading from JSON.

---

### `ViewModels/MainWindowViewModel.cs`

**The heart of the application.** A `partial class` inheriting `ViewModelBase`.

#### Observable state

| Property | Type | Purpose |
|----------|------|---------|
| `Slices` | `ObservableCollection<WheelSliceViewModel>` | Ordered list of slices on the wheel |
| `SelectedSlice` | `WheelSliceViewModel?` | Currently selected slice in the editor |
| `CurrentRotation` | `double` | Wheel rotation in degrees; drives `SpinnerWheelControl` |
| `WinnerIndex` | `int` | Index of winning slice (-1 = none); used for gold highlight |
| `WinnerMessage` | `string` | Banner text shown on SpinnerWindow after spin |
| `SpinDurationSeconds` | `decimal` | Labelled "Speed" in UI. Peak velocity = Speed × 180 °/s |
| `Friction` | `int` | 1–10. Free-spin friction rate. Decay = `0.20 + (Friction-1) × 0.28` /s |
| `ChromaKeyColor` | `string` | CSS hex for SpinnerWindow background (OBS chroma key). Default `#00FF00` |
| `GlobalWeight` | `double` | Default weight value for "Apply to All" |
| `UseWeightedSlices` | `bool` | When true: proportional slice sizing + post-spin deduction. Default `false` |

#### Slice weighting system

`UseWeightedSlices` is the master switch:

**When ON:**
- Slices render proportionally to their `Weight` value.
- Winner determination uses weighted cumulative angles.
- After each spin, the winner's `Weight` is deducted by 1 (min 0).
- If weight reaches 0, `IsActive` is set to `false` (auto-untick).
- `_weightSnapshot` is saved by `ApplyWeightToAll` for undo support; cleared after spin.

**When OFF:**
- All `IsActive=true` slices render at equal size regardless of weight.
- No weight is deducted after a spin.
- No slice is auto-deactivated.
- Weight values are stored and editable but completely ignored at runtime.

`_weightSnapshot` — `double[]?` saved when "Apply to All" is clicked. `UndoWeightCommand`
restores it; the command is disabled when no snapshot exists (`CanUndoWeight()`).

#### CanExecute cascade on `SelectedSlice`

`_selectedSlice` carries multiple CommunityToolkit attributes:
```csharp
[NotifyPropertyChangedFor(nameof(HasSelectedSlice))]
[NotifyCanExecuteChangedFor(nameof(RemoveSliceCommand))]
[NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
[NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
[NotifyCanExecuteChangedFor(nameof(AssignImageCommand))]
[NotifyCanExecuteChangedFor(nameof(RemoveImageCommand))]
[NotifyCanExecuteChangedFor(nameof(AssignSoundCommand))]
[NotifyCanExecuteChangedFor(nameof(RemoveSoundCommand))]
```

#### Commands

| Source method | Generated command | CanExecute | Notes |
|---------------|-------------------|------------|-------|
| `SpinWheelAsync()` | `SpinWheelCommand` | `Slices.Count >= 1` | AsyncRelayCommand; stays disabled for entire animation |
| `ResetWheel()` | `ResetWheelCommand` | always | Stops animation, resets rotation and winner |
| `AddSlice()` | `AddSliceCommand` | always | Picks next palette colour; auto-selects new slice |
| `RemoveSlice()` | `RemoveSliceCommand` | `HasSelection()` | Selects neighbour after removal |
| `MoveUp()` | `MoveUpCommand` | `CanMoveUp()` | `Slices.Move(idx, idx-1)` |
| `MoveDown()` | `MoveDownCommand` | `CanMoveDown()` | `Slices.Move(idx, idx+1)` |
| `AssignImageAsync()` | `AssignImageCommand` | `HasSelection()` | Opens image file picker |
| `RemoveImage()` | `RemoveImageCommand` | `HasSelectionImagePath()` | Nulls `ImagePath` |
| `AssignSoundAsync()` | `AssignSoundCommand` | `HasSelection()` | Opens audio file picker |
| `RemoveSound()` | `RemoveSoundCommand` | `HasSelectionSoundPath()` | Nulls `SoundPath` |
| `SaveLayoutAsync()` | `SaveLayoutCommand` | always | Opens save dialog, calls `LayoutService.SaveAsync` |
| `LoadLayoutAsync()` | `LoadLayoutCommand` | always | Opens load dialog, rebuilds `Slices` from model |
| `ApplyWeightToAll()` | `ApplyWeightToAllCommand` | always | Saves snapshot, applies `GlobalWeight` to all slices |
| `UndoWeight()` | `UndoWeightCommand` | `CanUndoWeight()` | Restores `_weightSnapshot`; disabled when no snapshot |
| `RandomiseStartAngle()` | `RandomiseStartAngleCommand` | always | Immediately sets `CurrentRotation` to a random 0–360° angle |
| `RandomiseSliceOrder()` | `RandomiseSliceOrderCommand` | always | Immediately Fisher-Yates shuffles `Slices`; preserves `SelectedSlice` |

**CommunityToolkit naming rules:**
- Method `SpinWheelAsync` → command `SpinWheelCommand` (strips `Async` suffix, appends `Command`)
- Method `AddSlice` → command `AddSliceCommand`

#### Spin animation deep dive — physics model

The spin is **velocity-based physics** rather than a fixed-target easing curve.
`SpinWheelAsync` sets up phase boundaries and starts a `DispatcherTimer`; each tick
advances `CurrentRotation` by `_currentVelocity × dt`. The winner is read from
`CurrentRotation` when the wheel physically stops — it is **not** pre-determined.

`SpinWheelAsync` flow:
1. Sets physics parameters from `SpinDurationSeconds` (Speed) and `Friction`.
2. Creates `_spinTcs` and starts the `DispatcherTimer` (~60 fps).
3. `await _spinTcs.Task` — SPIN button stays disabled for the entire physics simulation.
4. After await: filters active slices, calculates winner using weighted or equal angles,
   sets `WinnerIndex` / `WinnerMessage`, plays winner sound, optionally deducts weight.

#### Five animation phases

All time boundaries are in seconds from `_animStart`. `totalDuration = SpinDurationSeconds`.

| Phase | Time window | Velocity behaviour |
|-------|-------------|-------------------|
| 1 Wind-up | `0 → _windUpDuration` (random 2–5 % of total) | Ease-in **backwards** to `−60 °/s` |
| 2 Acceleration | `_windUpDuration → _accelEndTime` (10 % of total) | `Lerp(−windUpSpeed, peakVelocity, t²)` |
| 3 Cruise | `_accelEndTime → _fullSpeedEndTime` (10–80 %) | Constant `_peakVelocity` |
| 4 Engine off | `_fullSpeedEndTime → _halfSpeedEndTime` (80–100 %) | Linear drop to `peakVelocity / 2` |
| 5 Free spin | `> _halfSpeedEndTime` until stop | Exponential friction decay; stops when velocity < 0.5 °/s |

`_peakVelocity = SpinDurationSeconds × 180 °/s`

Friction decay per tick: `velocity *= (1 − frictionRate × dt)` where
`frictionRate = 0.20 + (Friction − 1) × 0.28`.

`_spinCancelled` flag: if `ResetWheel()` is called mid-spin it sets this flag. The
`SpinWheelAsync` continuation returns early without setting a winner.

#### Coordinate system and winner math

Slices are drawn starting at **−90° (12 o'clock)** going **clockwise**. The pointer is
fixed at the top. The canvas is rotated clockwise by `CurrentRotation` degrees.

After the wheel stops at angle R:
```
pointerAngle = ((360 − R mod 360) mod 360 + 360) mod 360
```

Winner is found by walking cumulative slice arcs:
```csharp
// Weighted mode: arc proportional to weight
// Unweighted mode: equal arcs (weight=1.0 each)
double cumDeg = 0;
for each activeSlice:
    cumDeg += (w / totalWeight) * 360
    if pointerAngle < cumDeg → this slice wins
```

`WinnerIndex` is the index in the full `Slices` collection (not the filtered active list),
so the gold highlight in `SpinnerWheelControl` still works correctly.

Active slice filter:
- `UseWeightedSlices=ON`: `s.IsActive && s.Weight > 0`
- `UseWeightedSlices=OFF`: `s.IsActive` only

#### Helpers

- `Lerp(a, b, t)` — linear interpolation, clamped 0–1.
- `ShuffleSlices(Random rng)` — Fisher-Yates in-place shuffle of `Slices`. Saves and restores `SelectedSlice` (the reference stays valid; clearing + re-adding doesn't break it). Used by `RandomiseSliceOrderCommand`.
- `NotifyMoveCanExecuteChanged()` — raises CanExecute for both `MoveUpCommand` and `MoveDownCommand` together.

#### Palette colours

When a new slice is added: `PaletteColors[Slices.Count % PaletteColors.Length]`.
12 colours in the array; cycle repeats every 12 slices.

---

### `Views/MainWindow.axaml` — Settings window

**The streamer-facing control panel.** Has `x:DataType="vm:MainWindowViewModel"`.

#### Layout structure

```
Window (760×640, title "SpinnerWheel — Settings")
└── Grid (2 columns: 320px fixed, *)
    ├── [Col 0] ScrollViewer > StackPanel  — Controls
    │     SpinnerWheel header
    │     Grid: [SPIN! button | ↺ Reset button]
    │     Grid: Speed (1–30) | Friction (1–10) — aligned two-column grid
    │     StackPanel: "🔀 Randomise start" + "🔀 Randomise order" instant-action buttons
    │     Chroma Key: TextBox + colour swatch flyout (ColorView)
    │     Weights section:
    │       "Weights" header + "Use weighted slices" CheckBox
    │       Default NumericUpDown + "Apply to All" + "Undo" buttons
    │     Save / Load Layout buttons
    └── [Col 1] Border  — Slice editor
          Grid (3 rows: Auto, *, Auto)
          ├── Toolbar: + − ▲ ▼ buttons
          ├── ListBox: slices (DataTemplate x:DataType=WheelSliceViewModel)
          │     Each row: CheckBox(IsActive) + colour swatch + Label + "(inactive)" hint
          └── ScrollViewer: properties editor (IsVisible=HasSelectedSlice)
                Label TextBox
                Colour TextBox + swatch flyout
                Weight NumericUpDown (0–100)
                ── Separator ──
                Image browse + remove
                Sound browse + remove
```

#### Slice list checkbox

Each slice in the `ListBox` has a `CheckBox` bound to `IsActive` (two-way). Ticked =
slice participates in the wheel. Unticked = excluded. A grey `(inactive)` hint text
appears alongside any slice where `!IsActive`. The checkbox in the list is the single
control for per-slice visibility — there is no separate "hidden" checkbox.

#### Colour pickers

Both the **Chroma Key** colour and each **Slice colour** have a swatch button that opens
a `Flyout` containing a `ColorView` (`Avalonia.Controls.ColorPicker`).
`IsAlphaVisible="False"` hides the alpha channel. `ColorView.Color` is bound two-way
via `{StaticResource ColorToHex}`.

---

### `Views/MainWindow.axaml.cs` — Settings window code-behind

Constructs all services and the shared ViewModel, then creates and shows `SpinnerWindow`:

```csharp
public MainWindow()
{
    InitializeComponent();
    var pickerService = new WindowFilePickerService(this);
    var layoutService = new LayoutService();
    var audioService  = new AudioService();
    var vm = new MainWindowViewModel(pickerService, layoutService, audioService);
    DataContext = vm;

    _spinnerWindow = new SpinnerWindow(vm);
    _spinnerWindow.Show();

    Closing               += (_, _) => _spinnerWindow.Close();
    _spinnerWindow.Closed += (_, _) => Close();

    // Mutual z-raise: clicking either window brings both to front.
    Activated                += (_, _) => BringToFrontNoActivate(_spinnerWindow);
    _spinnerWindow.Activated += (_, _) => BringToFrontNoActivate(this);

    Opened += (_, _) => PositionWindowsSideBySide();
}
```

**`BringToFrontNoActivate(Window)`** — uses Win32 P/Invoke `SetWindowPos` with
`SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE` to raise the other window to the top of the
Z-order **without stealing focus**. This is critical: the old `Activate()` approach stole
focus mid-click (between `PointerPressed` and `PointerReleased`), causing controls to
require multiple clicks. `SWP_NOACTIVATE` avoids the focus transfer entirely.
No guard flag is needed because `SetWindowPos(SWP_NOACTIVATE)` does not trigger the
`Activated` event, so there is no ping-pong risk.

---

### `Views/SpinnerWindow.axaml` — OBS capture window

**The wheel-only display window.** Has `x:DataType="vm:MainWindowViewModel"`.
`Window.Background` is live-bound to `ChromaKeyColor`.

```
Window (620×660, title "SpinnerWheel — OBS Capture", resizable)
Background = ChromaKeyColor (live)
└── Grid (children overlap / Z-stacked)
    ├── Viewbox (Stretch=Uniform)
    │   └── SpinnerWheelControl (600×600)
    │         Slices, RotationAngle, WinnerIndex, UseWeightedSlices bound from VM
    └── Border (VerticalAlignment=Center, HorizontalAlignment=Center, IsVisible when WinnerMessage non-empty)
            Background = #CC000000 (dark semi-transparent pill, CornerRadius=8, Padding=24,10)
            └── TextBlock: WinnerMessage (30px bold white)
```

The winner banner background is `#CC000000` (not the chromakey colour) so OBS does not
key it out — the text must remain readable after keying.

---

### `Views/SpinnerWindow.axaml.cs` — OBS capture window code-behind

Two constructors (chained). Parameterless required by Avalonia XAML loader; runtime
constructor accepts `MainWindowViewModel` and sets `DataContext`.

---

### `Controls/SpinnerWheelControl.cs`

**Custom Avalonia `Control` subclass.** Renders entirely by overriding
`Render(DrawingContext)` — no AXAML template.

#### Styled properties

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Slices` | `ObservableCollection<WheelSliceViewModel>?` | null | Slice data to render |
| `RotationAngle` | `double` | 0.0 | Clockwise rotation in degrees |
| `WinnerIndex` | `int` | -1 | Which slice to highlight gold; -1 = none |
| `UseWeightedSlices` | `bool` | true | When true: proportional arcs; when false: equal arcs |

`RotationAngleProperty`, `WinnerIndexProperty`, and `UseWeightedSlicesProperty` are all
registered with `AffectsRender<>` — changing them automatically triggers a redraw.

`SlicesProperty` uses manual `CollectionChanged` + per-item `PropertyChanged` subscriptions
(all calling `InvalidateVisual()`) because the collection reference rarely changes.

#### Active slice filter in DrawSlices

```csharp
// Only IsActive slices are drawn. Zero-weight slices are NOT excluded from rendering —
// they stay visible until the start of the next spin so the user can see what won.
foreach (var s in slices)
    if (s.IsActive) active.Add(s);
```

When `active` is empty, `DrawEmptyWheel` is shown.

#### Weighted arc calculation

Weight is floored at 1.0 so a weight-0 slice still has a visible arc:

```csharp
double totalWeight = useWeightedSlices
    ? active.Sum(s => Math.Max(1.0, s.Weight))
    : active.Count;
double w       = useWeightedSlices ? Math.Max(1.0, slice.Weight) : 1.0;
var sliceRad   = (w / totalWeight) * 2 * Math.PI;
```

`startAngle` is accumulated across the loop (not recalculated from index × sliceRad),
which is required for weighted slices where arcs have different sizes.

#### Winner highlight

`WinnerIndex` is an index into the **full** `Slices` collection. The control finds the
active slice's position using `slices.IndexOf(slice) == WinnerIndex` so the gold border
correctly follows the slice even after filtering.

#### Rendering pipeline

`Render()`:
1. Calculates `center` and `radius` from `Bounds`.
2. Builds rotation matrix: `Translate(-cx,-cy) × Rotate(rad) × Translate(cx,cy)`.
3. Inside `PushTransform`: draws slices (rotate with wheel).
4. Outside transform: draws outer ring, pointer, centre pin (fixed in screen space).

#### DrawPieSlice

Handles single-slice (full circle) and general case (pie sector via `StreamGeometry`
with `ArcTo`, `isLargeArc = true` when arc > 180°).

#### DrawSliceImage / DrawSliceLabel

- **Image**: at 72% radius when a label also exists (pushed toward rim); 50% radius when alone. Size capped at `min(radius × 0.28, 52px)`.
- **Label**: at 50% radius (centre of slice); pulled to 32% when an image is also present (pushed inward). Font: 14px (≤4 slices), 12px (5–8), 10px (>8). Truncated at 16 chars.

#### Fixed decorations

- `DrawOuterRing` — `#444444` circle, 3px, drawn over slices to clean up jagged edges.
- `DrawPointer` — red (`#E74C3C`) downward triangle at 12 o'clock, tip on the rim.
- `DrawCenterPin` — white disc (r=14) + red dot (r=7) at centre.

---

### `Services/IFilePickerService.cs`

Dependency-inversion interface. Returns `string?` paths; null on cancel.

```csharp
Task<string?> OpenImageFileAsync();
Task<string?> OpenSoundFileAsync();
Task<string?> OpenLayoutFileAsync();
Task<string?> SaveLayoutFileAsync(string defaultName);
```

---

### `Services/WindowFilePickerService.cs`

Concrete implementation using Avalonia's `IStorageProvider` API. Uses
`TopLevel.GetTopLevel(_window)?.StorageProvider`. Returns paths via `TryGetLocalPath()`.

---

### `Services/LayoutService.cs`

JSON save/load for `WheelLayout` using `System.Text.Json` with `camelCase` naming policy.
Both methods are `async` (non-blocking disk I/O). `LoadAsync` returns `null` on failure.

---

### `Services/AudioService.cs`

**Audio playback service.** Implements `IDisposable`.

| Platform | Implementation | Notes |
|----------|---------------|-------|
| Windows (WAV + MP3) | NAudio `WaveOutEvent` + `AudioFileReader` | In-process, no window, can be stopped |
| macOS | `afplay` process | Handles both WAV and MP3 natively |
| Linux (WAV) | `aplay` process | Standard ALSA tool |
| Linux (MP3) | `mpg123` process | Must be installed separately |

**Windows implementation detail:**
`AudioFileReader` detects format from file extension and uses Windows' built-in
ACM/Media Foundation codecs — the same codecs used by Windows Media Player, but entirely
in-process. `WaveOutEvent.PlaybackStopped` event auto-disposes the reader when playback
ends naturally. `StopCurrent()` calls `_waveOut.Stop()` + `Dispose()` before starting a
new sound, preventing overlapping playback.

The old Windows MP3 approach (`cmd /c start /b`) launched the system's default media
player as a visible external process and could not be stopped cleanly. NAudio replaces
both WAV and MP3 paths on Windows.

---

### `Converters/HexColorToBrushConverter.cs`

**One-way** `IValueConverter`. Parses CSS hex → `SolidColorBrush`. Fallback: grey.
Registered in `App.axaml` as `"HexColorToBrush"`.

---

### `Converters/ColorToHexConverter.cs`

**Two-way** `IValueConverter` between CSS hex `string` and `Avalonia.Media.Color`.
Used to bind `ColorView.Color` (in colour picker flyouts) to ViewModel string properties.

- `Convert` (string → Color): parses hex, forces alpha=255.
- `ConvertBack` (Color → string): formats as `#RRGGBB`, discarding alpha.
- Fallback in both directions: `#00B140`.
- Registered in `App.axaml` as `"ColorToHex"`.

---

## How to make common changes

### Add a new property to a slice

1. Add the property to `Models/WheelSlice.cs`.
2. Add a corresponding `[ObservableProperty]` field to `ViewModels/WheelSliceViewModel.cs`.
3. Add it to `WheelSliceViewModel.ToModel()` so saves include it.
4. Add it to the `WheelSliceViewModel(WheelSlice model)` constructor so loads restore it.
5. If it should appear in the editor UI, add a binding row to the properties section in
   `Views/MainWindow.axaml` inside the `ScrollViewer`.

### Add a new command to the ViewModel

1. Write a private method (sync `void` or `async Task`).
2. Decorate it with `[RelayCommand]` (optionally add `CanExecute = nameof(SomeMethod)`).
3. Generated command name: method name with `Async` stripped, `Command` appended.
4. Bind in AXAML: `Command="{Binding MyNewCommand}"`.
5. If enabled state depends on `SelectedSlice`, add
   `[NotifyCanExecuteChangedFor(nameof(MyNewCommand))]` to the `_selectedSlice` field.

### Change the spin animation feel

Key tuning points in `MainWindowViewModel.cs`:

- **Wind-up speed** — `_windUpSpeed = 60.0` °/s.
- **Phase boundaries** — `_accelEndTime = 0.10`, `_fullSpeedEndTime = 0.80` (fractions of total).
- **Acceleration curve** — Phase 2 uses `Lerp(−windUpSpeed, peakVelocity, t²)`. Change `t²` to
  `t³` for a slower build-up.
- **Friction formula** — `frictionRate = 0.20 + (Friction − 1) × 0.28`.
- **Stop threshold** — `_currentVelocity < 0.5 °/s`.

### Change how slices are drawn

All drawing code is in `Controls/SpinnerWheelControl.cs`. Key entry points:
- `DrawSlices` — outer loop; weighted/unweighted arc calculation
- `DrawPieSlice` — geometry of each sector
- `DrawSliceLabel` — text positioning and font
- `DrawSliceImage` — image positioning and size
- `DrawPointer` — the fixed triangle indicator

### Add a global UI style

Add `<Style Selector="...">` inside `<Application.Styles>` in `App.axaml`, after `<FluentTheme />`.

### Change the default palette

Edit `PaletteColors` in `MainWindowViewModel.cs` — `static readonly string[]` of CSS hex strings.

---

## What NOT to do

- **Do not use `WrapPanel` with a spacing property** — Avalonia's `WrapPanel` has no `Spacing`.
  Use `StackPanel Orientation="Horizontal"` with `Spacing` instead.
- **Do not add `x:CompileBindings="False"`** without good reason — compiled bindings are
  project-wide and the type-safety catches errors early.
- **Do not reference `_fieldName` directly in the ViewModel body** when an `[ObservableProperty]`
  field has that name — use the generated PascalCase property (MVVMTK0034 warning).
- **Do not add ReactiveUI** — the project deliberately uses CommunityToolkit.Mvvm only.
- **Do not embed binary assets in the JSON layout** — `WheelLayout` stores file paths only.
- **Do not call `InvalidateVisual()` from a background thread** — always call from UI thread.
- **Do not use `Activate()` for mutual window raising** — use `SetWindowPos(SWP_NOACTIVATE)`
  via P/Invoke. `Activate()` steals focus mid-click and causes controls to need multiple clicks.
- **Do not auto-deactivate slices from `OnWeightChanged`** — weight changes are intentionally
  inert. Only post-spin logic (guarded by `UseWeightedSlices`) should change `IsActive`.
