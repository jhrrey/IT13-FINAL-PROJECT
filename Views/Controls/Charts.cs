using VetCare.Helpers;

namespace VetCare.Views.Controls;

/// <summary>One point of a chart: a short label ("Nov 3", "Mon", "Dr. Lim") and its values.</summary>
public sealed record ChartPoint(string Label, params double[] Values);

/// <summary>
/// Simple flat charts for the dashboard (navy, with cyan / orange accents and light gridlines; no 3D, no gradients),
/// drawn with Microsoft.Maui.Graphics — no chart package. Tap a point or bar to see its exact value.
/// </summary>
public abstract class ChartView : GraphicsView, IDrawable
{
    protected static readonly Color Navy = Color.FromArgb("#0A2E4F");
    protected static readonly Color Cyan = Color.FromArgb("#5DCFD6");
    protected static readonly Color CyanFill = Color.FromArgb("#D3EEF8");
    protected static readonly Color Orange = Color.FromArgb("#D4421C");
    protected static readonly Color Grid = Color.FromArgb("#E6EEF2");
    protected static readonly Color LightGray = Color.FromArgb("#C9D3DA");
    protected static readonly Color Text = Color.FromArgb("#5B6B7B");

    private IReadOnlyList<ChartPoint> _points = Array.Empty<ChartPoint>();

    protected ChartView()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        StartInteraction += (s, e) =>
        {
            if (e.Touches.Length == 0) return;
            Selected = HitTest(e.Touches[0]);
            Invalidate();
        };
    }

    public IReadOnlyList<ChartPoint> Points
    {
        get => _points;
        set
        {
            _points = value ?? Array.Empty<ChartPoint>();
            Selected = -1;
            Invalidate();
        }
    }

    /// <summary>Formats a value for axis labels and the tooltip (e.g. pesos).</summary>
    public Func<double, string> Format { get; set; } = v => v.ToString("N0");

    protected int Selected { get; private set; } = -1;

    public abstract void Draw(ICanvas canvas, RectF dirtyRect);

    protected abstract int HitTest(PointF point);

    /// <summary>A rounded navy label with white text above <paramref name="anchor"/> (kept inside the chart).</summary>
    protected static void Tooltip(ICanvas canvas, RectF bounds, PointF anchor, string text)
    {
        canvas.FontSize = 12;
        var size = canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.DefaultBold, 12);
        float w = size.Width + 16, h = 24;
        float x = Math.Clamp(anchor.X - w / 2, bounds.Left, bounds.Right - w);
        float y = Math.Max(bounds.Top, anchor.Y - h - 8);
        canvas.FillColor = Navy;
        canvas.FillRoundedRectangle(x, y, w, h, 6);
        canvas.FontColor = Colors.White;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(text, x, y, w, h, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
    }

    /// <summary>A "nice" top for the y axis (1, 2, 2.5, 5 × 10^n).</summary>
    protected static double NiceMax(double max)
    {
        if (max <= 0) return 1;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (double step in new[] { 1, 2, 2.5, 5, 10 })
            if (step * magnitude >= max) return step * magnitude;
        return 10 * magnitude;
    }
}

/// <summary>Line with a light cyan area under it (Income – last 30 days).</summary>
public sealed class LineChartView : ChartView
{
    private RectF _plot;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 11;
        canvas.FontColor = Text;
        double max = NiceMax(points.Count == 0 ? 0 : points.Max(p => p.Values[0]));
        string topLabel = Format(max);
        float left = Math.Max(44, canvas.GetStringSize(topLabel, Microsoft.Maui.Graphics.Font.Default, 11).Width + 10);
        _plot = new RectF(rect.Left + left, rect.Top + 8, rect.Width - left - 8, rect.Height - 8 - 24);

        // Gridlines and y labels (0, ½, max).
        canvas.StrokeColor = Grid;
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 2; i++)
        {
            float y = _plot.Bottom - _plot.Height * i / 2f;
            canvas.DrawLine(_plot.Left, y, _plot.Right, y);
            canvas.DrawString(Format(max * i / 2), rect.Left, y - 8, left - 8, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
        if (points.Count < 2) return;

        PointF At(int i) => new(_plot.Left + _plot.Width * i / (points.Count - 1),
            (float)(_plot.Bottom - _plot.Height * points[i].Values[0] / max));

        var area = new PathF();
        area.MoveTo(_plot.Left, _plot.Bottom);
        for (int i = 0; i < points.Count; i++) area.LineTo(At(i));
        area.LineTo(_plot.Right, _plot.Bottom);
        area.Close();
        canvas.FillColor = CyanFill;
        canvas.FillPath(area);

        var line = new PathF();
        line.MoveTo(At(0));
        for (int i = 1; i < points.Count; i++) line.LineTo(At(i));
        canvas.StrokeColor = Navy;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(line);

        // X labels: first, middle and last day.
        foreach (int i in new[] { 0, points.Count / 2, points.Count - 1 })
        {
            var p = At(i);
            var align = i == 0 ? HorizontalAlignment.Left : i == points.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            float x = align == HorizontalAlignment.Left ? p.X : align == HorizontalAlignment.Right ? p.X - 80 : p.X - 40;
            canvas.DrawString(points[i].Label, x, _plot.Bottom + 6, 80, 16, align, VerticalAlignment.Top);
        }

        if (Selected >= 0 && Selected < points.Count)
        {
            var p = At(Selected);
            canvas.FillColor = Navy;
            canvas.FillCircle(p, 4.5f);
            Tooltip(canvas, rect, p, points[Selected].Label + ": " + Format(points[Selected].Values[0]));
        }
    }

    protected override int HitTest(PointF point)
    {
        if (Points.Count < 2 || _plot.Width <= 0) return -1;
        int i = (int)Math.Round((point.X - _plot.Left) / _plot.Width * (Points.Count - 1));
        return Math.Clamp(i, 0, Points.Count - 1);
    }
}

/// <summary>Vertical stacked bars per day (Completed navy, Cancelled light gray, No-show orange) with a legend under it.</summary>
public sealed class StackedBarChartView : ChartView
{
    /// <summary>The stacked series (bottom first) and their colours; the default is the appointments chart.</summary>
    public (string Name, Color Color)[] Series { get; set; } =
    {
        ("Completed", Color.FromArgb("#0A2E4F")), ("Cancelled", Color.FromArgb("#C9D3DA")), ("No-show", Color.FromArgb("#D4421C"))
    };

    private RectF _plot;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 11;
        canvas.FontColor = Text;
        double max = NiceMax(points.Count == 0 ? 0 : points.Max(p => p.Values.Sum()));
        float axis = Math.Max(28, canvas.GetStringSize(Format(max), Microsoft.Maui.Graphics.Font.Default, 11).Width + 8);
        bool legend = Series.Length > 1;
        _plot = new RectF(rect.Left + axis, rect.Top + 8, rect.Width - axis - 8, rect.Height - 8 - (legend ? 48 : 26));

        canvas.StrokeColor = Grid;
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 2; i++)
        {
            float y = _plot.Bottom - _plot.Height * i / 2f;
            canvas.DrawLine(_plot.Left, y, _plot.Right, y);
            canvas.DrawString(Format(max * i / 2), rect.Left, y - 8, axis - 6, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        if (points.Count > 0)
        {
            float slot = _plot.Width / points.Count, bar = Math.Min(28, slot * 0.55f);
            // Show every n-th label so labels never overlap (or wrap) when the bars are narrow.
            float labelW = points.Max(p => canvas.GetStringSize(p.Label ?? "", Microsoft.Maui.Graphics.Font.Default, 11).Width) + 10;
            int step = Math.Max(1, (int)Math.Ceiling(labelW / slot));
            for (int i = 0; i < points.Count; i++)
            {
                float x = _plot.Left + slot * i + (slot - bar) / 2, bottom = _plot.Bottom;
                for (int s = 0; s < Series.Length && s < points[i].Values.Length; s++)
                {
                    float h = (float)(_plot.Height * points[i].Values[s] / max);
                    if (h <= 0) continue;
                    canvas.FillColor = Series[s].Color;
                    canvas.FillRectangle(x, bottom - h, bar, h);
                    bottom -= h;
                }
                if (i % step == 0)
                {
                    float cx = _plot.Left + slot * (i + 0.5f);
                    float lx0 = Math.Clamp(cx - labelW / 2, rect.Left, rect.Right - labelW);
                    canvas.DrawString(points[i].Label, lx0, _plot.Bottom + 6, labelW, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
                }
            }
        }

        // Legend (only for more than one series).
        float lx = _plot.Left, ly = rect.Bottom - 14;
        foreach (var (name, color) in legend ? Series : Array.Empty<(string, Color)>())
        {
            canvas.FillColor = color;
            canvas.FillRoundedRectangle(lx, ly - 5, 10, 10, 2);
            canvas.DrawString(name, lx + 14, ly - 8, 80, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
            lx += 14 + canvas.GetStringSize(name, Microsoft.Maui.Graphics.Font.Default, 11).Width + 18;
        }

        if (Selected >= 0 && Selected < points.Count)
        {
            float slot = _plot.Width / points.Count;
            var p = points[Selected];
            string text = p.Label + ": " + string.Join(", ", Series.Select((s, i) => Format(p.Values.ElementAtOrDefault(i)) + " " + s.Name.ToLowerInvariant()));
            float top = (float)(_plot.Bottom - _plot.Height * p.Values.Sum() / max);
            Tooltip(canvas, rect, new PointF(_plot.Left + slot * (Selected + 0.5f), top), text);
        }
    }

    protected override int HitTest(PointF point)
    {
        if (Points.Count == 0 || _plot.Width <= 0) return -1;
        int i = (int)((point.X - _plot.Left) / (_plot.Width / Points.Count));
        return i >= 0 && i < Points.Count ? i : -1;
    }
}

/// <summary>Horizontal navy bars with the number at the end (Visits per vet today).</summary>
public sealed class HorizontalBarChartView : ChartView
{
    public const double RowHeight = 34;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 12.5f;
        double max = Math.Max(1, points.Count == 0 ? 0 : points.Max(p => p.Values[0]));
        float labelWidth = Math.Min(rect.Width * 0.4f, 150);
        float valueWidth = points.Count == 0 ? 26 : points.Max(p => canvas.GetStringSize(Format(p.Values[0]), Microsoft.Maui.Graphics.Font.DefaultBold, 12.5f).Width) + 8;
        for (int i = 0; i < points.Count; i++)
        {
            float y = rect.Top + (float)RowHeight * i;
            canvas.FontColor = Navy;
            canvas.DrawString(points[i].Label, rect.Left, y, labelWidth - 8, (float)RowHeight, HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds);
            float available = rect.Width - labelWidth - valueWidth - 4;
            float w = (float)(available * points[i].Values[0] / max);
            canvas.FillColor = Grid;
            canvas.FillRoundedRectangle(rect.Left + labelWidth, y + 9, available, (float)RowHeight - 18, 4);
            if (w > 0)
            {
                canvas.FillColor = Navy;
                canvas.FillRoundedRectangle(rect.Left + labelWidth, y + 9, Math.Max(w, 6), (float)RowHeight - 18, 4);
            }
            canvas.FontColor = Navy;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(Format(points[i].Values[0]), rect.Right - valueWidth, y, valueWidth, (float)RowHeight, HorizontalAlignment.Right, VerticalAlignment.Center);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        }
    }

    protected override int HitTest(PointF point) => -1;
}

/// <summary>A donut with the total in the centre (the legend with amounts and percentages is drawn by the page).</summary>
public sealed class DonutChartView : ChartView
{
    /// <summary>Slice colours in order: navy, cyan, orange, amber, gray (then repeated).</summary>
    public static readonly Color[] Palette =
    {
        Color.FromArgb("#0A2E4F"), Color.FromArgb("#5DCFD6"), Color.FromArgb("#D4421C"), Color.FromArgb("#E8A33D"), Color.FromArgb("#A9B6C0")
    };

    public static Color ColorAt(int i) => Palette[i % Palette.Length];

    private PointF _center;
    private float _outer, _inner;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        double total = points.Sum(p => Math.Max(0, p.Values[0]));
        _outer = Math.Min(rect.Width, rect.Height) / 2 - 4;
        _inner = _outer * 0.62f;
        _center = new PointF(rect.Center.X, rect.Center.Y);
        if (total <= 0)
        {
            canvas.StrokeColor = Grid;
            canvas.StrokeSize = _outer - _inner;
            canvas.DrawCircle(_center, (_outer + _inner) / 2);
        }
        else
        {
            float start = -90;
            for (int i = 0; i < points.Count; i++)
            {
                float sweep = (float)(360 * Math.Max(0, points[i].Values[0]) / total);
                if (sweep <= 0) continue;
                float r = Selected == i ? _outer + 3 : _outer;
                if (sweep >= 359.9f)
                {
                    // One slice is the whole ring; a 360° arc would collapse to nothing.
                    canvas.StrokeColor = ColorAt(i);
                    canvas.StrokeSize = r - _inner;
                    canvas.DrawCircle(_center, (r + _inner) / 2);
                    break;
                }
                var path = new PathF();
                path.AddArc(_center.X - r, _center.Y - r, _center.X + r, _center.Y + r, -start, -(start + sweep), true);
                path.AddArc(_center.X - _inner, _center.Y - _inner, _center.X + _inner, _center.Y + _inner, -(start + sweep), -start, false);
                path.Close();
                canvas.FillColor = ColorAt(i);
                canvas.FillPath(path);
                start += sweep;
            }
        }

        // Total (or the tapped slice) in the centre.
        string top = Selected >= 0 && Selected < points.Count ? points[Selected].Label : "Total";
        string value = Format(Selected >= 0 && Selected < points.Count ? points[Selected].Values[0] : total);
        canvas.FontColor = Text;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 11.5f;
        canvas.DrawString(top, _center.X - _inner, _center.Y - 20, _inner * 2, 16, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.FontColor = Navy;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = value.Length > 10 ? 13 : 16;
        canvas.DrawString(value, _center.X - _inner, _center.Y - 2, _inner * 2, 22, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
    }

    protected override int HitTest(PointF point)
    {
        double dx = point.X - _center.X, dy = point.Y - _center.Y, distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < _inner || distance > _outer + 4) return -1;
        double angle = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
        double total = Points.Sum(p => Math.Max(0, p.Values[0])), sum = 0;
        if (total <= 0) return -1;
        for (int i = 0; i < Points.Count; i++)
        {
            sum += 360 * Math.Max(0, Points[i].Values[0]) / total;
            if (angle <= sum) return i;
        }
        return -1;
    }
}

/// <summary>
/// Bars with a line over them (Clinic growth): Values[0] are the cyan bars (new clinics), Values[1] the navy line
/// (active clinics). Legend under the chart.
/// </summary>
public sealed class BarLineChartView : ChartView
{
    public string BarName { get; set; } = "New";
    public string LineName { get; set; } = "Active";

    private RectF _plot;

    public override void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 11;
        canvas.FontColor = Text;
        double max = NiceMax(points.Count == 0 ? 0 : points.Max(p => Math.Max(p.Values[0], p.Values.Length > 1 ? p.Values[1] : 0)));
        float axis = Math.Max(24, canvas.GetStringSize(Format(max), Microsoft.Maui.Graphics.Font.Default, 11).Width + 8);
        _plot = new RectF(rect.Left + axis, rect.Top + 8, rect.Width - axis - 8, rect.Height - 8 - 48);

        canvas.StrokeColor = Grid;
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 2; i++)
        {
            float y = _plot.Bottom - _plot.Height * i / 2f;
            canvas.DrawLine(_plot.Left, y, _plot.Right, y);
            canvas.DrawString(Format(max * i / 2), rect.Left, y - 8, axis - 6, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        if (points.Count > 0)
        {
            float slot = _plot.Width / points.Count, bar = Math.Min(26, slot * 0.5f);
            var line = new PathF();
            for (int i = 0; i < points.Count; i++)
            {
                float cx = _plot.Left + slot * (i + 0.5f);
                float h = (float)(_plot.Height * points[i].Values[0] / max);
                if (h > 0)
                {
                    canvas.FillColor = Cyan;
                    canvas.FillRoundedRectangle(cx - bar / 2, _plot.Bottom - h, bar, h, 3);
                }
                canvas.FontColor = Text;
                canvas.DrawString(points[i].Label, cx - slot / 2, _plot.Bottom + 6, slot, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
                if (points[i].Values.Length > 1)
                {
                    var p = new PointF(cx, (float)(_plot.Bottom - _plot.Height * points[i].Values[1] / max));
                    if (i == 0) line.MoveTo(p); else line.LineTo(p);
                }
            }
            canvas.StrokeColor = Navy;
            canvas.StrokeSize = 2.5f;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(line);
            canvas.FillColor = Navy;
            for (int i = 0; i < points.Count; i++)
                if (points[i].Values.Length > 1)
                    canvas.FillCircle(_plot.Left + slot * (i + 0.5f), (float)(_plot.Bottom - _plot.Height * points[i].Values[1] / max), 3.5f);
        }

        // Legend
        float lx = _plot.Left, ly = rect.Bottom - 12;
        canvas.FillColor = Cyan;
        canvas.FillRoundedRectangle(lx, ly - 5, 10, 10, 2);
        canvas.FontColor = Text;
        canvas.DrawString(BarName, lx + 14, ly - 8, 120, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        lx += 14 + canvas.GetStringSize(BarName, Microsoft.Maui.Graphics.Font.Default, 11).Width + 18;
        canvas.StrokeColor = Navy;
        canvas.StrokeSize = 2.5f;
        canvas.DrawLine(lx, ly, lx + 12, ly);
        canvas.DrawString(LineName, lx + 16, ly - 8, 120, 16, HorizontalAlignment.Left, VerticalAlignment.Center);

        if (Selected >= 0 && Selected < points.Count)
        {
            float slot = _plot.Width / points.Count;
            var p = points[Selected];
            string text = p.Label + ": " + Format(p.Values[0]) + " " + BarName.ToLowerInvariant() +
                          (p.Values.Length > 1 ? " · " + Format(p.Values[1]) + " " + LineName.ToLowerInvariant() : string.Empty);
            Tooltip(canvas, rect, new PointF(_plot.Left + slot * (Selected + 0.5f), _plot.Top + 20), text);
        }
    }

    protected override int HitTest(PointF point)
    {
        if (Points.Count == 0 || _plot.Width <= 0) return -1;
        int i = (int)((point.X - _plot.Left) / (_plot.Width / Points.Count));
        return i >= 0 && i < Points.Count ? i : -1;
    }
}
