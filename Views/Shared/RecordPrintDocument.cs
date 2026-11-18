using System.Globalization;
using System.Net;
using System.Text;
using VetCare.Models.Entities;

namespace VetCare.Views.Shared;

/// <summary>
/// The owner's printed copy of a medical record (section 3.D): an A4, black-and-white friendly
/// HTML page that opens the print dialog by itself (it can also be saved as PDF from there).
/// </summary>
public static class RecordPrintDocument
{
    /// <summary>The VetCare logo as a data URI (built into the app), so the printed page needs no other file.</summary>
    private static readonly Lazy<string> LogoDataUri = new(() =>
    {
        using var stream = typeof(RecordPrintDocument).Assembly.GetManifestResourceStream("VetCare.logo.png");
        if (stream == null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
    });

    /// <summary>The logo image, or the black placeholder box if the logo can't be read.</summary>
    internal static string LogoHtml() => LogoDataUri.Value is string uri
        ? "<img class=\"logo\" src=\"" + uri + "\" alt=\"VetCare Veterinary Clinic\">"
        : "<div class=\"logo-box\"></div>";

    public static string Build(RecordDetails d, Clinic clinic, DateTime? nextVaccineDue, DateTime printedAt)
    {
        var r = d.Record;
        static string E(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
        static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
        string Multiline(string s) => E(Or(s)).Replace("\n", "<br>");
        var inv = CultureInfo.InvariantCulture;

        var html = new StringBuilder();
        html.Append(@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<title>Medical record – ").Append(E(r.PetName)).Append(@"</title>
<style>
  @page { size: A4; margin: 16mm; }
  * { box-sizing: border-box; }
  body { font-family: 'Segoe UI', Arial, sans-serif; color: #000; font-size: 11pt; margin: 0; }
  .head { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #000; padding-bottom: 10px; }
  .logo { height: 60px; width: auto; display: block; }
  .logo-box { width: 160px; height: 50px; background: #000; }
  .clinic { text-align: right; font-size: 10pt; line-height: 1.4; }
  .clinic b { font-size: 13pt; }
  h1 { font-size: 18pt; margin: 18px 0 12px; }
  .two { display: flex; gap: 24px; }
  .box { flex: 1; border: 1px solid #000; padding: 8px 10px; }
  .box h3 { margin: 0 0 6px; font-size: 10pt; text-transform: uppercase; letter-spacing: .5px; }
  .row { display: flex; gap: 24px; margin: 12px 0; flex-wrap: wrap; }
  .row div span { color: #444; }
  h2 { font-size: 12pt; margin: 16px 0 4px; border-bottom: 1px solid #000; padding-bottom: 2px; }
  p { margin: 4px 0; line-height: 1.4; }
  table { width: 100%; border-collapse: collapse; margin-top: 6px; font-size: 10pt; }
  th, td { border: 1px solid #000; padding: 4px 6px; text-align: left; vertical-align: top; }
  th { background: #eee; }
  .due { margin-top: 14px; font-weight: bold; }
  .foot { margin-top: 40px; display: flex; justify-content: space-between; align-items: flex-end; font-size: 10pt; }
  .sign { border-top: 1px solid #000; width: 280px; padding-top: 4px; }
  @media screen { body { max-width: 210mm; margin: 20px auto; padding: 16mm; box-shadow: 0 0 12px rgba(0,0,0,.2); } }
</style></head>
<body onload=""window.print()"">
<div class=""head"">").Append(LogoHtml()).Append(@"<div class=""clinic""><b>").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</b><br>")
            .Append(E(clinic?.Address)).Append("<br>").Append(E(clinic?.ContactNo)).Append(@"</div></div>
<h1>Medical Record</h1>
<div class=""two"">
  <div class=""box""><h3>Pet</h3>")
            .Append("<b>").Append(E(r.PetName)).Append("</b><br>")
            .Append(E(string.Join(" · ", new[] { r.Species, r.Breed, r.PetSex }.Where(t => !string.IsNullOrWhiteSpace(t))))).Append("<br>")
            .Append("Age: ").Append(E(Or(r.AgeText))).Append(@"</div>
  <div class=""box""><h3>Owner</h3><b>").Append(E(r.OwnerName)).Append("</b><br>Contact no.: ").Append(E(r.OwnerContactDisplay)).Append(@"</div>
</div>
<div class=""row"">
  <div><span>Visit date:</span> ").Append(E(r.VisitText)).Append(@"</div>
  <div><span>Veterinarian:</span> ").Append(E(r.VetName)).Append(@"</div>
  <div><span>Weight:</span> ").Append(E(Or(r.WeightText))).Append(@"</div>
  <div><span>Temperature:</span> ").Append(E(Or(r.TemperatureText))).Append(@"</div>
</div>
<h2>Diagnosis</h2><p>").Append(Multiline(r.Diagnosis)).Append(@"</p>
<h2>Treatment</h2><p>").Append(Multiline(r.Treatment)).Append(@"</p>
<h2>Notes</h2><p>").Append(Multiline(r.Notes)).Append("</p>");

        html.Append("<h2>Prescriptions</h2>");
        if (d.Prescriptions.Count == 0) html.Append("<p>None.</p>");
        else
        {
            html.Append("<table><tr><th>Medicine</th><th>Qty</th><th>Dosage</th><th>Instructions</th></tr>");
            foreach (var p in d.Prescriptions)
                html.Append("<tr><td>").Append(E(p.MedicineName)).Append("</td><td>").Append(p.Quantity).Append(' ').Append(E(p.Unit))
                    .Append("</td><td>").Append(E(Or(p.Dosage))).Append("</td><td>").Append(E(Or(p.Instructions))).Append("</td></tr>");
            html.Append("</table>");
        }

        html.Append("<h2>Vaccinations</h2>");
        if (d.Vaccinations.Count == 0) html.Append("<p>None given at this visit.</p>");
        else
        {
            html.Append("<table><tr><th>Vaccine</th><th>Dose no.</th><th>Date given</th><th>Next due</th></tr>");
            foreach (var v in d.Vaccinations)
                html.Append("<tr><td>").Append(E(v.VaccineName)).Append("</td><td>").Append(v.DoseNo)
                    .Append("</td><td>").Append(v.DateGiven.ToString("MMM d, yyyy", inv))
                    .Append("</td><td>").Append(v.NextDueDate?.ToString("MMM d, yyyy", inv) ?? "—").Append("</td></tr>");
            html.Append("</table>");
        }

        foreach (var n in d.Notes)
            html.Append("<p><i>Added ").Append(n.CreatedAt.ToString("MMM d, yyyy", inv)).Append(" by ").Append(E(n.VetName))
                .Append(":</i> ").Append(E(n.NoteText)).Append("</p>");

        if (nextVaccineDue != null)
            html.Append("<div class=\"due\">Next vaccine due: ").Append(nextVaccineDue.Value.ToString("MMM dd, yyyy", inv)).Append("</div>");

        html.Append(@"
<div class=""foot""><div class=""sign"">Attending veterinarian: ").Append(E(r.VetName)).Append(@"</div>
<div>Printed ").Append(printedAt.ToString("MMM d, yyyy h:mm tt", inv)).Append(@"</div></div>
</body></html>");
        return html.ToString();
    }
}
