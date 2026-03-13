# SpinnerWheel (GoldenSpinner)

A cross-platform spinner-wheel desktop application built with **.NET 9** and **Avalonia UI 11**.

---

## Project layout

```
GoldenSpinner/
├── Models/
│   ├── WheelSlice.cs          – JSON-serialisable data model for one slice
│   └── WheelLayout.cs         – Root container for save/load
├── ViewModels/
│   ├── ViewModelBase.cs       – ObservableObject base (CommunityToolkit)
│   ├── WheelSliceViewModel.cs – Observable wrapper + runtime bitmap
│   └── MainWindowViewModel.cs – All commands, spin animation, layout I/O
├── Views/
│   ├── MainWindow.axaml       – UI layout (compiled Avalonia XAML)
│   └── MainWindow.axaml.cs    – Code-behind; wires services → ViewModel
├── Controls/
│   └── SpinnerWheelControl.cs – Custom Avalonia Control; draws the wheel
├── Services/
│   ├── IFilePickerService.cs  – Abstraction over OS file pickers
│   ├── WindowFilePickerService.cs – Avalonia StorageProvider impl.
│   ├── LayoutService.cs       – JSON save / load via System.Text.Json
│   └── AudioService.cs        – Cross-platform sound playback
├── Converters/
│   └── HexColorToBrushConverter.cs – "#RRGGBB" → SolidColorBrush
└── Assets/
    └── avalonia-logo.ico
```

---

## Prerequisites

| Tool | Minimum version |
|------|----------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0 |
| Visual Studio 2022 (optional) | 17.8 or later |
| Avalonia for VS extension (optional) | For AXAML previewer |

---

## Building & running locally

```bash
# Debug run
dotnet run

# Or open GoldenSpinner.sln in Visual Studio 2022 and press F5
```

---

## Features

| Feature | Detail |
|---------|--------|
| Spinner wheel | Smooth cubic-ease-out rotation animation (~60 fps via DispatcherTimer) |
| Slice editor  | Add / remove / reorder slices; live label & colour editing |
| Images        | PNG / JPG per slice; thumbnail rendered inside the slice |
| Sounds        | WAV / MP3 played when the winning slice lands |
| Save / Load   | JSON layout (paths are stored absolute; assets must stay in place) |
| Winner highlight | Gold-outlined winning slice + banner message |
| Spin duration | Adjustable 1–30 s via NumericUpDown |

---

## Publishing (standalone executables)

### Windows (x64)

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/win-x64
```

Output: `publish/win-x64/GoldenSpinner.exe`

### Linux (x64)

```bash
dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/linux-x64

chmod +x ./publish/linux-x64/GoldenSpinner
```

Linux audio requirements:
- WAV: `aplay` (part of `alsa-utils`)
- MP3: `mpg123` (`sudo apt install mpg123`)

### macOS (Apple Silicon / Intel)

```bash
# Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/osx-arm64

# Intel
dotnet publish -c Release -r osx-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/osx-x64
```

macOS uses `afplay` (built-in) for both WAV and MP3.

---

## Audio notes

| Platform | WAV | MP3 |
|----------|-----|-----|
| Windows  | PowerShell `Media.SoundPlayer` | Shell-opens default player |
| macOS    | `afplay` | `afplay` |
| Linux    | `aplay` | `mpg123` |

For richer cross-platform audio (including in-process MP3 on Windows) consider
adding [LibVLCSharp](https://github.com/videolan/libvlcsharp) and implementing
a custom `IAudioService`.

---

## Layout JSON format

```json
{
  "name": "My Wheel",
  "spinDurationSeconds": 4.0,
  "slices": [
    {
      "id": "…",
      "label": "Grand Prize",
      "imagePath": "C:/images/trophy.png",
      "soundPath": "C:/sounds/win.wav",
      "colorHex": "#E74C3C",
      "weight": 1.0
    }
  ]
}
```

Asset paths are stored as absolute paths. Move assets alongside the layout file
and update the paths if you relocate the project.

---

## Architecture notes

- **MVVM** via `CommunityToolkit.Mvvm`; source generators emit `[RelayCommand]`
  and `[ObservableProperty]` implementations at build time.
- `SpinnerWheelControl` is a plain `Control` subclass that owns all rendering
  via `Render(DrawingContext)` — no XAML template, no data templates.
- The spin animation runs entirely on the **UI thread** via `DispatcherTimer`
  (`DispatcherPriority.Render`) so no marshalling is required.
- `IFilePickerService` keeps the ViewModel free of Avalonia `Window` references;
  the concrete `WindowFilePickerService` lives in the view layer.
