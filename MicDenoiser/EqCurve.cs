using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MicDenoiser;

/// <summary>A logarithmic target-response display, redrawn only by UI settings/layout changes.</summary>
public sealed class EqCurve : FrameworkElement
{
    private double[] _curve = EqualizerResponse.Curve(new ProcessingSettings());
    public void SetSettings(ProcessingSettings settings) { _curve = EqualizerResponse.Curve(settings); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth - 56, height = ActualHeight - 32;
        if (width < 80 || height < 30) return;
        Brush ink = (Brush)FindResource("Muted"), line = (Brush)FindResource("Line"), accent = (Brush)FindResource("Accent");
        var grid = new Pen(line, 1); var curvePen = new Pen(accent, 2);
        double left = 40, top = 8;
        void Label(string text, double x, double y)
        {
            var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(x, y));
        }
        foreach (double db in new[] { -24d, -12, 0, 12, 24 })
        {
            double y = top + (24 - db) / 48 * height;
            dc.DrawLine(grid, new Point(left, y), new Point(left + width, y)); Label(db.ToString("+0;-0;0"), 0, y - 6);
        }
        foreach (double hz in new[] { 50d, 200, 1000, 4000, 16000 })
        {
            double x = left + Math.Log(hz / 40) / Math.Log(400) * width;
            dc.DrawLine(grid, new Point(x, top), new Point(x, top + height));
            Label(hz >= 1000 ? (hz / 1000).ToString("0") + "k" : hz.ToString("0"), x - 9, top + height + 7);
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (int i = 0; i < _curve.Length; i++)
            {
                var point = new Point(left + i / (double)(_curve.Length - 1) * width, top + (24 - Math.Clamp(_curve[i], -24, 24)) / 48 * height);
                if (i == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze(); dc.DrawGeometry(null, curvePen, geometry);
    }
}
