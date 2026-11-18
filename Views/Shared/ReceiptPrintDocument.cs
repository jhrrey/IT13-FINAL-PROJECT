using System.Net;
using System.Text;
using VetCare.Helpers;
using VetCare.Models.Entities;

namespace VetCare.Views.Shared;

/// <summary>
/// The official receipt (section 3.E): an 80 mm thermal-style, black-and-white HTML page (also fine on A5) that
/// opens the print dialog. Logo, clinic, bill no. and date, owner / pet / vet, items, total, payments, balance, cashier.
/// </summary>
public static class ReceiptPrintDocument
{
    public static string Build(Bill bill, Clinic clinic, DateTime printedAt, decimal? lastCashChange = null)
    {
        var html = new StringBuilder();
        html.Append(@"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>Receipt ").Append(E(bill.NumberText)).Append(@"</title><style>
@page { size: 80mm auto; margin: 4mm; }
* { box-sizing: border-box; }
body { margin: 0; font-family: 'Courier New', Consolas, monospace; color: #000; font-size: 12px; }
.r { width: 72mm; margin: 0 auto; }
.c { text-align: center; }
.logo img { height: 40px; filter: grayscale(1); }
.clinic { font-weight: bold; font-size: 14px; margin-top: 4px; }
.small { font-size: 11px; }
.title { font-weight: bold; font-size: 14px; letter-spacing: 1px; margin-top: 8px; }
hr { border: 0; border-top: 1px dashed #000; margin: 8px 0; }
.row { display: flex; justify-content: space-between; gap: 8px; }
.item { margin-bottom: 4px; }
.total { font-weight: bold; font-size: 15px; }
.foot { margin-top: 10px; }
@media screen { body { background: #eee; } .r { background: #fff; padding: 10px; margin: 16px auto; } }
</style></head><body onload=""window.print()""><div class=""r"">");

        html.Append(@"<div class=""c logo"">").Append(RecordPrintDocument.LogoHtml()).Append("</div>");
        html.Append(@"<div class=""c clinic"">").Append(E(clinic?.ClinicName ?? "VetCare Clinic")).Append("</div>");
        if (!string.IsNullOrWhiteSpace(clinic?.Address)) html.Append(@"<div class=""c small"">").Append(E(clinic.Address)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(clinic?.ContactNo)) html.Append(@"<div class=""c small"">Tel. ").Append(E(clinic.ContactNo)).Append("</div>");
        html.Append(@"<div class=""c title"">OFFICIAL RECEIPT</div>");
        html.Append(@"<div class=""c"">").Append(E(bill.NumberText)).Append("</div>");
        html.Append(@"<div class=""c small"">").Append(E(printedAt.ToString("MMM d, yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture))).Append("</div>");
        if (bill.Status == BillStatuses.Void) html.Append(@"<div class=""c title"">*** VOID ***</div>");

        html.Append("<hr>");
        Line(html, "Owner", bill.OwnerName);
        Line(html, "Pet", bill.PetName);
        Line(html, "Vet", bill.VetName);
        Line(html, "Visit", bill.VisitDate.ToString("MMM d, yyyy"));

        html.Append("<hr>");
        foreach (var item in bill.Items)
        {
            html.Append(@"<div class=""item""><div>").Append(E(item.Description)).Append("</div>");
            html.Append(@"<div class=""row""><span>").Append(item.Quantity).Append(" x ").Append(E(CurrencyHelper.Peso(item.UnitPrice)))
                .Append("</span><span>").Append(E(CurrencyHelper.Peso(item.Subtotal))).Append("</span></div></div>");
        }

        html.Append("<hr>");
        html.Append(@"<div class=""row total""><span>TOTAL</span><span>").Append(E(CurrencyHelper.Peso(bill.TotalAmount))).Append("</span></div>");
        foreach (var p in bill.Payments)
        {
            string label = p.PaymentMethod + (string.IsNullOrEmpty(p.ReferenceNo) ? string.Empty : " #" + p.ReferenceNo) + " " + p.PaymentDate.ToString("MM/dd");
            Line(html, label, CurrencyHelper.Peso(p.AmountPaid));
        }
        if (lastCashChange is decimal change && change > 0) Line(html, "Change", CurrencyHelper.Peso(change));
        html.Append(@"<div class=""row total""><span>BALANCE</span><span>").Append(E(CurrencyHelper.Peso(bill.Balance))).Append("</span></div>");

        html.Append("<hr>");
        html.Append(@"<div class=""small"">Cashier: ").Append(E(bill.CashierName)).Append("</div>");
        html.Append(@"<div class=""c foot"">" + System.Net.WebUtility.HtmlEncode(SettingsCache.GetText(SettingKeys.FooterMessage)) + "<br>&#128062;</div>");
        html.Append("</div></body></html>");
        return html.ToString();
    }

    private static void Line(StringBuilder html, string label, string value) =>
        html.Append(@"<div class=""row""><span>").Append(E(label)).Append("</span><span>").Append(E(value)).Append("</span></div>");

    private static string E(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
