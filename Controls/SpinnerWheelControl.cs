using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
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
    /// Winner determination
    /// ────────────────────
    /// After the spin the pointer (top, –90°) sees canvas angle:
    ///     pointerAngle = (360 – R mod 360) mod 360
    /// Slice i occupies [i·sliceDeg, (i+1)·sliceDeg), so:
    ///     winnerIndex = floor(pointerAngle / sliceDeg)
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
            AvaloniaProperty.Register<SpinnerWheelControl, bool>(nameof(UseWeightedSlices), true);

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

        // ── Static constructor – wire up AffectsRender ───────────────────────

        static SpinnerWheelControl()
        {
            AffectsRender<SpinnerWheelControl>(RotationAngleProperty, WinnerIndexProperty, UseWeightedSlicesProperty);
        }

        // ── Property change tracking ─────────────────────────────────────────

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SlicesProperty)
            {
                // Unsubscribe from the old collection and its items
                if (change.OldValue is ObservableCollection<WheelSliceViewModel> oldCol)
                {
                    oldCol.CollectionChanged -= OnCollectionChanged;
                    foreach (var s in oldCol) s.PropertyChanged -= OnSliceChanged;
                }

                // Subscribe to the new collection and its items
                if (change.NewValue is ObservableCollection<WheelSliceViewModel> newCol)
                {
                    newCol.CollectionChanged += OnCollectionChanged;
                    foreach (var s in newCol) s.PropertyChanged += OnSliceChanged;
                }

                InvalidateVisual();
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

            var size = Math.Min(bounds.Width, bounds.Height);
            var cx = bounds.Width / 2;
            var cy = bounds.Height / 2;
            var center = new Point(cx, cy);
            // Leave room for the pointer above the wheel
            var radius = size / 2 - 18;

            if (slices == null || slices.Count == 0)
            {
                DrawEmptyWheel(ctx, center, radius);
                DrawPointer(ctx, center, radius);
                return;
            }

            // Rotation matrix around the wheel centre
            var rad = RotationAngle * Math.PI / 180.0;
            var rotMatrix =
                Matrix.CreateTranslation(-cx, -cy) *
                Matrix.CreateRotation(rad) *
                Matrix.CreateTranslation(cx, cy);

            // ── Rotating layer (slices) ──────────────────────────────────────
            using (ctx.PushTransform(rotMatrix))
            {
                DrawSlices(ctx, center, radius, slices, UseWeightedSlices);
            }

            // ── Fixed layer (drawn on top, no rotation) ──────────────────────
            DrawOuterRing(ctx, center, radius);
            DrawPointer(ctx, center, radius);
            DrawCenterPin(ctx, center);
        }

        // ── Slice drawing ─────────────────────────────────────────────────────

        private void DrawSlices(DrawingContext ctx, Point center, double radius,
            IList<WheelSliceViewModel> slices, bool useWeightedSlices)
        {
            // Weighted mode: exclude inactive slices AND zero-weight slices (0-arc slices).
            // Unweighted mode: exclude only inactive slices; weight value is ignored.
            var active = new List<WheelSliceViewModel>();
            foreach (var s in slices)
                if (s.IsActive && (!useWeightedSlices || s.Weight > 0)) active.Add(s);

            if (active.Count == 0)
            {
                DrawEmptyWheel(ctx, center, radius);
                return;
            }

            double totalWeight = useWeightedSlices
                ? active.Sum(s => s.Weight)
                : active.Count;
            if (totalWeight <= 0) totalWeight = active.Count;

            var borderPen = new Pen(Brushes.White, 2);
            var winnerPen = new Pen(new SolidColorBrush(Color.Parse("#FFD700")), 4);  // gold highlight

            double startAngle = -Math.PI / 2;
            var n = active.Count;
            for (int i = 0; i < n; i++)
            {
                var slice    = active[i];
                double w     = useWeightedSlices ? slice.Weight : 1.0;
                var sliceRad = (w / totalWeight) * 2 * Math.PI;
                var endAngle = startAngle + sliceRad;
                var midAngle = startAngle + sliceRad / 2;

                // Fill colour
                Color color;
                try { color = Color.Parse(slice.ColorHex); }
                catch { color = Color.Parse("#808080"); }

                // Highlight winner — WinnerIndex is an index into the full Slices collection.
                bool isWinner = slices.IndexOf(slice) == WinnerIndex;
                IBrush fill = isWinner
                    ? new SolidColorBrush(Lighten(color, 0.2f))
                    : new SolidColorBrush(color);

                DrawPieSlice(ctx, center, radius, startAngle, endAngle, fill,
                    isWinner ? winnerPen : borderPen, n == 1);

                // Image (small thumbnail near the arc edge)
                if (slice.LoadedBitmap is Bitmap bmp)
                    DrawSliceImage(ctx, bmp, center, radius, midAngle, sliceRad);

                // Text label
                if (!string.IsNullOrEmpty(slice.Label))
                    DrawSliceLabel(ctx, slice.Label, center, radius, midAngle, n);

                startAngle = endAngle;
            }
        }

        private static void DrawPieSlice(DrawingContext ctx, Point center, double radius,
            double startAngle, double endAngle, IBrush fill, IPen pen, bool isFullCircle)
        {
            if (isFullCircle)
            {
                ctx.DrawEllipse(fill, pen, center, radius, radius);
                return;
            }

            var start = new Point(center.X + radius * Math.Cos(startAngle),
                                  center.Y + radius * Math.Sin(startAngle));
            var end   = new Point(center.X + radius * Math.Cos(endAngle),
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

            ctx.DrawGeometry(fill, pen, geo);
        }

        private static void DrawSliceImage(DrawingContext ctx, Bitmap bmp, Point center,
            double radius, double midAngle, double sliceRad)
        {
            // Place image at 55 % of the radius from centre
            var imgR = radius * 0.55;
            var ix = center.X + imgR * Math.Cos(midAngle);
            var iy = center.Y + imgR * Math.Sin(midAngle);

            // Size the image so it fits within ~¼ of the radius
            var size = Math.Min(radius * 0.28, 52.0);
            var dest = new Rect(ix - size / 2, iy - size / 2, size, size);

            ctx.DrawImage(bmp, dest);
        }

        private static void DrawSliceLabel(DrawingContext ctx, string label, Point center,
            double radius, double midAngle, int sliceCount)
        {
            // Scale font with slice count
            double fontSize = sliceCount <= 4 ? 14 : sliceCount <= 8 ? 12 : 10;

            // Place text halfway to the rim (or further if no image)
            var textR = radius * 0.7;
            var tx = center.X + textR * Math.Cos(midAngle);
            var ty = center.Y + textR * Math.Sin(midAngle);

            // Truncate long labels
            var display = label.Length > 16 ? label[..15] + "…" : label;

            var ft = new FormattedText(
                display,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                fontSize,
                Brushes.White);

            ctx.DrawText(ft, new Point(tx - ft.Width / 2, ty - ft.Height / 2));
        }

        // ── Fixed decorations ────────────────────────────────────────────────

        private static void DrawOuterRing(DrawingContext ctx, Point center, double radius)
        {
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#444444")), 3),
                center, radius, radius);
        }

        private static void DrawPointer(DrawingContext ctx, Point center, double radius)
        {
            // Downward-pointing triangle sitting just above the wheel rim
            const double hs = 11.0;  // half-width of base
            const double h = 20.0;   // height

            var tipY = center.Y - radius + 2;
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

        private static void DrawCenterPin(DrawingContext ctx, Point center)
        {
            ctx.DrawEllipse(Brushes.White, new Pen(Brushes.LightGray, 1.5), center, 14, 14);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#E74C3C")), null, center, 7, 7);
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
