using System.Net;
using System.Text;
using VetCare.Helpers;
using VetCare.Models.Entities;

namespace VetCare.Views.Shared;

/// <summary>
/// The printable report (section 3.C): A4, black-and-white friendly, opened with the print dialog. Logo, clinic,
/// report name and range, generated-by line, the summary boxes, the chart, the table with TOTAL, and page numbers.
/// </summary>
public static class ReportPrintDocument
{
    public static string Build(ReportResult r, Clinic clinic, string generatedBy)
    {
        var html = new StringBuilder();
        html.Append(@"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>").Append(E(r.Definition.Name)).Append(@" report</title><style>
@page { size: A4; margin: 14mm 12mm 16mm; @bottom-right { content: ""Page "" counter(page) "" of "" counter(pages); font-size: 9px; } @bottom-left { content: ""VetCare – Veterinary Clinic Management System""; font-size: 9px; } }
body { font-family: Arial, Helvetica, sans-serif; color: #000; font-size: 11px; margin: 0; }
.head { display: flex; justify-content: space-between; align-items: center; border-bottom: 1.5px solid #000; padding-bottom: 8px; }
.head img { height: 40px; filter: grayscale(1); }
.clinic { margin-left: 10px; flex: 1; }
.title { text-align: right; }
.title b { font-size: 16px; }
.muted { color: #444; font-size: 10px; }
.cards { display: flex; gap: 8px; margin: 12px 0; }
.card { flex: 1; border: 1px solid #999; padding: 8px; }
.card .v { font-size: 15px; font-weight: bold; }
h3 { font-size: 12px; margin: 12px 0 4px; }
svg { width: 100%; height: auto; }
table { width: 100%; border-collapse: collapse; }
th, td { padding: 4px 5px; border-bottom: 0.5px solid #bbb; text-align: left; }
th { background: #eee; border-bottom: 1px solid #000; font-size: 10px; }
.r { text-align: right; }
tr.total td { font-weight: bold; background: #eee; border-top: 1px solid #000; }
thead { display: table-header-group; }
tr { page-break-inside: avoid; }
@media screen { body { background: #ddd; } .page { background: #fff; max-width: 190mm; margin: 12px auto; padding: 14mm 12mm; } }
</style></head><body onload=""window.print()""><div class=""page"">");

        html.Append(@"<div class=""head"">").Append(RecordPrintDocument.LogoHtml())
            .Append(@"<div class=""clinic""><b>").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</b>");
        if (!string.IsNullOrWhiteSpace(clinic?.Address)) html.Append("<br>").Append(E(clinic.Address));
        if (!string.IsNullOrWhiteSpace(clinic?.ContactNo)) html.Append("<br>Tel. ").Append(E(clinic.ContactNo));
        html.Append(@"</div><div class=""title""><b>").Append(E(r.Definition.Name)).Append(" report</b><br>")
            .Append(E(ExportHelper.RangeText(r))).Append(@"<br><span class=""muted"">").Append(E(generatedBy)).Append("</span></div></div>");
        if (!string.IsNullOrEmpty(r.Note)) html.Append(@"<p class=""muted"">").Append(E(r.Note)).Append("</p>");

        html.Append(@"<div class=""cards"">");
        foreach (var card in r.Cards)
        {
            html.Append(@"<div class=""card""><div class=""muted"">").Append(E(card.Label)).Append(@"</div><div class=""v"">").Append(E(card.Value)).Append("</div>");
            if (!string.IsNullOrEmpty(card.Sub)) html.Append(@"<div class=""muted"">").Append(E(card.Sub)).Append("</div>");
            html.Append("</div>");
        }
        html.Append("</div>");

        if (!r.IsEmpty)
        {
            html.Append("<h3>").Append(E(r.Chart.Title)).Append("</h3>").Append(ReportChartSvg.Build(r.Chart));
            if (r.SideChart != null) html.Append("<h3>").Append(E(r.SideChart.Title)).Append("</h3>").Append(ReportChartSvg.Build(r.SideChart, 720, 200));
        }

        html.Append("<h3>").Append(E(r.TableTitle)).Append("</h3><table><thead><tr>");
        foreach (var c in r.Columns) html.Append(c.RightAligned ? @"<th class=""r"">" : "<th>").Append(E(c.Title)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        void Row(object[] values, bool total)
        {
            html.Append(total ? @"<tr class=""total"">" : "<tr>");
            for (int i = 0; i < r.Columns.Count; i++)
                html.Append(r.Columns[i].RightAligned ? @"<td class=""r"">" : "<td>")
                    .Append(E(i < values.Length ? ReportFormat.Cell(values[i], r.Columns[i].Kind) : string.Empty)).Append("</td>");
            html.Append("</tr>");
        }
        foreach (var values in r.Rows) Row(values, false);
        if (r.Total != null) Row(r.Total, true);
        if (r.IsEmpty) html.Append(@"<tr><td colspan=""").Append(r.Columns.Count).Append(@""">No data for this period.</td></tr>");
        html.Append("</tbody></table></div></body></html>");
        return html.ToString();
    }

    private static string E(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
