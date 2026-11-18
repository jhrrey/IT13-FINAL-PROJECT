using System.Net;
using System.Text;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;

namespace VetCare.Views.Shared;

/// <summary>
/// The medicine label (section H): a 4 × 2.5 inch black-and-white sticker per prescription, as a printable
/// HTML page that opens the print dialog. Clinic name and contact, pet and owner, medicine and quantity,
/// dosage and instructions, then the date, the vet and "For animal use only".
/// </summary>
public static class MedicineLabelDocument
{
    public static string Build(IReadOnlyList<PharmacyPrescription> items, Clinic clinic, DateTime date)
    {
        var html = new StringBuilder();
        html.Append(@"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>Medicine label</title><style>
@page { size: 4in 2.5in; margin: 0; }
* { box-sizing: border-box; }
body { margin: 0; font-family: Arial, Helvetica, sans-serif; color: #000; }
.label { width: 4in; height: 2.5in; padding: 0.14in 0.18in; display: flex; flex-direction: column; page-break-after: always; overflow: hidden; }
.label:last-child { page-break-after: auto; }
.head { display: flex; align-items: center; gap: 8px; border-bottom: 1.5px solid #000; padding-bottom: 4px; }
.head img { height: 26px; filter: grayscale(1); }
.clinic { font-size: 10px; line-height: 1.25; }
.clinic b { font-size: 12px; }
.who { font-size: 11px; margin-top: 4px; }
.med { font-size: 17px; font-weight: bold; margin-top: 3px; line-height: 1.15; }
.qty { font-weight: normal; font-size: 13px; }
.dose { font-size: 14px; font-weight: bold; margin-top: 3px; }
.instr { font-size: 13px; line-height: 1.25; margin-top: 2px; flex: 1; overflow: hidden; }
.foot { display: flex; justify-content: space-between; font-size: 9.5px; border-top: 1px solid #000; padding-top: 3px; }
@media screen { body { background: #eee; } .label { background: #fff; margin: 12px auto; border: 1px dashed #999; } }
</style></head><body onload=""window.print()"">");

        foreach (var p in items)
        {
            html.Append(@"<div class=""label""><div class=""head"">").Append(RecordPrintDocument.LogoHtml())
                .Append(@"<div class=""clinic""><b>").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</b>");
            if (!string.IsNullOrWhiteSpace(clinic?.ContactNo)) html.Append("<br>Tel. ").Append(E(clinic.ContactNo));
            html.Append("</div></div>");
            html.Append(@"<div class=""who""><b>").Append(E(p.PetName)).Append("</b> · Owner: ").Append(E(p.OwnerName)).Append("</div>");
            html.Append(@"<div class=""med"">").Append(E(p.MedicineName)).Append(@" <span class=""qty"">× ").Append(E(p.QuantityText)).Append("</span></div>");
            if (!string.IsNullOrWhiteSpace(p.Dosage)) html.Append(@"<div class=""dose"">").Append(E(p.Dosage)).Append("</div>");
            html.Append(@"<div class=""instr"">").Append(E(p.Instructions ?? string.Empty)).Append("</div>");
            html.Append(@"<div class=""foot""><span>").Append(E((p.DispensedAt ?? date).ToString("MMM dd, yyyy"))).Append(" · ").Append(E(p.VetDisplay))
                .Append("</span><b>For animal use only</b></div></div>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string E(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
