using System.Globalization;
using Microsoft.Maui.Graphics;

namespace VetCare.Views.Controls;

/// <summary>
/// Minimal SVG path-data parser (M L H V C S Q T A Z, absolute and relative)
/// that appends the shape to a <see cref="PathF"/>. Arcs are converted to cubic Béziers.
/// </summary>
internal static class SvgPathParser
{
    public static void Append(PathF path, string data)
    {
        var reader = new Reader(data);
        var current = PointF.Zero;
        var start = PointF.Zero;
        var lastControl = PointF.Zero;
        char command = '\0';
        char previous = '\0';

        while (reader.SkipSeparators())
        {
            if (reader.PeekIsCommand()) command = reader.ReadCommand();
            else if (command == '\0') throw new FormatException("Path data must start with a command.");

            bool relative = char.IsLower(command);
            var origin = relative ? current : PointF.Zero;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.MoveTo(p);
                    current = start = p;
                    command = relative ? 'l' : 'L'; // further pairs are implicit line-tos
                    previous = 'M';
                    continue;
                }
                case 'L':
                {
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.LineTo(p);
                    current = p;
                    break;
                }
                case 'H':
                {
                    var p = new PointF(reader.ReadNumber() + origin.X, current.Y);
                    path.LineTo(p);
                    current = p;
                    break;
                }
                case 'V':
                {
                    var p = new PointF(current.X, reader.ReadNumber() + origin.Y);
                    path.LineTo(p);
                    current = p;
                    break;
                }
                case 'C':
                {
                    var c1 = reader.ReadPoint().Offset(origin.X, origin.Y);
                    var c2 = reader.ReadPoint().Offset(origin.X, origin.Y);
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.CurveTo(c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'S':
                {
                    var c1 = "CS".Contains(char.ToUpperInvariant(previous)) ? Reflect(lastControl, current) : current;
                    var c2 = reader.ReadPoint().Offset(origin.X, origin.Y);
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.CurveTo(c1, c2, p);
                    lastControl = c2;
                    current = p;
                    break;
                }
                case 'Q':
                {
                    var q = reader.ReadPoint().Offset(origin.X, origin.Y);
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.QuadTo(q, p);
                    lastControl = q;
                    current = p;
                    break;
                }
                case 'T':
                {
                    var q = "QT".Contains(char.ToUpperInvariant(previous)) ? Reflect(lastControl, current) : current;
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    path.QuadTo(q, p);
                    lastControl = q;
                    current = p;
                    break;
                }
                case 'A':
                {
                    float rx = reader.ReadNumber();
                    float ry = reader.ReadNumber();
                    float rotation = reader.ReadNumber();
                    bool largeArc = reader.ReadFlag();
                    bool sweep = reader.ReadFlag();
                    var p = reader.ReadPoint().Offset(origin.X, origin.Y);
                    AppendArc(path, current, rx, ry, rotation, largeArc, sweep, p);
                    current = p;
                    break;
                }
                case 'Z':
                    path.Close();
                    current = start;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{command}'.");
            }

            previous = command;
        }
    }

    private static PointF Reflect(PointF control, PointF about) =>
        new(2 * about.X - control.X, 2 * about.Y - control.Y);

    /// <summary>SVG elliptical arc (endpoint form) converted to cubic Béziers.</summary>
    private static void AppendArc(PathF path, PointF p0, float rxIn, float ryIn, float rotationDeg,
        bool largeArc, bool sweep, PointF p1)
    {
        if (p0 == p1) return;
        double rx = Math.Abs(rxIn), ry = Math.Abs(ryIn);
        if (rx < 1e-6 || ry < 1e-6)
        {
            path.LineTo(p1);
            return;
        }

        double phi = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(phi), sin = Math.Sin(phi);

        double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
        double x1 = cos * dx + sin * dy;
        double y1 = -sin * dx + cos * dy;

        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = Math.Sqrt(Math.Max(0, num / den)) * (largeArc == sweep ? -1 : 1);
        double cxp = coef * rx * y1 / ry;
        double cyp = coef * -ry * x1 / rx;

        double cx = cos * cxp - sin * cyp + (p0.X + p1.X) / 2.0;
        double cy = sin * cxp + cos * cyp + (p0.Y + p1.Y) / 2.0;

        double theta1 = VectorAngle(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
        double delta = VectorAngle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
        double step = delta / segments;
        double t = 4.0 / 3.0 * Math.Tan(step / 4);

        PointF Map(double u, double v) => new(
            (float)(cx + rx * u * cos - ry * v * sin),
            (float)(cy + rx * u * sin + ry * v * cos));

        for (int i = 0; i < segments; i++)
        {
            double a1 = theta1 + i * step;
            double a2 = a1 + step;
            double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);
            double cos2 = Math.Cos(a2), sin2 = Math.Sin(a2);

            var c1 = Map(cos1 - t * sin1, sin1 + t * cos1);
            var c2 = Map(cos2 + t * sin2, sin2 - t * cos2);
            var to = i == segments - 1 ? p1 : Map(cos2, sin2);
            path.CurveTo(c1, c2, to);
        }
    }

    private static double VectorAngle(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double len = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
        double angle = Math.Acos(Math.Max(-1, Math.Min(1, dot / len)));
        return ux * vy - uy * vx < 0 ? -angle : angle;
    }

    private sealed class Reader
    {
        private readonly string _s;
        private int _i;

        public Reader(string s) => _s = s ?? string.Empty;

        /// <summary>Skips whitespace and commas. Returns false at the end of input.</summary>
        public bool SkipSeparators()
        {
            while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',')) _i++;
            return _i < _s.Length;
        }

        public bool PeekIsCommand()
        {
            char c = _s[_i];
            return char.IsLetter(c) && c != 'e' && c != 'E';
        }

        public char ReadCommand() => _s[_i++];

        public PointF ReadPoint()
        {
            float x = ReadNumber();
            float y = ReadNumber();
            return new PointF(x, y);
        }

        public bool ReadFlag()
        {
            SkipSeparators();
            char c = _s[_i++];
            if (c != '0' && c != '1') throw new FormatException("Invalid arc flag.");
            return c == '1';
        }

        public float ReadNumber()
        {
            SkipSeparators();
            int start = _i;
            if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;

            bool seenDot = false;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsDigit(c)) { _i++; continue; }
                if (c == '.' && !seenDot) { seenDot = true; _i++; continue; }
                break;
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }

            if (_i == start) throw new FormatException($"Expected a number at position {start}.");
            return float.Parse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
