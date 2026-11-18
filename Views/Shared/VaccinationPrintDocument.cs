using System.Globalization;
using System.Net;
using System.Text;
using VetCare.Helpers;
using VetCare.Models.Entities;

namespace VetCare.Views.Shared;

/// <summary>
/// Printable pages of the Vaccinations module, opened in the browser with the print dialog:
/// the owner's vaccination card (A5, black-and-white friendly) and the clinic's due list (A4).
/// </summary>
public static class VaccinationPrintDocument
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string E(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    private static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    /// <summary>Section 3.D: the pet's vaccination card.</summary>
    public static string BuildCard(Pet pet, IReadOnlyList<RecordVaccination> vaccinations, Clinic clinic, DateTime printedAt)
    {
        var next = vaccinations
            .GroupBy(v => v.MedicineId).Select(g => g.First().NextDueDate)
            .Where(d => d != null).OrderBy(d => d).FirstOrDefault();

        var html = new StringBuilder();
        html.Append(Head("Vaccination card – " + pet.PetName, "A5", @"
  .head { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #000; padding-bottom: 8px; }
  .logo { height: 46px; width: auto; display: block; } .logo-box { width: 140px; height: 44px; background: #000; }
  .clinic { text-align: right; font-size: 8.5pt; line-height: 1.35; } .clinic b { font-size: 11pt; }
  h1 { font-size: 15pt; margin: 12px 0 8px; }
  .two { display: flex; gap: 12px; } .box { flex: 1; border: 1px solid #000; padding: 6px 8px; font-size: 9pt; line-height: 1.45; }
  .box h3 { margin: 0 0 4px; font-size: 8pt; text-transform: uppercase; letter-spacing: .5px; }
  table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 8.5pt; }
  th, td { border: 1px solid #000; padding: 4px 5px; text-align: left; } th { background: #eee; }
  td.sign { width: 22%; }
  .next { margin-top: 12px; font-size: 14pt; font-weight: bold; text-align: center; border: 2px solid #000; padding: 6px; }
  .foot { margin-top: 10px; font-size: 8pt; text-align: right; }"));

        html.Append("<div class=\"head\">").Append(RecordPrintDocument.LogoHtml())
            .Append("<div class=\"clinic\"><b>").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</b><br>")
            .Append(E(clinic?.Address)).Append("<br>").Append(E(clinic?.ContactNo)).Append("</div></div>")
            .Append("<h1>Pet Vaccination Card</h1><div class=\"two\"><div class=\"box\"><h3>Pet</h3><b>").Append(E(pet.PetName)).Append("</b><br>")
            .Append("Species: ").Append(E(Or(pet.Species))).Append("<br>Breed: ").Append(E(Or(pet.Breed)))
            .Append("<br>Sex: ").Append(E(Or(pet.Sex))).Append("<br>Birth date: ").Append(pet.BirthDate?.ToString("MMM d, yyyy", Inv) ?? "—")
            .Append("<br>Color: ").Append(E(Or(pet.Color))).Append("</div><div class=\"box\"><h3>Owner</h3><b>").Append(E(pet.OwnerName))
            .Append("</b><br>Contact no.: ").Append(E(pet.OwnerContactDisplay)).Append("</div></div>");

        html.Append("<table><tr><th>Vaccine</th><th>Dose</th><th>Date given</th><th>Next due</th><th>Vet signature</th></tr>");
        if (vaccinations.Count == 0) html.Append("<tr><td colspan=\"5\">No vaccines recorded yet.</td></tr>");
        foreach (var v in vaccinations.OrderBy(v => v.VaccineName).ThenBy(v => v.DateGiven))
            html.Append("<tr><td>").Append(E(v.VaccineName)).Append("</td><td>").Append(E(v.DoseText))
                .Append("</td><td>").Append(v.DateGiven.ToString("MMM d, yyyy", Inv))
                .Append("</td><td>").Append(v.NextDueDate?.ToString("MMM d, yyyy", Inv) ?? "—")
                .Append("</td><td class=\"sign\"></td></tr>");
        html.Append("</table>");

        if (next != null)
            html.Append("<div class=\"next\">Next vaccine due: ").Append(next.Value.ToString("MMM dd, yyyy", Inv)).Append("</div>");
        html.Append("<div class=\"foot\">Printed ").Append(printedAt.ToString("MMM d, yyyy h:mm tt", Inv)).Append("</div></body></html>");
        return html.ToString();
    }

    /// <summary>The "Print due list" page: the follow-up list as currently filtered.</summary>
    public static string BuildDueList(IReadOnlyList<RecordVaccination> due, Clinic clinic, string filterText, DateTime today, DateTime printedAt)
    {
        var html = new StringBuilder();
        html.Append(Head("Vaccines due – " + (clinic?.ClinicName ?? "VetCare"), "A4", @"
  .head { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #000; padding-bottom: 8px; }
  .logo { height: 50px; width: auto; display: block; } .logo-box { width: 140px; height: 44px; background: #000; }
  .clinic { text-align: right; font-size: 9pt; line-height: 1.35; } .clinic b { font-size: 12pt; }
  h1 { font-size: 16pt; margin: 14px 0 2px; } .sub { font-size: 9.5pt; margin-bottom: 10px; }
  table { width: 100%; border-collapse: collapse; font-size: 9pt; }
  th, td { border: 1px solid #000; padding: 4px 6px; text-align: left; } th { background: #eee; }
  td.called { width: 12%; }
  .foot { margin-top: 10px; font-size: 8.5pt; text-align: right; }"));

        html.Append("<div class=\"head\">").Append(RecordPrintDocument.LogoHtml())
            .Append("<div class=\"clinic\"><b>").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</b><br>")
            .Append(E(clinic?.Address)).Append("<br>").Append(E(clinic?.ContactNo)).Append("</div></div>")
            .Append("<h1>Vaccines due &amp; overdue</h1><div class=\"sub\">").Append(E(filterText)).Append(" · ").Append(due.Count)
            .Append(due.Count == 1 ? " pet" : " pets").Append("</div>");

        html.Append("<table><tr><th>Pet</th><th>Owner</th><th>Contact no.</th><th>Vaccine</th><th>Dose</th><th>Due date</th><th>Days</th><th>Called</th></tr>");
        if (due.Count == 0) html.Append("<tr><td colspan=\"8\">No pets are due or overdue.</td></tr>");
        foreach (var v in due)
            html.Append("<tr><td>").Append(E(v.PetName)).Append(" (").Append(E(v.Species)).Append(")</td><td>").Append(E(v.OwnerName))
                .Append("</td><td>").Append(E(v.OwnerContactDisplay)).Append("</td><td>").Append(E(v.VaccineName))
                .Append("</td><td>").Append(E(NextDoseText(v))).Append("</td><td>").Append(v.NextDueDate?.ToString("MMM d, yyyy", Inv))
                .Append("</td><td>").Append(E(VaccineScheduleHelper.DaysText(v.NextDueDate ?? today, today)))
                .Append("</td><td class=\"called\"></td></tr>");
        html.Append("</table><div class=\"foot\">Printed ").Append(printedAt.ToString("MMM d, yyyy h:mm tt", Inv)).Append("</div></body></html>");
        return html.ToString();
    }

    /// <summary>The dose that is due next ("Dose 3" or "Booster").</summary>
    public static string NextDoseText(RecordVaccination v) => v.DoseNo + 1 >= 4 ? "Booster" : "Dose " + (v.DoseNo + 1);

    private static string Head(string title, string pageSize, string css) => @"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8""><title>" + E(title) + @"</title>
<style>
  @page { size: " + pageSize + @"; margin: 12mm; }
  * { box-sizing: border-box; }
  body { font-family: 'Segoe UI', Arial, sans-serif; color: #000; margin: 0; font-size: 10pt; }
  @media screen { body { max-width: " + (pageSize == "A5" ? "148mm" : "210mm") + @"; margin: 20px auto; padding: 12mm; box-shadow: 0 0 12px rgba(0,0,0,.2); } }" + css + @"
</style></head><body onload=""window.print()"">";
}
