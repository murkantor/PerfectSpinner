using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GoldenSpinner.ViewModels;

namespace GoldenSpinner.Controls
{
    /// <summary>
    /// Custom Avalonia control that renders the spinner wheel.
    ///
    /// Coordinate convention
    /// ─────────────────────
    /// • Slices are drawn starting at canvas angle –90° (12 o'clock) going clockwise.
    /// • The fixed pointer indicator sits at the top of the wheel in screen space.
    /// • The entire slice layer is rotated by <see cref="RotationAngle"/> degrees (clockwise).
    ///
    /// Per-slice image modes  (<see cref="SliceImageMode"/>)
    /// ──────────────────────
    ///   0  Static   – image fixed in screen space; the rotating pie wedge is a window over it.
    ///   1  Rotating – image rotates with its slice (bottom anchored at wheel centre).
    ///   2  Upright  – image orbits with its slice but never rotates; always stays right-side up.
    /// </summary>
    public sealed class SpinnerWheelControl : Control
    {
        // ── Styled properties ────────────────────────────────────────────────

        public static readonly StyledProperty<ObservableCollection<WheelSliceViewModel>?> SlicesProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, ObservableCollection<WheelSliceViewModel>?>(
                nameof(Slices));

        public static readonly StyledProperty<double> RotationAngleProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, double>(nameof(RotationAngle));

        public static readonly StyledProperty<int> WinnerIndexProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(WinnerIndex), -1);

        public static readonly StyledProperty<bool> UseWeightedSlicesProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(UseWeightedSlices), false);

        /// <summary>0 = Static, 1 = Rotating, 2 = Upright.</summary>
        public static readonly StyledProperty<int> SliceImageModeProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(SliceImageMode), 0);

        public static readonly StyledProperty<bool> ShowLabelsProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(ShowLabels), true);

        public static readonly StyledProperty<string> LabelFontFamilyProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, string>(nameof(LabelFontFamily), "");

        /// <summary>Font size in pixels. 0 = auto-scale by slice count.</summary>
        public static readonly StyledProperty<double> LabelFontSizeProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, double>(nameof(LabelFontSize), 0);

        /// <summary>0 = white text / black border. 1 = black text / white border.</summary>
        public static readonly StyledProperty<int> LabelColorStyleProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(LabelColorStyle), 0);

        /// <summary>When true, labels are drawn with bold weight.</summary>
        public static readonly StyledProperty<bool> LabelBoldProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(LabelBold), false);

        /// <summary>When true, a white overlay is drawn over the winning slice to brighten it.</summary>
        public static readonly StyledProperty<bool> BrightenWinnerProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(BrightenWinner), false);

        /// <summary>When true, a dark overlay is drawn over all losing slices.</summary>
        public static readonly StyledProperty<bool> DarkenLosersProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(DarkenLosers), false);

        /// <summary>When true, the border colour is applied as text colour on losing slices.</summary>
        public static readonly StyledProperty<bool> InvertLoserTextProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(InvertLoserText), false);

        /// <summary>0 = white borders (outer ring + slice dividers). 1 = black.</summary>
        public static readonly StyledProperty<int> BorderColorStyleProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(BorderColorStyle), 0);

        /// <summary>When true, the label of the slice currently under the pointer is shown near the top of the wheel.</summary>
        public static readonly StyledProperty<bool> ShowPointerLabelProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(ShowPointerLabel), false);

        /// <summary>0 = off, 1 = reveal winner only, 2 = reveal all on win.</summary>
        public static readonly StyledProperty<int> BlackoutWheelModeProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(BlackoutWheelMode), 0);

        /// <summary>When true, a confetti burst plays when a winner is decided.</summary>
        public static readonly StyledProperty<bool> ShowConfettiProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(ShowConfetti), false);

        /// <summary>Optional path to a PNG/JPEG/GIF used as each confetti particle. Null = default coloured shapes.</summary>
        public static readonly StyledProperty<string?> ConfettiImagePathProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, string?>(nameof(ConfettiImagePath));

        /// <summary>Number of confetti particles to spawn. 1–500, default 120.</summary>
        public static readonly StyledProperty<int> ConfettiCountProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(ConfettiCount), 120);

        /// <summary>0 = Mixed, 1 = Strips, 2 = Circles, 3 = Triangles, 4 = Stars.</summary>
        public static readonly StyledProperty<int> ConfettiShapeModeProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(ConfettiShapeMode), 0);

        /// <summary>0 = Rainbow, 1 = Custom colour.</summary>
        public static readonly StyledProperty<int> ConfettiColorModeProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, int>(nameof(ConfettiColorMode), 0);

        /// <summary>CSS hex string used when ConfettiColorMode = 1.</summary>
        public static readonly StyledProperty<string> ConfettiCustomColorProperty =
            AvaloniaProperty.Register<SpinnerWheelControl, string>(nameof(ConfettiCustomColor), "#FFD700");

        // ── Property accessors ───────────────────────────────────────────────

        public ObservableCollection<WheelSliceViewModel>? Slices
        {
            get => GetValue(SlicesProperty);
            set => SetValue(SlicesProperty, value);
        }

        public double RotationAngle
        {
            get => GetValue(RotationAngleProperty);
            set => SetValue(RotationAngleProperty, value);
        }

        public int WinnerIndex
        {
            get => GetValue(WinnerIndexProperty);
            set => SetValue(WinnerIndexProperty, value);
        }

        public bool UseWeightedSlices
        {
            get => GetValue(UseWeightedSlicesProperty);
            set => SetValue(UseWeightedSlicesProperty, value);
        }

        public int SliceImageMode
        {
            get => GetValue(SliceImageModeProperty);
            set => SetValue(SliceImageModeProperty, value);
        }

        public bool ShowLabels
        {
            get => GetValue(ShowLabelsProperty);
            set => SetValue(ShowLabelsProperty, value);
        }

        public string LabelFontFamily
        {
            get => GetValue(LabelFontFamilyProperty);
            set => SetValue(LabelFontFamilyProperty, value);
        }

        public double LabelFontSize
        {
            get => GetValue(LabelFontSizeProperty);
            set => SetValue(LabelFontSizeProperty, value);
        }

        public int LabelColorStyle
        {
            get => GetValue(LabelColorStyleProperty);
            set => SetValue(LabelColorStyleProperty, value);
        }

        public bool LabelBold
        {
            get => GetValue(LabelBoldProperty);
            set => SetValue(LabelBoldProperty, value);
        }

        public bool BrightenWinner
        {
            get => GetValue(BrightenWinnerProperty);
            set => SetValue(BrightenWinnerProperty, value);
        }

        public bool DarkenLosers
        {
            get => GetValue(DarkenLosersProperty);
            set => SetValue(DarkenLosersProperty, value);
        }

        public bool InvertLoserText
        {
            get => GetValue(InvertLoserTextProperty);
            set => SetValue(InvertLoserTextProperty, value);
        }

        public int BorderColorStyle
        {
            get => GetValue(BorderColorStyleProperty);
            set => SetValue(BorderColorStyleProperty, value);
        }

        public bool ShowPointerLabel
        {
            get => GetValue(ShowPointerLabelProperty);
            set => SetValue(ShowPointerLabelProperty, value);
        }

        public int BlackoutWheelMode
        {
            get => GetValue(BlackoutWheelModeProperty);
            set => SetValue(BlackoutWheelModeProperty, value);
        }

        public bool ShowConfetti
        {
            get => GetValue(ShowConfettiProperty);
            set => SetValue(ShowConfettiProperty, value);
        }

        public string? ConfettiImagePath
        {
            get => GetValue(ConfettiImagePathProperty);
            set => SetValue(ConfettiImagePathProperty, value);
        }

        public int ConfettiCount
        {
            get => GetValue(ConfettiCountProperty);
            set => SetValue(ConfettiCountProperty, value);
        }

        public int ConfettiShapeMode
        {
            get => GetValue(ConfettiShapeModeProperty);
            set => SetValue(ConfettiShapeModeProperty, value);
        }

        public int ConfettiColorMode
        {
            get => GetValue(ConfettiColorModeProperty);
            set => SetValue(ConfettiColorModeProperty, value);
        }

        public string ConfettiCustomColor
        {
            get => GetValue(ConfettiCustomColorProperty);
            set => SetValue(ConfettiCustomColorProperty, value);
        }

        // ── Constructor ───────────────────────────────────────────────────────

        public SpinnerWheelControl()
        {
            // Text and images are antialiased globally.
            // EdgeMode is intentionally left at default (Aliased) so the outer wheel
            // boundary stays pixel-hard for clean OBS chroma key capture.
            // Interior antialiasing is enabled via PushRenderOptions inside the wheel clip.
            RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        // ── Static constructor – wire up AffectsRender ───────────────────────

        static SpinnerWheelControl()
        {
            AffectsRender<SpinnerWheelControl>(
                RotationAngleProperty, WinnerIndexProperty,
                UseWeightedSlicesProperty, SliceImageModeProperty,
                ShowLabelsProperty, LabelFontFamilyProperty,
                LabelFontSizeProperty, LabelColorStyleProperty, LabelBoldProperty,
                BrightenWinnerProperty, DarkenLosersProperty, InvertLoserTextProperty,
                ShowPointerLabelProperty, BorderColorStyleProperty, BlackoutWheelModeProperty);
        }

        // ── Unit geometries for confetti shapes ───────────────────────────────
        // Built once at class load time; both are centred at the origin with radius 1.
        // At render time they are scaled by drawSize/2 and translated to the particle centre.

        private static readonly StreamGeometry UnitTriangle = BuildUnitTriangle();
        private static readonly StreamGeometry UnitStar     = BuildUnitStar();

        private static StreamGeometry BuildUnitTriangle()
        {
            // Equilateral triangle, circumradius = 1, pointing up.
            const double r = 1.0;
            double h = r * Math.Sqrt(3) / 2;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(0, -r), true);
                gc.LineTo(new Point( h,  r * 0.5));
                gc.LineTo(new Point(-h,  r * 0.5));
                gc.EndFigure(true);
            }
            return geo;
        }

        private static StreamGeometry BuildUnitStar()
        {
            // 5-pointed star, outer radius = 1, inner radius ≈ 0.382 (golden ratio).
            const double outerR = 1.0;
            const double innerR = 0.382;
            const int    points = 5;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                bool first = true;
                for (int i = 0; i < points * 2; i++)
                {
                    double angle = i * Math.PI / points - Math.PI / 2;
                    double r     = (i % 2 == 0) ? outerR : innerR;
                    var    pt    = new Point(r * Math.Cos(angle), r * Math.Sin(angle));
                    if (first) { gc.BeginFigure(pt, true); first = false; }
                    else         gc.LineTo(pt);
                }
                gc.EndFigure(true);
            }
            return geo;
        }

        // ── Confetti particle system ──────────────────────────────────────────

        private sealed class ConfettiParticle
        {
            public double X, Y;             // position relative to wheel centre
            public double VX, VY;           // velocity px/s
            public double BaseSize;         // reference size in px
            public double Rotation;         // degrees
            public double RotVelocity;      // degrees/s
            public double Age;              // seconds since spawn
            public double Lifetime;         // total lifetime seconds
            public Color  Color;
            public int    Shape;            // 0 = strip, 1 = circle, 2 = triangle, 3 = star
        }

        private static readonly Color[] ConfettiColors =
        [
            Color.Parse("#FF4444"), Color.Parse("#FFD700"), Color.Parse("#44CC44"),
            Color.Parse("#4488FF"), Color.Parse("#FF69B4"), Color.Parse("#AA44FF"),
            Color.Parse("#FF8C00"), Color.Parse("#00CED1"), Color.Parse("#FFFFFF"),
        ];

        private readonly List<ConfettiParticle> _confettiParticles = new();
        private DispatcherTimer? _confettiTimer;
        private DateTimeOffset   _lastConfettiTick;
        private Bitmap?          _confettiBitmap;      // single-frame (PNG/JPEG)
        private readonly List<Bitmap> _confettiFrames      = new(); // GIF frames
        private readonly List<int>    _confettiFrameDelays = new(); // ms per frame
        private int    _confettiFrameIndex;
        private double _confettiFrameAccumMs;

        private void SpawnConfetti()
        {
            _confettiParticles.Clear();
            _confettiFrameIndex  = 0;
            _confettiFrameAccumMs = 0;
            var rng = new Random();

            // We need the wheel radius at spawn time. Use a cached value; it will
            // be accurate once the control has been laid out at least once.
            // Fall back to a reasonable default if not yet measured.
            var size   = Math.Min(Bounds.Width, Bounds.Height);
            var radius = (size > 0 ? size : 600) / 2.0 - 18;

            int shapeMode = ConfettiShapeMode;
            int colorMode = ConfettiColorMode;
            Color customColor = Color.Parse("#FFD700");
            if (colorMode == 1)
            {
                try { customColor = Color.Parse(ConfettiCustomColor); }
                catch { customColor = Color.Parse("#FFD700"); }
            }

            int particleCount = Math.Clamp(ConfettiCount, 1, 500);
            for (int i = 0; i < particleCount; i++)
            {
                // Each particle has a personal max spread radius (40–90 % of wheel).
                // Speed is set so it arrives at maxSpread exactly at the end of its lifetime,
                // ensuring no particle ever exceeds the wheel boundary.
                var angle     = rng.NextDouble() * 2 * Math.PI;
                var lifetime  = 1.5 + rng.NextDouble() * 2.0;
                var maxSpread = radius * (0.4 + rng.NextDouble() * 0.5);
                var speed     = maxSpread / lifetime;

                // Shape: Mixed picks randomly from all 4; specific mode forces one shape.
                int shape = shapeMode switch
                {
                    1 => 0,          // Strips
                    2 => 1,          // Circles
                    3 => 2,          // Triangles
                    4 => 3,          // Stars
                    _ => rng.Next(4) // Mixed
                };

                Color color = colorMode == 1
                    ? customColor
                    : ConfettiColors[rng.Next(ConfettiColors.Length)];

                _confettiParticles.Add(new ConfettiParticle
                {
                    X           = 0,
                    Y           = 0,
                    VX          = speed * Math.Cos(angle),
                    VY          = speed * Math.Sin(angle),
                    BaseSize    = 10 + rng.NextDouble() * 16,
                    Rotation    = rng.NextDouble() * 360,
                    RotVelocity = (rng.NextDouble() - 0.5) * 720,
                    Age         = 0,
                    Lifetime    = lifetime,
                    Color       = color,
                    Shape       = shape,
                });
            }

            _lastConfettiTick = DateTimeOffset.UtcNow;
            _confettiTimer?.Stop();
            _confettiTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _confettiTimer.Tick += OnConfettiTick;
            _confettiTimer.Start();
        }

        private void StopConfetti()
        {
            _confettiTimer?.Stop();
            _confettiTimer        = null;
            _confettiFrameIndex   = 0;
            _confettiFrameAccumMs = 0;
            _confettiParticles.Clear();
            InvalidateVisual();
        }

        private void OnConfettiTick(object? sender, EventArgs e)
        {
            var now = DateTimeOffset.UtcNow;
            var dt  = Math.Min((now - _lastConfettiTick).TotalSeconds, 0.05);
            _lastConfettiTick = now;

            // Advance GIF frame when the current frame's delay has elapsed.
            if (_confettiFrames.Count > 1)
            {
                _confettiFrameAccumMs += dt * 1000;
                int frameDelay = _confettiFrameIndex < _confettiFrameDelays.Count
                    ? _confettiFrameDelays[_confettiFrameIndex]
                    : 100;
                if (_confettiFrameAccumMs >= frameDelay)
                {
                    _confettiFrameAccumMs -= frameDelay;
                    _confettiFrameIndex    = (_confettiFrameIndex + 1) % _confettiFrames.Count;
                }
            }

            bool anyAlive = false;
            foreach (var p in _confettiParticles)
            {
                p.Age      += dt;
                p.X        += p.VX * dt;
                p.Y        += p.VY * dt;
                p.Rotation += p.RotVelocity * dt;
                if (p.Age < p.Lifetime) anyAlive = true;
            }

            InvalidateVisual();

            if (!anyAlive)
            {
                _confettiTimer?.Stop();
                _confettiTimer = null;
                _confettiParticles.Clear();
                InvalidateVisual();
            }
        }

        private void LoadConfettiBitmap()
        {
            // Dispose all previous resources.
            _confettiBitmap?.Dispose();
            _confettiBitmap = null;
            foreach (var f in _confettiFrames) f.Dispose();
            _confettiFrames.Clear();
            _confettiFrameDelays.Clear();
            _confettiFrameIndex   = 0;
            _confettiFrameAccumMs = 0;

            var path = ConfettiImagePath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

            try
            {
                if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    LoadGifFrames(path);
                else
                    _confettiBitmap = new Bitmap(path);
            }
            catch { /* silently ignore bad paths */ }
        }

        private void LoadGifFrames(string path)
        {
            using var sysImg = System.Drawing.Image.FromFile(path);
            var dim        = new System.Drawing.Imaging.FrameDimension(sysImg.FrameDimensionsList[0]);
            int frameCount = sysImg.GetFrameCount(dim);

            // Property 0x5100 = PropertyTagFrameDelay; values are in 1/100 s units.
            int[]? rawDelays = null;
            try
            {
                var prop = sysImg.GetPropertyItem(0x5100);
                if (prop?.Value != null && prop.Value.Length >= frameCount * 4)
                {
                    rawDelays = new int[frameCount];
                    for (int i = 0; i < frameCount; i++)
                        rawDelays[i] = Math.Max(20, BitConverter.ToInt32(prop.Value, i * 4) * 10);
                }
            }
            catch { }

            for (int i = 0; i < frameCount; i++)
            {
                sysImg.SelectActiveFrame(dim, i);
                using var copy = new System.Drawing.Bitmap(sysImg);
                using var ms   = new System.IO.MemoryStream();
                copy.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                _confettiFrames.Add(new Bitmap(ms));
                _confettiFrameDelays.Add(rawDelays?[i] ?? 100);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _confettiTimer?.Stop();
            _confettiTimer = null;
            _confettiBitmap?.Dispose();
            _confettiBitmap = null;
            foreach (var f in _confettiFrames) f.Dispose();
            _confettiFrames.Clear();
        }

        // ── Property change tracking ─────────────────────────────────────────

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SlicesProperty)
            {
                if (change.OldValue is ObservableCollection<WheelSliceViewModel> oldCol)
                {
                    oldCol.CollectionChanged -= OnCollectionChanged;
                    foreach (var s in oldCol) s.PropertyChanged -= OnSliceChanged;
                }

                if (change.NewValue is ObservableCollection<WheelSliceViewModel> newCol)
                {
                    newCol.CollectionChanged += OnCollectionChanged;
                    foreach (var s in newCol) s.PropertyChanged += OnSliceChanged;
                }

                InvalidateVisual();
            }
            else if (change.Property == WinnerIndexProperty)
            {
                int oldVal = change.OldValue is int ov ? ov : -1;
                int newVal = change.NewValue is int nv ? nv : -1;
                if (oldVal < 0 && newVal >= 0 && ShowConfetti)
                    SpawnConfetti();
                else if (newVal < 0)
                    StopConfetti();
            }
            else if (change.Property == ConfettiImagePathProperty)
            {
                LoadConfettiBitmap();
            }
            else if (change.Property == ShowConfettiProperty && !(bool)(change.NewValue ?? false))
            {
                StopConfetti();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (WheelSliceViewModel s in e.OldItems) s.PropertyChanged -= OnSliceChanged;
            if (e.NewItems != null)
                foreach (WheelSliceViewModel s in e.NewItems) s.PropertyChanged += OnSliceChanged;

            InvalidateVisual();
        }

        private void OnSliceChanged(object? sender, PropertyChangedEventArgs e) =>
            InvalidateVisual();

        // ── Rendering ────────────────────────────────────────────────────────

        public override void Render(DrawingContext ctx)
        {
            var slices = Slices;
            var bounds = Bounds;

            if (bounds.Width < 1 || bounds.Height < 1) return;

            var size   = Math.Min(bounds.Width, bounds.Height);
            var cx     = bounds.Width / 2;
            var cy     = bounds.Height / 2;
            var center = new Point(cx, cy);
            var radius = size / 2 - 18;

            if (slices == null || slices.Count == 0)
            {
                DrawEmptyWheel(ctx, center, radius);
                DrawPointer(ctx, center, radius);
                return;
            }

            var active = new List<WheelSliceViewModel>();
            foreach (var s in slices)
                if (s.IsActive) active.Add(s);

            if (active.Count == 0)
            {
                IBrush emptyBorderBrush = BorderColorStyle == 1 ? Brushes.Black : Brushes.White;
                DrawEmptyWheel(ctx, center, radius);
                DrawOuterRing(ctx, center, radius, emptyBorderBrush);
                DrawPointer(ctx, center, radius);
                DrawCenterPin(ctx, center, emptyBorderBrush, new SolidColorBrush(Color.Parse("#E74C3C")));
                return;
            }

            bool   useWeighted = UseWeightedSlices;
            double totalWeight = useWeighted ? active.Sum(s => Math.Max(1.0, s.Weight)) : active.Count;
            int    n           = active.Count;
            int    imageMode        = SliceImageMode;
            bool   showLabels       = ShowLabels;
            string labelFont        = LabelFontFamily;
            double labelFontSize    = LabelFontSize;
            int    labelColorStyle  = LabelColorStyle;
            bool   labelBold        = LabelBold;
            bool   brightenWinner   = BrightenWinner;
            bool   darkenLosers     = DarkenLosers;
            bool   invertLoserText  = InvertLoserText;
            bool   showPointerLabel = ShowPointerLabel;
            int    borderColorStyle = BorderColorStyle;
            int    blackoutMode     = BlackoutWheelMode;
            IBrush borderBrush      = borderColorStyle == 1 ? Brushes.Black : Brushes.White;

            // Pre-compute per-slice angles.
            var starts   = new double[n];
            var ends     = new double[n];
            var isWinner = new bool[n];
            double ang = -Math.PI / 2;
            for (int i = 0; i < n; i++)
            {
                double w    = useWeighted ? Math.Max(1.0, active[i].Weight) : 1.0;
                starts[i]   = ang;
                ends[i]     = ang + (w / totalWeight) * 2 * Math.PI;
                isWinner[i] = slices.IndexOf(active[i]) == WinnerIndex;
                ang = ends[i];
            }

            var rotRad    = RotationAngle * Math.PI / 180.0;
            var rotMatrix =
                Matrix.CreateTranslation(-cx, -cy) *
                Matrix.CreateRotation(rotRad) *
                Matrix.CreateTranslation(cx, cy);

            // ── Wheel clip (hard outer edge, created in aliased context) ────────
            // Everything inside this clip is antialiased via the inner PushRenderOptions.
            // Because the clip itself is pushed before antialiasing is enabled, the clip
            // boundary stays pixel-hard — essential for clean OBS chroma key capture.
            var wheelClip = new EllipseGeometry(
                new Rect(cx - radius, cy - radius, radius * 2, radius * 2));

            using (ctx.PushGeometryClip(wheelClip))
            using (ctx.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Antialias }))
            {

            // ── Pass 1: Screen-space images (Static and Upright modes) ────────
            //
            // Drawn before the rotation transform so images have no rotation applied.
            // The clip geometry is in screen space (slice angles + current rotation).
            //
            //  Static  (0): image centred on wheel centre — wedge window slides over it.
            //  Upright (2): image centred on slice's current screen-space midpoint —
            //               orbits with the slice while staying right-side-up.
            if (imageMode == 0 || imageMode == 2)
            {
                for (int i = 0; i < n; i++)
                {
                    if (active[i].LoadedBitmap is not Bitmap bmp) continue;

                    var screenGeo = BuildSliceGeometry(
                        center, radius,
                        starts[i] + rotRad, ends[i] + rotRad,
                        n == 1);

                    Point imageCenter;
                    if (imageMode == 2)
                    {
                        // Upright: centre the image on the slice's midpoint at 50 % radius.
                        var screenMid = (starts[i] + ends[i]) / 2.0 + rotRad;
                        imageCenter = new Point(
                            center.X + radius * 0.5 * Math.Cos(screenMid),
                            center.Y + radius * 0.5 * Math.Sin(screenMid));
                    }
                    else
                    {
                        imageCenter = center;
                    }

                    // Fill with border colour first so any area the image doesn't reach
                    // shows the border colour rather than the chroma key background.
                    ctx.DrawGeometry(borderBrush, null, screenGeo);
                    using (ctx.PushGeometryClip(screenGeo))
                        DrawCoverImage(ctx, bmp, imageCenter, radius);
                }
            }

            // ── Pass 2a: Rotating fills and images (antialiased, no borders) ────
            using (ctx.PushTransform(rotMatrix))
            {
                for (int i = 0; i < n; i++)
                {
                    var slice = active[i];
                    var geo   = BuildSliceGeometry(center, radius, starts[i], ends[i], n == 1);

                    if (slice.LoadedBitmap is Bitmap bmp)
                    {
                        if (imageMode == 1)
                        {
                            // Fill with border colour behind the rotating image for the same reason.
                            ctx.DrawGeometry(borderBrush, null, geo);
                            using (ctx.PushGeometryClip(geo))
                                DrawCoverImage(ctx, bmp, center, radius);
                        }
                        // Mode 0/2: fill + image already handled in Pass 1.
                    }
                    else
                    {
                        Color color;
                        try { color = Color.Parse(slice.ColorHex); }
                        catch { color = Color.Parse("#808080"); }

                        IBrush fill = isWinner[i]
                            ? new SolidColorBrush(Lighten(color, 0.2f))
                            : new SolidColorBrush(color);

                        ctx.DrawGeometry(fill, null, geo); // no border pen — drawn separately below
                    }
                }
            }

            int winnerIndex = WinnerIndex;

            // ── Pass 2.3: Blackout fill (before borders so borders show on top) ──
            // Blackout colour is the inverse of the border colour:
            //   white borders → black fill  |  black borders → white fill
            // Mode 1 (reveal winner only):
            //   - No winner yet → entire wheel blacked out.
            //   - Winner decided → black out every non-winner slice; winner stays visible.
            // Mode 2 (reveal all on win):
            //   - No winner yet → entire wheel blacked out.
            //   - Winner decided → normal rendering, no overlay.
            IBrush blackoutBrush = borderColorStyle == 1 ? Brushes.White : Brushes.Black;
            if (blackoutMode == 1)
            {
                if (winnerIndex < 0)
                {
                    ctx.DrawEllipse(blackoutBrush, null, center, radius, radius);
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (!isWinner[i])
                        {
                            var geo = BuildSliceGeometry(center, radius,
                                starts[i] + rotRad, ends[i] + rotRad, n == 1);
                            ctx.DrawGeometry(blackoutBrush, null, geo);
                        }
                    }
                }
            }
            else if (blackoutMode == 2 && winnerIndex < 0)
            {
                ctx.DrawEllipse(blackoutBrush, null, center, radius, radius);
            }

            // ── Pass 2b: Borders only (aliased — hard crisp lines) ───────────
            // Nested PushRenderOptions overrides the outer Antialias for border drawing only.
            using (ctx.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
            using (ctx.PushTransform(rotMatrix))
            {
                var slicePen  = new Pen(borderBrush, 2);
                var winnerPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 4);

                for (int i = 0; i < n; i++)
                {
                    var pen = isWinner[i] ? winnerPen : slicePen;
                    var geo = BuildSliceGeometry(center, radius, starts[i], ends[i], n == 1);
                    ctx.DrawGeometry(Brushes.Transparent, pen, geo);
                }
            }

            // ── Pass 2.5: Winner/loser highlight overlays (screen-space) ─────────
            // Applied after fills so the overlay sits on top of both solid colours
            // and images.  Only drawn when a winner has been decided.
            if (winnerIndex >= 0 && (brightenWinner || darkenLosers))
            {
                var winnerOverlay = new SolidColorBrush(Color.FromArgb(80,  255, 255, 255));
                var loserOverlay  = new SolidColorBrush(Color.FromArgb(140,   0,   0,   0));

                for (int i = 0; i < n; i++)
                {
                    var geo = BuildSliceGeometry(center, radius,
                        starts[i] + rotRad, ends[i] + rotRad, n == 1);

                    if (isWinner[i] && brightenWinner)
                        ctx.DrawGeometry(winnerOverlay, null, geo);
                    else if (!isWinner[i] && darkenLosers)
                        ctx.DrawGeometry(loserOverlay, null, geo);
                }
            }

            // ── Pass 3: Upright labels (screen-space, orbits with slice) ─────────
            // Drawn after the rotation transform so labels are never flipped.
            // Label centre is at 68 % radius along the slice's current screen-space midpoint.
            if (showLabels)
            {
                for (int i = 0; i < n; i++)
                {
                    if (string.IsNullOrEmpty(active[i].Label)) continue;

                    // In blackout mode 1: skip labels on blacked-out slices.
                    // (mode 2 with no winner: entire wheel is black, skip all labels)
                    if (blackoutMode == 1 && !isWinner[i]) continue;
                    if (blackoutMode == 2 && winnerIndex < 0) continue;

                    var screenMid = (starts[i] + ends[i]) / 2.0 + rotRad;
                    var labelCenter = new Point(
                        center.X + radius * 0.68 * Math.Cos(screenMid),
                        center.Y + radius * 0.68 * Math.Sin(screenMid));
                    bool isLoser = !isWinner[i] && winnerIndex >= 0;
                    DrawSliceLabel(ctx, active[i].Label, labelCenter, n,
                        labelFont, labelFontSize, labelColorStyle, labelBold,
                        textMatchesBorder: isLoser && invertLoserText);
                }
            }

            // ── Pass 4: Pointer label — slice name just inside the top edge ──────
            // Drawn inside the wheel clip so it can't overflow the circle boundary.
            // Position is 28 px below the wheel's top edge (pointer tip is at +2 px).
            if (showPointerLabel && n > 0)
            {
                // Find which active slice is currently under the fixed pointer (12 o'clock).
                // In the unrotated wheel frame the pointer sits at -PI/2.
                // Normalise into [starts[0], starts[0] + 2*PI).
                double pAngle = -Math.PI / 2.0 - rotRad;
                double span   = 2 * Math.PI;
                double norm   = ((pAngle - starts[0]) % span + span) % span + starts[0];

                string? pLabel = null;
                for (int i = 0; i < n; i++)
                {
                    if (norm >= starts[i] && norm < ends[i])
                    {
                        pLabel = active[i].Label;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(pLabel))
                {
                    var labelPos = new Point(center.X, center.Y - radius + 28);
                    DrawSliceLabel(ctx, pLabel, labelPos, n,
                        labelFont, labelFontSize, labelColorStyle, labelBold);
                }
            }

            // ── Pass 5: Confetti burst ────────────────────────────────────────────
            // Drawn last so it sits on top of everything.
            // The wheelClip already constrains every particle to the circle boundary.
            if (_confettiParticles.Count > 0)
            {
                // Resolve which bitmap/frame to use (same for all particles each frame).
                Bitmap? confettiBmp = null;
                if (_confettiFrames.Count > 0)
                    confettiBmp = _confettiFrames[Math.Clamp(_confettiFrameIndex, 0, _confettiFrames.Count - 1)];
                else if (_confettiBitmap != null)
                    confettiBmp = _confettiBitmap;

                foreach (var p in _confettiParticles)
                {
                    if (p.Age >= p.Lifetime) continue;

                    double t = p.Age / p.Lifetime;

                    // Parabolic arc: height = 4t(1-t)
                    //   t=0   → height=0  (just launched, flat on wheel, tiny)
                    //   t=0.5 → height=1  (peak: closest to camera, largest)
                    //   t=1   → height=0  (landed, flat again, tiny)
                    // This gives the 3D rising-and-falling illusion in a top-down view.
                    double height   = 4.0 * t * (1.0 - t);
                    double drawSize = p.BaseSize * height;
                    if (drawSize < 0.5) continue; // skip invisible particles

                    // Slight opacity fade in the final 15 % so landing is smooth.
                    double opacity = t > 0.85 ? (1.0 - t) / 0.15 : 1.0;
                    if (opacity <= 0) continue;

                    double px = center.X + p.X;
                    double py = center.Y + p.Y;

                    var rot = Matrix.CreateTranslation(-px, -py)
                            * Matrix.CreateRotation(p.Rotation * Math.PI / 180.0)
                            * Matrix.CreateTranslation(px, py);

                    using var _o  = ctx.PushOpacity(opacity);
                    using var _tr = ctx.PushTransform(rot);

                    if (confettiBmp != null)
                    {
                        // Custom image (or animated GIF frame): draw scaled to drawSize.
                        ctx.DrawImage(confettiBmp,
                            new Rect(px - drawSize / 2, py - drawSize / 2, drawSize, drawSize));
                    }
                    else
                    {
                        var brush = new SolidColorBrush(p.Color);
                        if (p.Shape == 0)
                        {
                            // Elongated rectangle — classic paper-confetti strip.
                            double w = drawSize * 0.35;
                            double h = drawSize;
                            ctx.DrawRectangle(brush, null,
                                new Rect(px - w / 2, py - h / 2, w, h));
                        }
                        else if (p.Shape == 1)
                        {
                            // Circle — confetti dot.
                            ctx.DrawEllipse(brush, null,
                                new Point(px, py), drawSize * 0.4, drawSize * 0.4);
                        }
                        else
                        {
                            // Triangle (shape 2) or Star (shape 3).
                            // UnitTriangle/UnitStar are centred at origin with radius 1.
                            // Scale by drawSize/2 then translate to particle centre.
                            var shapeMatrix = Matrix.CreateScale(drawSize / 2, drawSize / 2)
                                            * Matrix.CreateTranslation(px, py);
                            using var _sh = ctx.PushTransform(shapeMatrix);
                            ctx.DrawGeometry(brush, null, p.Shape == 2 ? UnitTriangle : UnitStar);
                        }
                    }
                }
            }

            } // end wheel clip + antialias push

            // ── Fixed layer — drawn outside the clip with hard edges ──────────
            // These sit on the chroma key boundary so they must remain pixel-hard.

            // Determine which active slice is currently under the fixed pointer (12 o'clock).
            // The pointer sits at -π/2 in screen space; subtract rotRad to get wheel-frame angle.
            // When blackout is active the centre dot stays a fixed inverted-border colour
            // so it doesn't reveal which slice is under the pointer.
            // borderColorStyle 0 = white border → black dot; 1 = black border → white dot.
            IBrush dotBrush;
            if (blackoutMode > 0)
            {
                dotBrush = borderColorStyle == 1 ? Brushes.White : Brushes.Black;
            }
            else
            {
                dotBrush = new SolidColorBrush(Color.Parse("#E74C3C")); // default red
                if (n > 0)
                {
                    double pAngle = -Math.PI / 2.0 - rotRad;
                    double span   = 2 * Math.PI;
                    double norm   = ((pAngle - starts[0]) % span + span) % span + starts[0];
                    for (int i = 0; i < n; i++)
                    {
                        if (norm >= starts[i] && norm < ends[i])
                        {
                            try { dotBrush = new SolidColorBrush(Color.Parse(active[i].ColorHex)); }
                            catch { }
                            break;
                        }
                    }
                }
            }

            DrawOuterRing(ctx, center, radius, borderBrush);
            DrawPointer(ctx, center, radius);
            DrawCenterPin(ctx, center, borderBrush, dotBrush);
        }

        // ── Geometry helper ───────────────────────────────────────────────────

        private static Geometry BuildSliceGeometry(Point center, double radius,
            double startAngle, double endAngle, bool isFullCircle)
        {
            if (isFullCircle)
                return new EllipseGeometry(
                    new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));

            var start   = new Point(center.X + radius * Math.Cos(startAngle),
                                    center.Y + radius * Math.Sin(startAngle));
            var end     = new Point(center.X + radius * Math.Cos(endAngle),
                                    center.Y + radius * Math.Sin(endAngle));
            var isLarge = (endAngle - startAngle) > Math.PI;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(center, true);
                gc.LineTo(start);
                gc.ArcTo(end, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise);
                gc.EndFigure(true);
            }
            return geo;
        }

        // ── Cover image ───────────────────────────────────────────────────────

        /// <summary>
        /// Draws <paramref name="bmp"/> cover-scaled so it fills a <c>2×radius</c> square
        /// centred on <paramref name="imageCenter"/>.  The caller must push any required
        /// geometry clip before calling.
        /// </summary>
        private static void DrawCoverImage(DrawingContext ctx, Bitmap bmp,
            Point imageCenter, double radius)
        {
            var diam  = radius * 2;
            var imgW  = bmp.PixelSize.Width;
            var imgH  = bmp.PixelSize.Height;

            var scale = Math.Max(diam / imgW, diam / imgH);
            var srcW  = diam / scale;
            var srcH  = diam / scale;
            var srcX  = (imgW - srcW) / 2.0;
            var srcY  = (imgH - srcH) / 2.0;

            ctx.DrawImage(bmp,
                new Rect(srcX, srcY, srcW, srcH),
                new Rect(imageCenter.X - radius, imageCenter.Y - radius, diam, diam));
        }

        // ── Label ─────────────────────────────────────────────────────────────

        private static void DrawSliceLabel(DrawingContext ctx, string label, Point labelCenter,
            int sliceCount, string fontFamily, double fontSize, int colorStyle, bool bold,
            bool textMatchesBorder = false)
        {
            // Font size: manual override or auto-scale by slice count.
            double size = fontSize > 0
                ? fontSize
                : sliceCount <= 4 ? 14 : sliceCount <= 8 ? 12 : 10;

            var weight = bold ? FontWeight.Bold : FontWeight.Normal;
            var typeface = string.IsNullOrEmpty(fontFamily)
                ? new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, weight)
                : new Typeface(fontFamily, FontStyle.Normal, weight);

            // colorStyle 0 → white text, black border
            // colorStyle 1 → black text, white border
            IBrush borderBrush = colorStyle == 1 ? Brushes.White : Brushes.Black;
            IBrush textBrush   = textMatchesBorder ? borderBrush
                               : colorStyle == 1   ? Brushes.Black : Brushes.White;

            var display = label.Length > 16 ? label[..15] + "…" : label;

            // Build both FormattedText instances once.
            var borderFt = new FormattedText(display, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, size, borderBrush);
            var mainFt = new FormattedText(display, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, size, textBrush);

            double bx = labelCenter.X - mainFt.Width  / 2;
            double by = labelCenter.Y - mainFt.Height / 2;

            // Draw the border: 8 directional offsets at 2 px.
            const double O = 2.0;
            ReadOnlySpan<(double dx, double dy)> offsets =
            [
                (-O,  0), ( O,  0), ( 0, -O), ( 0,  O),
                (-O, -O), (-O,  O), ( O, -O), ( O,  O),
            ];
            foreach (var (dx, dy) in offsets)
                ctx.DrawText(borderFt, new Point(bx + dx, by + dy));

            // Draw the main text on top.
            ctx.DrawText(mainFt, new Point(bx, by));
        }

        // ── Fixed decorations ─────────────────────────────────────────────────

        private static void DrawOuterRing(DrawingContext ctx, Point center, double radius, IBrush color)
        {
            ctx.DrawEllipse(null, new Pen(color, 3), center, radius, radius);
        }

        private static void DrawPointer(DrawingContext ctx, Point center, double radius)
        {
            const double hs = 11.0;
            const double h  = 20.0;

            var tipY  = center.Y - radius + 2;
            var baseY = tipY - h;

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(center.X - hs, baseY), true);
                gc.LineTo(new Point(center.X + hs, baseY));
                gc.LineTo(new Point(center.X, tipY));
                gc.EndFigure(true);
            }

            ctx.DrawGeometry(
                new SolidColorBrush(Color.Parse("#E74C3C")),
                new Pen(new SolidColorBrush(Color.Parse("#C0392B")), 1.5),
                geo);
        }

        private static void DrawCenterPin(DrawingContext ctx, Point center, IBrush borderBrush, IBrush dotBrush)
        {
            ctx.DrawEllipse(borderBrush, null, center, 14, 14);
            ctx.DrawEllipse(dotBrush, null, center, 7, 7);
        }

        private static void DrawEmptyWheel(DrawingContext ctx, Point center, double radius)
        {
            ctx.DrawEllipse(
                new SolidColorBrush(Color.Parse("#2C2C2C")),
                new Pen(new SolidColorBrush(Color.Parse("#555555")), 3),
                center, radius, radius);

            var ft = new FormattedText(
                "Add slices to get started",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 16,
                new SolidColorBrush(Color.Parse("#888888")));

            ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        }

        // ── Colour helpers ────────────────────────────────────────────────────

        private static Color Lighten(Color c, float amount)
        {
            var r = Math.Min(255, (int)(c.R + 255 * amount));
            var g = Math.Min(255, (int)(c.G + 255 * amount));
            var b = Math.Min(255, (int)(c.B + 255 * amount));
            return Color.FromArgb(c.A, (byte)r, (byte)g, (byte)b);
        }
    }
}
