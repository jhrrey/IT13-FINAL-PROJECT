using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PdfColors = QuestPDF.Helpers.Colors;
using VetCare.Models.Entities;

namespace VetCare.Helpers;

/// <summary>How report values read on screen and in exports (₱ with 2 decimals, local dates — rule 9).</summary>
public static class ReportFormat
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Cell(object value, ColumnKind kind) => value switch
    {
        null => string.Empty,
        string s => s,
        DateTime d when kind == ColumnKind.DateTime => d.ToString("MMM d, yyyy h:mm tt", Invariant),
        DateTime d => d.ToString("MMM d, yyyy", Invariant),
        decimal m when kind == ColumnKind.Money => CurrencyHelper.Peso(m),
        decimal m => m.ToString("N2", Invariant),
        double p when kind == ColumnKind.Percent => p.ToString("0.#", Invariant) + "%",
        double p => p.ToString("N1", Invariant),
        int n when n < 0 && kind == ColumnKind.Number => "−" + Math.Abs(n).ToString("N0", Invariant),
        int n => n.ToString("N0", Invariant),
        _ => value.ToString()
    };

    /// <summary>Pill colours for status-like cells (appointment, stock, bill, stock-log type, guest result, due status).</summary>
    public static (string Background, string Text) PillColors(string text) => text switch
    {
        "Completed" or "Arrived & registered" or "In stock" or "Paid" or "Stock In" => ("Mint", "CompletedText"),
        "Checked In" or "Vaccine Used" => ("Cyan", "Navy"),
        "In Consultation" or "Low stock" or "Partial" or "Due soon" or "Adjustment" or "Waiting" => ("WarningBox", "ConsultText"),
        "No-show" or "Overdue" or "Expiring soon" or "Unpaid" => ("OrangeSoft", "Orange"),
        "Out of stock" => ("DangerSoft", "DangerHover"),
        "Dispensed" or "Scheduled" => ("PaleBlue", "Navy"),
        _ => ("LightGray", "BodyText")
    };
}

/// <summary>A grayscale SVG of a report chart (bars, stacked bars, horizontal bars, line, donut) for print and PDF.</summary>
public static class ReportChartSvg
{
    private static readonly string[] Grays = { "#1F1F1F", "#6E6E6E", "#A6A6A6", "#C8C8C8", "#E2E2E2" };
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static string F(double v) => v.ToString("0.##", Invariant);

    private static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

    public static string Build(ReportChart chart, int width = 720, int height = 260)
    {
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Arial, Helvetica, sans-serif\" font-size=\"10\">");
        if (chart == null || chart.IsEmpty)
            svg.Append($"<text x=\"{width / 2}\" y=\"{height / 2}\" text-anchor=\"middle\" fill=\"#555\">No data for this period</text>");
        else if (chart.Kind == ChartKind.Donut) Donut(svg, chart, width, height);
        else if (chart.Kind == ChartKind.HorizontalBar) HorizontalBars(svg, chart, width, height);
        else Bars(svg, chart, width, height);
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Value(double v, bool money) => money ? "₱" + v.ToString("N2", Invariant) : v.ToString("N0", Invariant);

    private static void Bars(StringBuilder svg, ReportChart chart, int width, int height)
    {
        int n = chart.Labels.Count;
        double max = Enumerable.Range(0, n).Max(i => chart.Series.Sum(s => s.Values[i]));
        max = max <= 0 ? 1 : max;
        double left = 60, top = 10, bottom = height - (chart.Series.Count > 1 ? 44 : 26), plotW = width - left - 10, plotH = bottom - top;
        for (int g = 0; g <= 2; g++)
        {
            double y = bottom - plotH * g / 2;
            svg.Append($"<line x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(width - 10)}\" y2=\"{F(y)}\" stroke=\"#DDD\"/>");
            svg.Append($"<text x=\"{F(left - 6)}\" y=\"{F(y + 3)}\" text-anchor=\"end\" fill=\"#444\">{E(Value(max * g / 2, chart.Money))}</text>");
        }
        double slot = plotW / n, bar = Math.Min(26, slot * 0.6);
        int step = n <= 16 ? 1 : (int)Math.Ceiling(n / 8.0);
        for (int i = 0; i < n; i++)
        {
            double x = left + slot * i + (slot - bar) / 2, y = bottom;
            for (int s = 0; s < chart.Series.Count; s++)
            {
                double h = plotH * chart.Series[s].Values[i] / max;
                if (h <= 0) continue;
                svg.Append($"<rect x=\"{F(x)}\" y=\"{F(y - h)}\" width=\"{F(bar)}\" height=\"{F(h)}\" fill=\"{Grays[s % Grays.Length]}\"/>");
                y -= h;
            }
            if (i % step == 0)
                svg.Append($"<text x=\"{F(left + slot * (i + 0.5))}\" y=\"{F(bottom + 14)}\" text-anchor=\"middle\" fill=\"#444\">{E(chart.Labels[i])}</text>");
        }
        if (chart.Series.Count > 1)
        {
            double lx = left;
            for (int s = 0; s < chart.Series.Count; s++)
            {
                svg.Append($"<rect x=\"{F(lx)}\" y=\"{height - 14}\" width=\"10\" height=\"10\" fill=\"{Grays[s % Grays.Length]}\" stroke=\"#555\" stroke-width=\"0.5\"/>");
                svg.Append($"<text x=\"{F(lx + 14)}\" y=\"{height - 5}\" fill=\"#222\">{E(chart.Series[s].Name)}</text>");
                lx += 24 + chart.Series[s].Name.Length * 6;
            }
        }
    }

    private static void HorizontalBars(StringBuilder svg, ReportChart chart, int width, int height)
    {
        int n = Math.Min(chart.Labels.Count, 10);
        double max = chart.Series[0].Values.Take(n).DefaultIfEmpty(0).Max();
        max = max <= 0 ? 1 : max;
        double row = Math.Min(24, (height - 10.0) / n), labelW = 170, valueW = 90, barW = width - labelW - valueW - 10;
        for (int i = 0; i < n; i++)
        {
            double y = 5 + row * i, v = chart.Series[0].Values[i];
            string label = chart.Labels[i].Length > 28 ? chart.Labels[i][..27] + "…" : chart.Labels[i];
            svg.Append($"<text x=\"0\" y=\"{F(y + row / 2 + 3)}\" fill=\"#222\">{E(label)}</text>");
            svg.Append($"<rect x=\"{F(labelW)}\" y=\"{F(y + row * 0.2)}\" width=\"{F(Math.Max(2, barW * v / max))}\" height=\"{F(row * 0.6)}\" fill=\"#1F1F1F\"/>");
            svg.Append($"<text x=\"{width - 2}\" y=\"{F(y + row / 2 + 3)}\" text-anchor=\"end\" fill=\"#222\" font-weight=\"bold\">{E(Value(v, chart.Money))}</text>");
        }
    }

    private static void Donut(StringBuilder svg, ReportChart chart, int width, int height)
    {
        var values = chart.Series[0].Values;
        double total = values.Sum(), r = Math.Min(height / 2.0 - 10, 100), ri = r * 0.6, cx = r + 10, cy = height / 2.0, angle = -Math.PI / 2;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;
            double sweep = 2 * Math.PI * values[i] / total, end = angle + sweep;
            if (sweep >= 2 * Math.PI - 0.0001) end = angle + 2 * Math.PI - 0.0001;
            int large = sweep > Math.PI ? 1 : 0;
            string Pt(double rad, double a) => F(cx + rad * Math.Cos(a)) + " " + F(cy + rad * Math.Sin(a));
            svg.Append($"<path d=\"M {Pt(r, angle)} A {F(r)} {F(r)} 0 {large} 1 {Pt(r, end)} L {Pt(ri, end)} A {F(ri)} {F(ri)} 0 {large} 0 {Pt(ri, angle)} Z\" fill=\"{Grays[i % Grays.Length]}\" stroke=\"#FFF\" stroke-width=\"1\"/>");
            angle += sweep;
        }
        svg.Append($"<text x=\"{F(cx)}\" y=\"{F(cy - 4)}\" text-anchor=\"middle\" fill=\"#444\">Total</text>");
        svg.Append($"<text x=\"{F(cx)}\" y=\"{F(cy + 12)}\" text-anchor=\"middle\" font-weight=\"bold\" font-size=\"12\" fill=\"#111\">{E(Value(total, chart.Money))}</text>");
        double ly = 20, lx = cx + r + 30;
        for (int i = 0; i < chart.Labels.Count; i++)
        {
            if (values[i] <= 0) continue;
            svg.Append($"<rect x=\"{F(lx)}\" y=\"{F(ly - 9)}\" width=\"10\" height=\"10\" fill=\"{Grays[i % Grays.Length]}\" stroke=\"#555\" stroke-width=\"0.5\"/>");
            svg.Append($"<text x=\"{F(lx + 16)}\" y=\"{F(ly)}\" fill=\"#222\">{E(chart.Labels[i])} — {E(Value(values[i], chart.Money))} ({(values[i] * 100 / total).ToString("0.#", Invariant)}%)</text>");
            ly += 18;
        }
    }
}

/// <summary>Report exports (section 3.C): A4 PDF with QuestPDF, .xlsx with ClosedXML, saved under Documents\VetCare Reports.</summary>
public static class ExportHelper
{
    public static string ExportFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VetCare Reports");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>"Daily-collection_2026-11-01_2026-11-18".</summary>
    public static string FileName(ReportResult r) =>
        string.Concat(r.Definition.Name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-').Replace("&", "and") +
        "_" + r.Filters.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "_" + r.Filters.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string RangeText(ReportResult r) => r.Definition.Snapshot
        ? "As of " + DateTime.Today.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
        : DateRangeHelper.Label(r.Filters.From, r.Filters.To);

    private static byte[] Logo()
    {
        using var stream = typeof(ExportHelper).Assembly.GetManifestResourceStream("VetCare.logo.png");
        if (stream == null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // PDF (QuestPDF) ------------------------------------------------------------------------------------------------------

    public static void WritePdf(string path, ReportResult r, Clinic clinic, string generatedBy) =>
        BuildPdf(r, clinic, generatedBy).GeneratePdf(path);

    /// <summary>The A4 report document (QuestPDF).</summary>
    public static IDocument BuildPdf(ReportResult r, Clinic clinic, string generatedBy)
    {
        var logo = Logo();
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(PdfColors.Black));

            page.Header().PaddingBottom(10).BorderBottom(1).BorderColor(PdfColors.Grey.Medium).Row(row =>
            {
                if (logo != null) row.ConstantItem(110).Height(40).AlignMiddle().Image(logo).FitArea();
                row.RelativeItem().PaddingLeft(10).Column(c =>
                {
                    c.Item().Text(clinic?.ClinicName ?? "VetCare Clinic").Bold().FontSize(12);
                    if (!string.IsNullOrWhiteSpace(clinic?.Address)) c.Item().Text(clinic.Address);
                    if (!string.IsNullOrWhiteSpace(clinic?.ContactNo)) c.Item().Text("Tel. " + clinic.ContactNo);
                });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(r.Definition.Name + " report").Bold().FontSize(13);
                    c.Item().AlignRight().Text(RangeText(r));
                    c.Item().AlignRight().Text(generatedBy).FontSize(8).FontColor(PdfColors.Grey.Darken2);
                });
            });

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(12);
                if (!string.IsNullOrEmpty(r.Note)) col.Item().Text(r.Note).Italic();

                col.Item().Row(row =>
                {
                    foreach (var card in r.Cards)
                        row.RelativeItem().Padding(3).Border(1).BorderColor(PdfColors.Grey.Lighten1).Padding(8).Column(c =>
                        {
                            c.Item().Text(card.Label).FontSize(8).FontColor(PdfColors.Grey.Darken2);
                            c.Item().Text(card.Value).Bold().FontSize(13);
                            if (!string.IsNullOrEmpty(card.Sub)) c.Item().Text(card.Sub).FontSize(7.5f).FontColor(PdfColors.Grey.Darken2);
                        });
                });

                if (!r.IsEmpty)
                {
                    col.Item().Text(r.Chart.Title).Bold().FontSize(10);
                    col.Item().Height(170).Svg(ReportChartSvg.Build(r.Chart, 720, 240)).FitArea();
                    if (r.SideChart != null)
                    {
                        col.Item().Text(r.SideChart.Title).Bold().FontSize(10);
                        col.Item().Height(120).Svg(ReportChartSvg.Build(r.SideChart, 720, 170)).FitArea();
                    }
                }

                col.Item().Text(r.TableTitle).Bold().FontSize(10);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { foreach (var column in r.Columns) c.RelativeColumn((float)column.Width); });
                    table.Header(h =>
                    {
                        foreach (var column in r.Columns)
                        {
                            var cell = h.Cell().Background(PdfColors.Grey.Lighten3).BorderBottom(1).BorderColor(PdfColors.Grey.Medium).Padding(4);
                            (column.RightAligned ? cell.AlignRight() : cell).Text(column.Title).Bold().FontSize(8);
                        }
                    });
                    void Line(object[] values, bool total)
                    {
                        for (int i = 0; i < r.Columns.Count; i++)
                        {
                            var cell = table.Cell().BorderBottom(0.5f).BorderColor(PdfColors.Grey.Lighten2).Padding(4);
                            if (total) cell = cell.Background(PdfColors.Grey.Lighten3);
                            var text = (r.Columns[i].RightAligned ? cell.AlignRight() : cell).Text(i < values.Length ? ReportFormat.Cell(values[i], r.Columns[i].Kind) : string.Empty).FontSize(8);
                            if (total) text.Bold();
                        }
                    }
                    foreach (var values in r.Rows) Line(values, false);
                    if (r.Total != null) Line(r.Total, true);
                });
                if (r.IsEmpty) col.Item().Text("No data for this period.").Italic();
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text("VetCare – Veterinary Clinic Management System").FontSize(8).FontColor(PdfColors.Grey.Darken2);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(PdfColors.Grey.Darken2));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }));
    }

    // Excel (ClosedXML) -------------------------------------------------------------------------------------------------------

    public static void WriteExcel(string path, ReportResult r, Clinic clinic, string generatedBy)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        int row = 1;
        sheet.Cell(row, 1).Value = r.Definition.Name + " report";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        sheet.Cell(++row, 1).Value = (clinic?.ClinicName ?? "VetCare Clinic") + " · " + RangeText(r);
        sheet.Cell(++row, 1).Value = generatedBy;
        if (!string.IsNullOrEmpty(r.Note)) sheet.Cell(++row, 1).Value = r.Note;
        row++;

        foreach (var card in r.Cards)
        {
            sheet.Cell(++row, 1).Value = card.Label;
            sheet.Cell(row, 2).Value = card.Value;
            sheet.Cell(row, 2).Style.Font.Bold = true;
            if (!string.IsNullOrEmpty(card.Sub)) sheet.Cell(row, 3).Value = card.Sub;
        }
        row += 2;

        int header = row;
        for (int c = 0; c < r.Columns.Count; c++)
        {
            var cell = sheet.Cell(header, c + 1);
            cell.Value = r.Columns[c].Title;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3EEF8");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            if (r.Columns[c].RightAligned) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
        void Write(object[] values, bool total)
        {
            row++;
            for (int c = 0; c < r.Columns.Count && c < values.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                var kind = r.Columns[c].Kind;
                switch (values[c])
                {
                    case null: break;
                    case decimal m:
                        cell.Value = (double)m;
                        cell.Style.NumberFormat.Format = kind == ColumnKind.Money ? "\"₱\"#,##0.00" : "#,##0.00";
                        break;
                    case int n:
                        cell.Value = n;
                        cell.Style.NumberFormat.Format = "#,##0";
                        break;
                    case double d:
                        cell.Value = kind == ColumnKind.Percent ? d / 100 : d;
                        cell.Style.NumberFormat.Format = kind == ColumnKind.Percent ? "0.0%" : "#,##0.0";
                        break;
                    case DateTime when_:
                        cell.Value = when_;
                        cell.Style.DateFormat.Format = kind == ColumnKind.DateTime ? "mmm d, yyyy h:mm AM/PM" : "mmm d, yyyy";
                        break;
                    default:
                        cell.Value = values[c].ToString();
                        break;
                }
                if (total)
                {
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F4");
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                }
            }
        }
        foreach (var values in r.Rows) Write(values, false);
        if (r.Total != null) Write(r.Total, true);

        sheet.SheetView.FreezeRows(header);
        sheet.Columns(1, Math.Max(1, r.Columns.Count)).AdjustToContents();
        workbook.SaveAs(path);
    }
}
