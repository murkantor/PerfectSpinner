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

### NuGet packages in use

| Package | Version | Purpose |
|---------|---------|---------|
| `Avalonia` | 11.3.11 | Core UI framework |
| `Avalonia.Desktop` | 11.3.11 | Desktop platform support (Win/Linux/Mac) |
| `Avalonia.Themes.Fluent` | 11.3.11 | Microsoft Fluent Design theme |
| `Avalonia.Fonts.Inter` | 11.3.11 | Inter font (used by `.WithInterFont()`) |
| `Avalonia.Diagnostics` | 11.3.11 | Dev-tools overlay (Debug builds only) |
| `CommunityToolkit.Mvvm` | 8.2.1 | Source-generated MVVM boilerplate |

No additional packages. Do **not** add ReactiveUI — the project deliberately uses
CommunityToolkit only.

---

## Two-window architecture (OBS streaming design)

The app runs as **two separate windows** that share one `MainWindowViewModel`:

```
┌──────────────────────────────┐     ┌──────────────────────────────────┐
│  MainWindow  (Settings)      │     │  SpinnerWindow  (OBS Capture)    │
│  Title: "SpinnerWheel —      │     │  Title: "SpinnerWheel —          │
│          Settings"           │     │          OBS Capture"            │
│  Contains:                   │     │  Contains:                       │
│  • SPIN button + duration    │     │  • SpinnerWheelControl (full)    │
│  • Last winner display       │     │  • Winner banner overlay         │
│  • Chroma key colour picker  │     │  • Background = ChromaKeyColor   │
│  • Show Spinner Window btn   │     │                                  │
│  • Save / Load layout        │     │  In OBS: Window Capture source   │
│  • Full slice editor         │     │  + Chroma Key filter             │
└──────────────┬───────────────┘     └──────────────────────────────────┘
               │ creates + owns                   ▲
               │ DataContext = vm                 │ DataContext = same vm
               └──────────────────────────────────┘
```

**Why this works for streaming:**
- `SpinnerWindow.Background` is bound to `ChromaKeyColor` (default `#00B140` broadcast green).
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
- The "Show Spinner Window" button in Settings calls `Activate()` (bring to front) if the
  window is already visible, or `Show()` if it has been minimised.

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
│  AudioService                               │
└─────────────────────────────────────────────┘

Controls/SpinnerWheelControl  ← custom Avalonia Control
  receives Slices + RotationAngle + WinnerIndex via StyledProperties
  calls InvalidateVisual() → Render(DrawingContext) every ~16 ms during spin

Models/WheelSlice + WheelLayout  ← plain C#, JSON-serialisable

Converters/HexColorToBrushConverter  ← IValueConverter for AXAML
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

**Application-level resources and styles.** Two important things happen here:

1. **FluentTheme** is declared — this provides all default control styles (buttons,
   text boxes, list boxes, etc.). Without it the app renders unstyled.

2. **`HexColorToBrushConverter`** is registered as an application-level resource:
   ```xml
   <conv:HexColorToBrushConverter x:Key="HexColorToBrush"/>
   ```
   This makes `{StaticResource HexColorToBrush}` available in every `.axaml` file in
   the project without redeclaring it. If you add more global converters, register them
   here.

3. **`ViewLocator`** is in `Application.DataTemplates`. It is a leftover from the
   Avalonia MVVM template and is harmless. It automatically finds a View class for any
   ViewModel (by replacing "ViewModel" with "View" in the type name). This project does
   not currently use it for navigation but it should stay in case it is needed later.

---

### `App.axaml.cs`

**Application bootstrap.** The `OnFrameworkInitializationCompleted` override:

1. Calls `DisableAvaloniaDataAnnotationValidation()` to prevent duplicate validation
   errors that would occur because both Avalonia and CommunityToolkit.Mvvm validate
   `[Required]` / `[Range]` etc. annotations. Without this you get double error messages.

2. Creates `new MainWindow()` — note it does **not** set `DataContext`. The reason is
   that `MainWindow`'s constructor must create `WindowFilePickerService(this)` before
   the ViewModel can be constructed. Letting `MainWindow` build its own ViewModel keeps
   the construction order correct.

---

### `ViewLocator.cs`

**Convention-based View resolver.** Implements `IDataTemplate` (registered in
`App.axaml`). When Avalonia encounters a ViewModel as a content object, it asks
registered `IDataTemplate`s whether they can handle it. `ViewLocator.Match` returns
`true` for any `ViewModelBase` subclass, then `Build` swaps `"ViewModel"` → `"View"`
in the full type name and activates the View type via reflection.

**Current usage:** This is not actively used by the app (there is only one window)
but is kept because it is part of the standard Avalonia MVVM template and enables
future navigation scenarios without extra wiring.

---

### `Models/WheelSlice.cs`

**Pure data model — no Avalonia or UI dependencies.**

Properties:
| Property | Type | Purpose |
|----------|------|---------|
| `Id` | `string` (GUID) | Stable identity across save/load |
| `Label` | `string` | Text displayed inside the slice |
| `ImagePath` | `string?` | Absolute filesystem path to PNG/JPG, or null |
| `SoundPath` | `string?` | Absolute filesystem path to WAV/MP3, or null |
| `ColorHex` | `string` | CSS hex colour string, e.g. `"#E74C3C"` |
| `Weight` | `double` | Reserved for weighted-random spinning (always 1.0 now) |

This class is serialised/deserialised verbatim by `LayoutService` using
`System.Text.Json` with `camelCase` naming policy. The JSON on disk uses camelCase keys
(`"imagePath"`, not `"ImagePath"`).

**Important:** asset paths are stored as-is (absolute). If the user moves images or the
layout file to a different machine, paths will break. There is no asset-embedding or
relative-path resolution.

---

### `Models/WheelLayout.cs`

**Root container for a saved wheel.** Serialised to a single `.json` file.

Properties:
| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Name` | `string` | `"My Wheel"` | Display name (not currently shown in UI) |
| `Slices` | `List<WheelSlice>` | empty | Ordered list of all slices |
| `SpinDurationSeconds` | `double` | `4.0` | Spin animation length in seconds |

The slice order in the list is the slice order on the wheel (slice 0 starts at 12 o'clock).

---

### `ViewModels/ViewModelBase.cs`

**Abstract base for all ViewModels.** Inherits from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`.

`ObservableObject` provides:
- `INotifyPropertyChanged` and `INotifyPropertyChanging` implementations
- `SetProperty<T>(ref T field, T value)` for change-raising setters
- Support for CommunityToolkit's source-generation attributes (`[ObservableProperty]`, etc.)

All ViewModel classes in this project must inherit `ViewModelBase` (not `ObservableObject`
directly) so that `ViewLocator.Match` can identify them.

---

### `ViewModels/WheelSliceViewModel.cs`

**Observable wrapper around `WheelSlice`.** This is what the UI and the wheel control
actually bind to. It is a `partial class` — required for CommunityToolkit source generation.

**Generated properties** (from `[ObservableProperty]` fields):
| Field | Generated Property | Type | Notes |
|-------|--------------------|------|-------|
| `_label` | `Label` | `string` | Shown in the slice and the list |
| `_colorHex` | `ColorHex` | `string` | CSS hex, e.g. `"#3498DB"` |
| `_imagePath` | `ImagePath` | `string?` | Setting this triggers `OnImagePathChanged` |
| `_soundPath` | `SoundPath` | `string?` | Path to audio file |
| `_loadedBitmap` | `LoadedBitmap` | `Bitmap?` | Set internally; never bound in AXAML directly |

**Key behaviour — `OnImagePathChanged`:**
This `partial void` method is called automatically by the CommunityToolkit source
generator whenever `ImagePath` is set. It:
1. Calls `Dispose()` on any previously loaded `Bitmap` to release memory / file handle.
2. Returns early if the new path is null, empty, or the file does not exist on disk.
3. Tries to construct `new Bitmap(value)` (Avalonia bitmap from file path).
4. Logs to `Debug.WriteLine` on failure (does not throw).

This means the wheel control always has a ready-to-draw `LoadedBitmap` without any
async image loading infrastructure.

**`ToModel()`:**
Converts back to a plain `WheelSlice` for serialisation. Call this when saving a layout.
The `LoadedBitmap` field is intentionally not included — it is transient runtime state.

**Constructors:**
- Parameterless `WheelSliceViewModel()` — used when the user clicks "Add Slice".
- `WheelSliceViewModel(WheelSlice model)` — used when loading a layout from JSON. Sets
  fields directly (bypassing source-generated setters for `_label`, `_colorHex`,
  `_soundPath` to avoid redundant change notifications) but uses the **property setter**
  for `ImagePath` so the bitmap loads immediately.

---

### `ViewModels/MainWindowViewModel.cs`

**The heart of the application.** A `partial class` that inherits `ViewModelBase`.
All application logic lives here; the view has no logic.

#### Observable state

| Property | Type | Purpose |
|----------|------|---------|
| `Slices` | `ObservableCollection<WheelSliceViewModel>` | The ordered list of slices shown on the wheel |
| `SelectedSlice` | `WheelSliceViewModel?` | Currently selected slice in the editor panel |
| `CurrentRotation` | `double` | Current wheel rotation in degrees. Drives the `SpinnerWheelControl` |
| `WinnerIndex` | `int` | Index of the winning slice (-1 = no winner yet). Used by the control to highlight the winner |
| `WinnerMessage` | `string` | Banner text shown on the SpinnerWindow after spinning (not shown in Settings) |
| `SpinDurationSeconds` | `decimal` | Bound to the Duration `NumericUpDown` in Settings |
| `Inertia` | `int` | 1–10. Controls extra full rotations: `level × 2` min + `rng.Next(level+1)` random bonus. Level 1 = 2–3 spins; level 10 = 20–30 spins |

`HasSelectedSlice` is a plain computed property (not `[ObservableProperty]`) that returns
`SelectedSlice != null`. It is refreshed by `[NotifyPropertyChangedFor(nameof(HasSelectedSlice))]`
on `_selectedSlice`.

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
Whenever `SelectedSlice` changes, all of the above commands automatically re-evaluate
their `CanExecute` state. This is what enables/disables the editor buttons without any
manual wiring.

#### Commands

| Source method | Generated command | CanExecute | Notes |
|---------------|-------------------|------------|-------|
| `SpinWheelAsync()` | `SpinWheelCommand` | `CanSpinWheel()` — `Slices.Count >= 1` | `AsyncRelayCommand`; button stays disabled for the entire animation because the task does not complete until the `DispatcherTimer` fires `_spinTcs.SetResult()` |
| `AddSlice()` | `AddSliceCommand` | always enabled | Picks next palette colour; auto-selects new slice |
| `RemoveSlice()` | `RemoveSliceCommand` | `HasSelection()` | Selects neighbour after removal |
| `MoveUp()` | `MoveUpCommand` | `CanMoveUp()` | Calls `Slices.Move(idx, idx-1)` |
| `MoveDown()` | `MoveDownCommand` | `CanMoveDown()` | Calls `Slices.Move(idx, idx+1)` |
| `AssignImageAsync()` | `AssignImageCommand` | `HasSelection()` | Opens image file picker; sets `SelectedSlice.ImagePath` |
| `RemoveImage()` | `RemoveImageCommand` | `HasSelectionImagePath()` | Nulls `SelectedSlice.ImagePath` |
| `AssignSoundAsync()` | `AssignSoundCommand` | `HasSelection()` | Opens audio file picker |
| `RemoveSound()` | `RemoveSoundCommand` | `HasSelectionSoundPath()` | Nulls `SelectedSlice.SoundPath` |
| `SaveLayoutAsync()` | `SaveLayoutCommand` | always enabled | Opens save dialog, calls `LayoutService.SaveAsync` |
| `LoadLayoutAsync()` | `LoadLayoutCommand` | always enabled | Opens load dialog, rebuilds `Slices` from model |
| `ResetWheel()` | `ResetWheelCommand` | always enabled | Sets `CurrentRotation = 0`, `WinnerIndex = -1`, clears `WinnerMessage` |

**CommunityToolkit naming rules:**
- Method `SpinWheelAsync` → command `SpinWheelCommand` (strips `Async` suffix, appends `Command`)
- Method `AddSlice` → command `AddSliceCommand`
- Method `AssignImageAsync` → command `AssignImageCommand`

#### Spin animation deep dive

The spin runs on the **UI thread** via `DispatcherTimer` at `DispatcherPriority.Render`.
The `SpinWheelAsync` method is an `async Task` that:

1. Picks a random winner index.
2. Calculates `_animTargetAngle` using the rotation math (see Coordinate System section below).
3. Creates a `TaskCompletionSource _spinTcs` and starts the timer.
4. `await _spinTcs.Task` — suspends `SpinWheelAsync` here. Because it is an `AsyncRelayCommand`,
   the SPIN button stays disabled until the task resumes.
5. Each timer tick calls `OnAnimationTick`, which advances `CurrentRotation` along a
   **two-phase easing curve** (see below) where `t` is normalised elapsed time [0, 1].
6. When `t >= 1.0`, the timer stops and calls `_spinTcs.TrySetResult()`, which unblocks step 4.
7. After the await resumes: sets `WinnerIndex`, `WinnerMessage`, and plays the winner's sound.

#### Coordinate system and winner math

Slices are drawn starting at **-90° (12 o'clock)** going **clockwise**. The pointer is
fixed at the top (screen -90°). The canvas is rotated clockwise by `CurrentRotation` degrees.

After a spin of R degrees, the pointer sees the canvas angle:
```
pointerAngle = (360 - R mod 360) mod 360
```

Slice `i` occupies canvas angles `[i × sliceDeg, (i+1) × sliceDeg)`.

To make the wheel stop so that the **centre** of winner slice `w` is at the pointer:
```
targetMod = (360 - (w + 0.5) × sliceDeg) mod 360
```

A random wobble of ±30% of one slice is added for visual variety. The number of extra
full rotations is driven by the `Inertia` property: `level × 2 + rng.Next(level + 1)`
so higher inertia = more spins, with randomisation preventing the wheel from always
landing in the same spot.

#### Two-phase animation easing

The easing curve in `OnAnimationTick` is split at `t = 0.67`:

| Phase | Time span | Rotation covered | Curve |
|-------|-----------|-----------------|-------|
| 1 | t ∈ [0.00, 0.67] | 82% of total | Quadratic ease-in-out — accelerates then cruises |
| 2 | t ∈ [0.67, 1.00] | 18% of total | Quintic ease-out `1-(1-t₂)⁵` — steep dramatic slowdown |

The dramatic quintic brake at the end gives the "will it land here?" tension that is
especially visible at long durations and high inertia settings.

#### Palette colours

When a new slice is added, its colour is chosen by:
```csharp
PaletteColors[Slices.Count % PaletteColors.Length]
```
There are 12 colours in the array, so the cycle repeats every 12 slices. To add or change
colours, edit the `PaletteColors` array in this file.

---

### `Views/MainWindow.axaml` — Settings window

**The streamer-facing control panel.** Has `x:DataType="vm:MainWindowViewModel"`.
Does **not** contain a wheel — the wheel lives in `SpinnerWindow`.

#### Layout structure

```
Window (760×640, title "SpinnerWheel — Settings")
└── Grid (2 columns: 320px fixed, *)
    ├── [Col 0] ScrollViewer > StackPanel  — Controls
    │     SpinnerWheel header
    │     Grid: [SPIN! button | ↺ Reset button]
    │     Grid: Duration (s) label | NumericUpDown (stretches to fill)
    │           Inertia (1–10) label | NumericUpDown (stretches to fill)
    │     Chroma Key: TextBox + live swatch Border
    │     "Show Spinner Window" button  (Click → code-behind handler)
    │     Save / Load Layout buttons
    └── [Col 1] Border  — Slice editor
          Grid (3 rows: Auto, *, Auto)
          ├── Toolbar: + − ▲ ▼ buttons
          ├── ListBox: slices (DataTemplate x:DataType=WheelSliceViewModel)
          └── ScrollViewer: properties editor (IsVisible=HasSelectedSlice)
```

Note: the "Last Winner" display was removed from the Settings window. The winner banner
is only shown on the `SpinnerWindow` (the OBS capture window).

#### Key bindings unique to this window

`ChromaKeyColor` — bound to the hex TextBox (two-way) and to the swatch `Border.Background`
via `HexColorToBrush`. The swatch updates live as the user types.

`Click="OnShowSpinnerWindowClicked"` — this is a **code-behind event handler**, not a
command binding. Showing/hiding a secondary window is a pure view concern; the ViewModel
knows nothing about `SpinnerWindow`.

---

### `Views/MainWindow.axaml.cs` — Settings window code-behind

Constructs all services and the shared ViewModel, then creates and shows `SpinnerWindow`:

```csharp
public MainWindow()
{
    InitializeComponent();
    var pickerService = new WindowFilePickerService(this);   // needs 'this' for StorageProvider
    var layoutService = new LayoutService();
    var audioService  = new AudioService();
    var vm = new MainWindowViewModel(pickerService, layoutService, audioService);
    DataContext = vm;

    _spinnerWindow = new SpinnerWindow(vm);   // share the same ViewModel instance
    _spinnerWindow.Show();

    // Bidirectional close: closing either window closes both.
    Closing               += (_, _) => _spinnerWindow.Close();
    _spinnerWindow.Closed += (_, _) => Close();   // Closed (past) avoids re-entrancy

    // Position side by side on primary display once the window is live.
    Opened += (_, _) => PositionWindowsSideBySide();
}
```

`PositionWindowsSideBySide()` — called from `Opened`. Reads `Screens.Primary.WorkingArea`
(physical pixels), converts logical window sizes via `screen.Scaling`, then sets both
windows' `Position` so the pair is horizontally and vertically centred on screen.

`OnShowSpinnerWindowClicked` — event handler for the "Show Spinner Window" button.
Calls `Activate()` if the window is already visible (bring to front), or `Show()` if
it has been minimised.

---

### `Views/SpinnerWindow.axaml` — OBS capture window

**The wheel-only display window.** Has `x:DataType="vm:MainWindowViewModel"`.
The entire `Window.Background` is the chromakey colour — no static value, always live:

```xml
Background="{Binding ChromaKeyColor, Converter={StaticResource HexColorToBrush}}"
```

#### Layout structure

```
Window (620×660, title "SpinnerWheel — OBS Capture", resizable)
Background = ChromaKeyColor (live)
└── Grid (no row/column definitions — children overlap)
    ├── Viewbox (Stretch=Uniform, fills window)
    │   └── SpinnerWheelControl (600×600 logical px)
    └── Border (VerticalAlignment=Bottom, IsVisible when WinnerMessage non-empty)
            Background = #CC000000 (dark semi-transparent pill)
            └── TextBlock: WinnerMessage (30px bold white)
```

**Why the winner banner is semi-transparent black, not chromakey coloured:**
If the banner background matched the chromakey colour, OBS would key it out and the
winner text would float without a legible background. The dark pill (`#CC000000`) is
intentionally opaque so it remains readable after keying.

**Why no `ColumnDefinitions`/`RowDefinitions` on the root Grid:**
Omitting them makes all children fill the same space (Z-stacked). The winner banner
overlays the wheel without pushing it upwards.

**Corners of the wheel:**
`SpinnerWheelControl` does not paint anything outside the circular wheel. The window
background (chromakey colour) shows through in those areas. OBS keys out the solid
colour, resulting in a clean circular wheel shape over the game scene.

---

### `Views/SpinnerWindow.axaml.cs` — OBS capture window code-behind

Two constructors (chained):

```csharp
// Parameterless — required by Avalonia XAML loader and IDE design tools.
public SpinnerWindow()
{
    InitializeComponent();
}

// Runtime constructor — shares the ViewModel.
public SpinnerWindow(MainWindowViewModel vm) : this()
{
    DataContext = vm;
}
```

There is no longer a hide-instead-of-close guard or `CloseForReal()`. Closing the
`SpinnerWindow` is handled symmetrically from `MainWindow` — both windows close together.

**Why two constructors?** Avalonia's build system warns (`AVLN3001`) if a Window has no
public parameterless constructor, because the XAML loader cannot instantiate it for
design-time preview. Chaining to the parameterless constructor keeps all initialisation
in one place while satisfying the loader.

---

### `Controls/SpinnerWheelControl.cs`

**Custom Avalonia `Control` subclass that owns all wheel rendering.**

It has **no AXAML template** — it renders entirely by overriding `Render(DrawingContext)`.
This gives complete control over pixel-level drawing at the cost of not being able to
use standard Avalonia control composition inside it.

#### Styled properties

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Slices` | `ObservableCollection<WheelSliceViewModel>?` | null | The slice data to render |
| `RotationAngle` | `double` | 0.0 | Current clockwise rotation in degrees |
| `WinnerIndex` | `int` | -1 | Which slice to highlight in gold; -1 = none |

`RotationAngleProperty` and `WinnerIndexProperty` are registered with `AffectsRender<>`,
meaning changing them automatically schedules a redraw via `InvalidateVisual()`.

`SlicesProperty` is **not** registered with `AffectsRender` because the reference itself
rarely changes — instead `OnPropertyChanged` manually subscribes to:
- `ObservableCollection.CollectionChanged` (slice added/removed/moved)
- Each `WheelSliceViewModel.PropertyChanged` (label edited, colour changed, image assigned)

All of these call `InvalidateVisual()`, which queues a redraw on the next render frame.
Subscriptions are carefully cleaned up in `OnPropertyChanged` to avoid memory leaks.

#### Rendering pipeline

`Render(DrawingContext ctx)` is called by Avalonia whenever the control needs repainting:

1. Reads `Bounds` to determine actual pixel dimensions.
2. Calculates `center` (midpoint) and `radius` (half the smallest dimension minus 18px padding).
3. Builds a **rotation matrix** that rotates the canvas around the centre point:
   ```csharp
   Matrix.CreateTranslation(-cx, -cy) *
   Matrix.CreateRotation(rad) *
   Matrix.CreateTranslation(cx, cy)
   ```
   This is the standard "rotate around a point" transform using Avalonia's row-vector
   matrix convention (A × B applies A first, then B).
4. Inside `ctx.PushTransform(rotMatrix)` — draws all slices (rotates with the wheel).
5. Outside the transform — draws the outer ring, pointer indicator, and centre pin
   (these are **fixed** in screen space and do not rotate).

#### DrawSlices

Loops through all `WheelSliceViewModel` items. For each slice:
- Calculates `startAngle` from `-Math.PI/2 + i * sliceRad` (starts at top, goes clockwise).
- Calls `DrawPieSlice` to fill the triangular sector.
- If `i == WinnerIndex`: uses a 4px gold `Pen` border instead of the 2px white one,
  and lightens the fill colour by 20% using the `Lighten` helper.
- Calls `DrawSliceImage` if a `LoadedBitmap` is present.
- Calls `DrawSliceLabel` if `Label` is non-empty.

#### DrawPieSlice

Handles the special case of a single slice (draws a full circle) and the general case
(draws a pie sector using `StreamGeometry` with `ArcTo`).

`StreamGeometry` is built imperatively:
```
BeginFigure(center) → LineTo(arcStart) → ArcTo(arcEnd, radius, isLargeArc=true if > 180°) → EndFigure
```

#### DrawSliceImage

Places the image at **55% of the radius** from the centre along the midAngle direction.
Image size is capped at `min(radius × 0.28, 52px)`. There is **no clipping** — images
small enough relative to the wheel will not overflow into neighbouring slices. If clipping
is needed in the future, use `ctx.PushClip(geometry)` around the `DrawImage` call.

#### DrawSliceLabel

Places text at **70% of the radius** from the centre. Font size scales with slice count:
- ≤ 4 slices: 14px
- 5–8 slices: 12px
- > 8 slices: 10px

Labels longer than 16 characters are truncated with `…`. Text is centred on the midAngle
point using `FormattedText.Width` and `.Height`.

#### Fixed decorations

- **`DrawOuterRing`** — a `#444444` circle with 3px stroke drawn over the rotating slices
  to clean up any anti-aliasing jaggies at the edge.
- **`DrawPointer`** — a red (`#E74C3C`) downward-pointing triangle. Its tip touches the
  wheel rim at the 12 o'clock position. Base half-width = 11px, height = 20px.
- **`DrawCenterPin`** — a white disc (r=14) with a red dot (r=7) at the exact centre,
  covering the point where all slice borders converge.

#### Empty state

When `Slices` is null or empty, `DrawEmptyWheel` renders a dark grey circle with the text
"Add slices to get started" centred inside it. The pointer is still drawn so the user
can see where the indicator is.

---

### `Services/IFilePickerService.cs`

**Dependency inversion interface** that isolates the ViewModel from any Avalonia UI
types. The ViewModel calls these methods and receives a `string?` path back.

```csharp
Task<string?> OpenImageFileAsync();
Task<string?> OpenSoundFileAsync();
Task<string?> OpenLayoutFileAsync();
Task<string?> SaveLayoutFileAsync(string defaultName);
```

All methods return `null` if the user cancels the dialog or if the platform cannot
provide a `StorageProvider`.

---

### `Services/WindowFilePickerService.cs`

**Concrete implementation of `IFilePickerService`** using Avalonia's cross-platform
`IStorageProvider` API (introduced in Avalonia 11 to replace the older `OpenFileDialog`).

Key points:
- Resolves the `IStorageProvider` lazily via `TopLevel.GetTopLevel(_window)?.StorageProvider`.
  `TopLevel` is the Avalonia class that represents the top-level native window. On
  all platforms it provides the `StorageProvider` for native OS file dialogs.
- `TryGetLocalPath()` converts the `IStorageFile` URI to a local filesystem path.
  Use this rather than `.Path.LocalPath` because `TryGetLocalPath()` handles
  sandboxed environments (e.g. macOS App Sandbox) where URI access may differ.
- Three static `FilePickerFileType` filter objects are shared across calls (image,
  audio, layout) to avoid allocation per dialog open.

---

### `Services/LayoutService.cs`

**JSON save/load for `WheelLayout`.** Uses `System.Text.Json` (built into .NET, no
extra package needed).

Options:
```csharp
new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}
```

`camelCase` means `WheelSlice.ImagePath` serialises as `"imagePath"` in JSON.
Do not change the naming policy without also updating any existing saved layout files.

Both `SaveAsync` and `LoadAsync` are `async` — they use `File.WriteAllTextAsync` /
`File.ReadAllTextAsync` so the UI thread is not blocked during disk I/O.
`LoadAsync` returns `null` (rather than throwing) on parse failure and logs to
`Debug.WriteLine`.

---

### `Services/AudioService.cs`

**Cross-platform, fire-and-forget audio playback** by spawning a system audio process.
Implements `IDisposable` — call `Dispose()` to kill any in-progress audio.

| Platform | WAV | MP3 |
|----------|-----|-----|
| Windows | PowerShell `Media.SoundPlayer.PlaySync()` | `cmd /c start /b` (opens default player) |
| macOS | `afplay` | `afplay` (handles both) |
| Linux | `aplay` | `mpg123` (must be installed) |

`StopCurrent()` kills the previous process (with `entireProcessTree: true`) before
starting a new one — so overlapping sounds do not stack up.

`StartProcess` is a private helper that creates a `ProcessStartInfo` with
`UseShellExecute=false`, `CreateNoWindow=true`, and redirected stdout/stderr so no
console window appears.

**Limitation:** Windows MP3 playback uses `start /b` which opens whatever the user has
set as their default media player. For seamless in-process MP3 on all platforms,
consider replacing this class with a `LibVLCSharp`-based implementation.

---

### `Converters/HexColorToBrushConverter.cs`

**One-way `IValueConverter`** that parses a CSS hex string and returns a
`SolidColorBrush`. Used in the ListBox item template to colour the small swatch square
next to each slice name.

- Registered in `App.axaml` under key `"HexColorToBrush"`.
- `Convert`: calls `Color.Parse(hex)` inside a try/catch; returns a gray brush on failure.
- `ConvertBack`: throws `NotSupportedException` (one-way only).

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
3. The generated command name is the method name with `Async` stripped and `Command` appended.
4. Bind it in AXAML: `Command="{Binding MyNewCommand}"`.
5. If the command's enabled state depends on `SelectedSlice`, add
   `[NotifyCanExecuteChangedFor(nameof(MyNewCommand))]` to the `_selectedSlice` field.

### Change the spin animation curve

Edit `OnAnimationTick` in `MainWindowViewModel.cs`. The current curve is two-phase
(quadratic ease-in-out for the fast part, quintic ease-out for the dramatic finale).
The split point (`splitT = 0.67`) and the progress split (`splitProgress = 0.82`) can
be tuned to shift more or less rotation into the dramatic slowdown phase.
`t` is in [0, 1] (0 = animation start, 1 = animation end).

### Change how slices are drawn

All drawing code is in `Controls/SpinnerWheelControl.cs`. The methods are all `static`
and receive `DrawingContext ctx` as their first argument. Key entry points:
- `DrawSlices` — the outer loop, where you can change per-slice behaviour
- `DrawPieSlice` — the geometry of each sector
- `DrawSliceLabel` — text positioning and font
- `DrawSliceImage` — image positioning and size
- `DrawPointer` — the fixed triangle indicator

### Add a global UI style

Add `<Style Selector="...">` elements inside `<Application.Styles>` in `App.axaml`,
after `<FluentTheme />`. This applies the style to every instance of the matched control
across the whole app.

### Change the default palette

Edit `PaletteColors` in `MainWindowViewModel.cs`. It is a `static readonly string[]` of
CSS hex strings. The array length can be anything; the code uses `% PaletteColors.Length`
so it always wraps correctly.

---

## What NOT to do

- **Do not use `WrapPanel` with a spacing property** — Avalonia's `WrapPanel` has no
  `Spacing`, `ItemSpacing`, or `LineSpacing`. Use a `StackPanel` instead when you need
  horizontal items with gaps.
- **Do not add `x:CompileBindings="False"`** to bypass compiled bindings without a good
  reason — the whole project is configured for compiled bindings and the type-safety
  catches errors early.
- **Do not reference `_fieldName` directly in the ViewModel body** when an `[ObservableProperty]`
  field has that name — CommunityToolkit emits warning MVVMTK0034. Always use the
  generated PascalCase property (`SelectedSlice`, not `_selectedSlice`).
- **Do not add ReactiveUI** — the project deliberately uses CommunityToolkit.Mvvm only.
- **Do not embed binary assets in the JSON layout** — `WheelLayout` stores file paths only.
- **Do not call `InvalidateVisual()` from a background thread** — always ensure calls
  originate on the UI thread (the `DispatcherTimer` guarantees this for animation).
