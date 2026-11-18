using System.Globalization;
using Microsoft.Data.SqlClient;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Controllers;

/// <summary>
/// Bills &amp; payments. Enforces Tasks/VetCare_Billing_Module.md, section 4:
///  1. only Cashier/Staff create bills, receive payments and void; Administrators view only;
///  2. only the signed-in user's clinic;
///  3. a bill only for a Completed visit, and only one per visit (checked under lock in BillingRepository);
///  4. subtotal = qty × price and total = sum of subtotals, calculated by the system;
///  5. quantity above 0, unit price 0 or more;
///  6. Medicine and Vaccine items come from Pharmacy / Vaccination and can't be edited or removed here;
///  7. medicines dispensed after the bill is created are added automatically while it's Unpaid or Partial (VisitCharges);
///  8. Consultation and Service items can be edited or removed while the bill is Unpaid;
///  9. a payment is above ₱0 and not more than the balance; cash change is shown, not saved;
/// 10. a reference no. is required for GCash, Maya, Card and Bank transfer;
/// 11. the status follows the payments (Unpaid → Partial → Paid);
/// 12. Paid bills are locked;  13. payments can't be edited or deleted;
/// 14. void only with no payments, after confirming; kept in the records;
/// 15. an owner with an unpaid or partial bill shows the balance and can't be archived (OwnerLinksRepository).
/// </summary>
public sealed class BillingController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";
    private const string VoidReasonField = "voidReason";

    private readonly UserSession _session;
    private readonly BillingRepository _billing;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private BillingPage _list;
    private BillDetailsPage _details;
    private Toast _toast;
    private int _page = 1;
    private int _refreshNumber;
    private Func<IDictionary<string, string>, Task> _pendingConfirm;

    public BillingController(UserSession session, BillingRepository billing, ClinicRepository clinics)
    {
        _session = session;
        _billing = billing;
        _clinics = clinics;
    }

    // Rules ---------------------------------------------------------------------------------------------------

    public bool CanView => _session.ClinicId != null && (_session.RoleId == RoleIds.CashierStaff || _session.IsAdministrator);

    /// <summary>Rule 1.</summary>
    public bool CanManage => _session.ClinicId != null && _session.RoleId == RoleIds.CashierStaff;

    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 6 of the module document) --------------------------------------------------------------

    public Task<IReadOnlyList<ReadyToBillVisit>> GetReadyToBillAsync(string search) => _billing.GetReadyToBillAsync(ClinicId, search);

    public Task<PagedResult<Bill>> GetBillsAsync(string search, string status, DateTime? from, DateTime? to, int page) =>
        _billing.SearchBillsAsync(new BillQuery { ClinicId = ClinicId, Search = search, Status = status, From = from, To = to, Page = page });

    public Task<(PagedResult<Payment> Page, decimal Total)> GetPaymentsAsync(string method, DateTime? from, DateTime? to, int page) =>
        _billing.GetPaymentsAsync(new PaymentQuery { ClinicId = ClinicId, Method = method, From = from, To = to, Page = page });

    public Task<BillingSummary> GetBillingSummaryAsync() => _billing.GetSummaryAsync(ClinicId, DateTime.Today);

    public Task<Bill> GetBillByIdAsync(int billId) => _billing.GetByIdAsync(ClinicId, billId);

    /// <summary>Builds the item list from the visit (vaccines, dispensed medicines, the clinic's consultation fee).</summary>
    public Task<BillDraft> PrepareBillAsync(int appointmentId) => _billing.PrepareAsync(ClinicId, appointmentId);

    /// <summary>Rules 3–6: creates the bill from the visit with the typed consultation and service lines.</summary>
    public async Task<(OperationResult Result, int BillId)> CreateBillAsync(int appointmentId, IReadOnlyList<BillLineInput> lines)
    {
        if (!CanManage) return (OperationResult.Fail("Only Cashier/Staff can create bills."), 0);
        var errors = new Dictionary<string, string>();
        var items = new List<BillItem>();
        for (int i = 0; i < lines.Count; i++)
        {
            var (item, error) = ValidateLine(lines[i].ItemType, lines[i].Description, lines[i].Quantity, lines[i].UnitPrice);
            if (error != null) errors["line:" + i] = error;
            else items.Add(item);
        }
        if (errors.Count > 0) return (OperationResult.Invalid(errors), 0);

        var (result, billId) = await _billing.CreateAsync(ClinicId, appointmentId, items, _session.UserId, DateTime.Now);
        return result switch
        {
            CreateBillResult.Created => (OperationResult.Success("Bill created."), billId),
            CreateBillResult.AlreadyBilled => (OperationResult.Fail("This visit already has a bill."), 0),
            _ => (OperationResult.Fail("Bills can only be created for completed visits."), 0)
        };
    }

    /// <summary>Rules 4, 5 and 8 for one consultation / service line.</summary>
    private static (BillItem Item, string Error) ValidateLine(string type, string description, string quantityText, string priceText)
    {
        string name = description?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, "Enter a description.");
        if (name.Length > 100) return (null, "Description: use 100 characters or fewer.");
        if (!int.TryParse(quantityText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) || quantity <= 0)
            return (null, "Quantity must be more than 0.");
        if (quantity > 9999) return (null, "Quantity is too large.");
        if (!CurrencyHelper.TryParse(priceText, out decimal price)) return (null, "Enter a unit price, e.g. 400.00.");
        if (price < 0) return (null, "Unit price can't be negative.");
        price = Math.Round(price, 2);
        if (price * quantity > 99999999.99m) return (null, "The amount is too large.");
        return (new BillItem { ItemType = type == BillItemTypes.Consultation ? BillItemTypes.Consultation : BillItemTypes.Service, Description = name, Quantity = quantity, UnitPrice = price }, null);
    }

    /// <summary>Rule 8: add a service to an Unpaid bill.</summary>
    public Task<OperationResult> AddServiceItemAsync(int billId, string description, string quantity, string price) =>
        SaveItemAsync(billId, null, description, quantity, price);

    /// <summary>Rule 8: edit a consultation / service line of an Unpaid bill.</summary>
    public Task<OperationResult> UpdateServiceItemAsync(int billId, BillItem item, string description, string quantity, string price) =>
        SaveItemAsync(billId, item, description, quantity, price);

    private async Task<OperationResult> SaveItemAsync(int billId, BillItem existing, string description, string quantity, string price)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can change bills.");
        if (existing != null && !existing.IsEditable) return OperationResult.Fail("Medicine and vaccine items can't be changed here.");   // rule 6
        var (item, error) = ValidateLine(existing?.ItemType ?? BillItemTypes.Service, description, quantity, price);
        if (error != null)
        {
            string field = error.StartsWith("Quantity", StringComparison.Ordinal) ? ServiceItemView.QuantityField
                : error.Contains("price", StringComparison.OrdinalIgnoreCase) || error.Contains("amount", StringComparison.OrdinalIgnoreCase) ? ServiceItemView.PriceField
                : ServiceItemView.DescriptionField;
            return OperationResult.Invalid(new Dictionary<string, string> { [field] = error });
        }
        if (existing != null) item.BillItemId = existing.BillItemId;
        var result = existing == null ? await _billing.AddItemAsync(ClinicId, billId, item) : await _billing.UpdateItemAsync(ClinicId, billId, item);
        return Changed(result, existing == null ? "Service added." : "Item saved.");
    }

    /// <summary>Rule 8: remove a consultation / service line of an Unpaid bill.</summary>
    public async Task<OperationResult> RemoveServiceItemAsync(int billId, BillItem item)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can change bills.");
        if (!item.IsEditable) return OperationResult.Fail("Medicine and vaccine items can't be removed here.");
        return Changed(await _billing.RemoveItemAsync(ClinicId, billId, item.BillItemId), "Item removed.");
    }

    private static OperationResult Changed(BillChangeResult result, string success) => result switch
    {
        BillChangeResult.Saved => OperationResult.Success(success),
        BillChangeResult.Locked => OperationResult.Fail("Only unpaid bills can be changed."),
        _ => OperationResult.Fail("This bill no longer exists.")
    };

    /// <summary>Rules 9–11 and 13. Returns the change for cash (shown, not saved).</summary>
    public async Task<(OperationResult Result, decimal Change)> ReceivePaymentAsync(Bill bill, PaymentInput input)
    {
        if (!CanManage) return (OperationResult.Fail("Only Cashier/Staff can receive payments."), 0);
        var errors = new Dictionary<string, string>();
        string method = PaymentMethods.All.Contains(input.Method) ? input.Method : PaymentMethods.Cash;

        decimal amount = 0;
        if (!CurrencyHelper.TryParse(input.Amount, out amount) || Math.Round(amount, 2) <= 0)
            errors[ReceivePaymentView.AmountField] = "Enter an amount above ₱0";
        else
        {
            amount = Math.Round(amount, 2);
            if (amount > bill.Balance) errors[ReceivePaymentView.AmountField] = "Amount can't be more than the balance";
        }

        decimal change = 0;
        if (method == PaymentMethods.Cash)
        {
            if (!string.IsNullOrWhiteSpace(input.AmountReceived))
            {
                if (!CurrencyHelper.TryParse(input.AmountReceived, out decimal received))
                    errors[ReceivePaymentView.ReceivedField] = "Enter the amount received";
                else if (Math.Round(received, 2) < amount && !errors.ContainsKey(ReceivePaymentView.AmountField))
                    errors[ReceivePaymentView.ReceivedField] = "Amount received is less than the amount to pay";
                else change = Math.Round(received, 2) - amount;
            }
        }
        else if (string.IsNullOrWhiteSpace(input.ReferenceNo)) errors[ReceivePaymentView.ReferenceField] = "Reference no. is required";       // rule 10
        else if (input.ReferenceNo.Trim().Length > 50) errors[ReceivePaymentView.ReferenceField] = "Use 50 characters or fewer";
        if (errors.Count > 0) return (OperationResult.Invalid(errors), 0);

        var (result, status) = await _billing.ReceivePaymentAsync(ClinicId, bill.BillId, amount, method,
            method == PaymentMethods.Cash ? null : input.ReferenceNo.Trim(), _session.UserId);
        return result switch
        {
            PaymentResult.Saved => (OperationResult.Success("Payment received. Bill is now " + status + "."), change),
            PaymentResult.OverBalance => (OperationResult.Invalid(new Dictionary<string, string> { [ReceivePaymentView.AmountField] = "Amount can't be more than the balance (it changed — reopen to see it)" }), 0),
            PaymentResult.Locked => (OperationResult.Fail("This bill is " + status + " and can't take payments."), 0),
            _ => (OperationResult.Fail("This bill no longer exists."), 0)
        };
    }

    /// <summary>Rule 14.</summary>
    public async Task<OperationResult> VoidBillAsync(Bill bill, string reason)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can void bills.");
        if (!bill.CanVoid) return OperationResult.Fail("This bill already has payments, so it can't be voided.");
        if (reason?.Trim().Length > 255) return OperationResult.Invalid(new Dictionary<string, string> { [VoidReasonField] = "Use 255 characters or fewer." });
        return await _billing.VoidAsync(ClinicId, bill.BillId, reason) == BillChangeResult.Saved
            ? OperationResult.Success(bill.NumberText + " voided.")
            : OperationResult.Fail("This bill already has payments, so it can't be voided.");
    }

    // View ------------------------------------------------------------------------------------------------------------

    /// <param name="openBillId">Open this bill's details straight away.</param>
    /// <param name="createForAppointmentId">Open "Create bill" for this visit (from Appointments).</param>
    public View CreateView(int? openBillId = null, int? createForAppointmentId = null)
    {
        if (!CanView)
            return new Label
            {
                Text = "Bills & payments are available to Cashier/Staff and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync(e.Values);

        _list = new BillingPage();
        _list.SetCanManage(CanManage);
        _list.FiltersChanged += async (s, e) => { _page = 1; await RefreshAsync(); };
        _list.PageRequested += async (s, p) => { _page = p; await RefreshAsync(); };
        _list.TabChanged += async (s, tab) => { _page = 1; await RefreshAsync(); };
        _list.CreateBillRequested += async (s, v) => await OpenCreateAsync(v.AppointmentId);
        _list.BillSelected += async (s, b) => await OpenDetailsAsync(b.BillId);
        _list.PaymentBillSelected += async (s, id) => await OpenDetailsAsync(id);
        _list.PaymentRequested += async (s, b) => await OpenPaymentAsync(b.BillId);
        _list.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Bill));
        _host.ShowPage(_list);
        _list.ShowLoading();

        _toast = new Toast();
        _ = LoadAsync(openBillId, createForAppointmentId);
        return new Grid { Children = { _host, _toast } };
    }

    private async Task LoadAsync(int? openBillId, int? createFor)
    {
        // Dashboard shortcuts: "tab:bills", "tab:payments", or "pay" (with openBillId: details + Receive payment).
        string intent = StartIntent ?? string.Empty;
        if (intent == "tab:bills") { _list.OpenTab(BillingPage.BillsTab); return; }
        if (intent == "tab:payments") { _list.OpenTab(BillingPage.PaymentsTab); return; }
        await RefreshAsync();
        if (createFor != null) await OpenCreateAsync(createFor.Value);
        else if (openBillId != null)
        {
            await OpenDetailsAsync(openBillId.Value);
            if (intent == "pay") await OpenPaymentAsync(openBillId.Value);
        }
    }

    /// <summary>Dashboard shortcut: "tab:bills", "tab:payments" or "pay".</summary>
    public string StartIntent { get; set; }

    private async Task RefreshAsync()
    {
        int mine = ++_refreshNumber;
        try
        {
            var summary = await GetBillingSummaryAsync();
            switch (_list.CurrentTab)
            {
                case BillingPage.ReadyTab:
                {
                    var visits = await GetReadyToBillAsync(_list.CurrentSearch);
                    if (mine != _refreshNumber) return;
                    _list.ShowSummary(summary);
                    _list.ShowReadyToBill(visits);
                    break;
                }
                case BillingPage.BillsTab:
                {
                    var page = await GetBillsAsync(_list.CurrentSearch, _list.CurrentStatus, _list.CurrentFrom, _list.CurrentTo, _page);
                    if (page.Items.Count == 0 && _page > 1) { _page--; page = await GetBillsAsync(_list.CurrentSearch, _list.CurrentStatus, _list.CurrentFrom, _list.CurrentTo, _page); }
                    if (mine != _refreshNumber) return;
                    _list.ShowSummary(summary);
                    _list.ShowBills(page);
                    break;
                }
                default:
                {
                    var (page, total) = await GetPaymentsAsync(_list.CurrentMethod, _list.CurrentFrom, _list.CurrentTo, _page);
                    if (mine != _refreshNumber) return;
                    _list.ShowSummary(summary);
                    _list.ShowPayments(page, total);
                    break;
                }
            }
        }
        catch (SqlException)
        {
            if (mine == _refreshNumber) _list.ShowError(DatabaseError);
        }
    }

    private async Task BackToListAsync()
    {
        _details = null;
        _host.ShowPage(_list);
        await RefreshAsync();
    }

    private IReadOnlyList<MenuEntry> MenuFor(Bill b)
    {
        var entries = new List<MenuEntry> { new("View", "eye", () => _ = OpenDetailsAsync(b.BillId)) };
        if (CanManage && b.Status != BillStatuses.Void)
        {
            entries.Add(new MenuEntry("Print receipt", "printer", () => _ = PrintReceiptAsync(b.BillId)));
            entries.Add(new MenuEntry("Void", "x", () => _ = ConfirmVoidAsync(b), Danger: true,
                Enabled: b.CanVoid, DisabledHint: "Only unpaid bills with no payments can be voided"));
        }
        return entries;
    }

    // Create bill -------------------------------------------------------------------------------------------------------

    /// <summary>Opens "Create bill" for a completed visit (or the visit's existing bill).</summary>
    public async Task OpenCreateAsync(int appointmentId)
    {
        try
        {
            var draft = await PrepareBillAsync(appointmentId);
            if (draft == null) { await ShowInfoAsync("Can’t create the bill", "This visit wasn’t found.", "circle-alert"); return; }
            if (draft.ExistingBillId is int existing) { await OpenDetailsAsync(existing); return; }
            if (!CanManage) return;
            if (draft.Status != AppointmentStatuses.Completed)
            {
                await ShowInfoAsync("Not ready to bill", "Bills can only be created for completed visits. This one is " + draft.Status + ".", "circle-alert");
                return;
            }

            var page = new CreateBillPage(draft);
            page.BackRequested += async (s, e) => await BackToListAsync();
            page.SaveRequested += async (s, pay) =>
            {
                page.SetBusy(true);
                try
                {
                    var (result, billId) = await CreateBillAsync(draft.AppointmentId, page.ReadLines());
                    if (!result.Ok) { page.ShowResult(result); return; }
                    _toast.Show(result.Message);
                    await OpenDetailsAsync(billId);
                    if (pay) await OpenPaymentAsync(billId);
                }
                catch (SqlException) { page.ShowResult(OperationResult.Fail(DatabaseError)); }
                finally { page.SetBusy(false); }
            };
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t create the bill", DatabaseError, "circle-alert");
        }
    }

    // Details -------------------------------------------------------------------------------------------------------------

    public async Task OpenDetailsAsync(int billId)
    {
        try
        {
            var bill = await GetBillByIdAsync(billId);
            if (bill == null) { await BackToListAsync(); return; }
            var page = new BillDetailsPage(bill, CanManage);
            page.BackRequested += async (s, e) => await BackToListAsync();
            page.PrintRequested += async (s, e) => await PrintReceiptAsync(bill.BillId);
            page.PaymentRequested += async (s, e) => await OpenPaymentAsync(bill.BillId);
            page.AddServiceRequested += async (s, e) => await OpenItemAsync(bill, null);
            page.EditItemRequested += async (s, item) => await OpenItemAsync(bill, item);
            page.RemoveItemRequested += async (s, item) => await RemoveItemAsync(bill, item);
            page.MenuRequested += (s, anchor) => _host.Menu.Show(anchor, new[]
            {
                new MenuEntry("Void bill", "x", () => _ = ConfirmVoidAsync(bill), Danger: true,
                    Enabled: bill.CanVoid, DisabledHint: "Only unpaid bills with no payments can be voided")
            });
            _details = page;
            _host.ShowPage(page);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t open the bill", DatabaseError, "circle-alert");
        }
    }

    private async Task ReloadDetailsAsync(int billId)
    {
        if (_details != null && _details.Bill.BillId == billId && _host.CurrentPage == _details) await OpenDetailsAsync(billId);
        else await RefreshAsync();
    }

    private async Task OpenItemAsync(Bill bill, BillItem item)
    {
        if (!CanManage) return;
        var view = new ServiceItemView(item);
        view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        view.SaveRequested += async (s, e) =>
        {
            var (description, quantity, price) = view.Read();
            view.SetBusy(true);
            try
            {
                var result = item == null
                    ? await AddServiceItemAsync(bill.BillId, description, quantity, price)
                    : await UpdateServiceItemAsync(bill.BillId, item, description, quantity, price);
                if (!result.Ok) { view.ShowResult(result); return; }
                await _host.Panel.HideAsync();
                _toast.Show(result.Message);
                await ReloadDetailsAsync(bill.BillId);
            }
            catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { view.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(view, ServiceItemView.PanelWidth);
    }

    private async Task RemoveItemAsync(Bill bill, BillItem item)
    {
        _pendingConfirm = async _ =>
        {
            _host.Dialog.SetBusy(true);
            try
            {
                var result = await RemoveServiceItemAsync(bill.BillId, item);
                if (!result.Ok) { _host.Dialog.ShowResult(result); return; }
                await CloseDialogAsync();
                _toast.Show(result.Message);
                await ReloadDetailsAsync(bill.BillId);
            }
            catch (SqlException) { _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { _host.Dialog.SetBusy(false); }
        };
        await _host.Dialog.ShowConfirmAsync("Remove " + item.Description + "?", "The bill total goes down by " + CurrencyHelper.Peso(item.Subtotal) + ".",
            "Remove", danger: true, icon: "trash-2");
    }

    // Payment ---------------------------------------------------------------------------------------------------------------

    private async Task OpenPaymentAsync(int billId)
    {
        if (!CanManage) return;
        try
        {
            var bill = await GetBillByIdAsync(billId);
            if (bill == null || !bill.IsOpen) { await ReloadDetailsAsync(billId); return; }
            var view = new ReceivePaymentView(bill);
            view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
            view.ConfirmRequested += async (s, e) =>
            {
                view.SetBusy(true);
                try
                {
                    var (result, change) = await ReceivePaymentAsync(bill, view.Read());
                    if (!result.Ok) { view.ShowResult(result); return; }
                    await _host.Panel.HideAsync();
                    _toast.Show(result.Message);
                    await ReloadDetailsAsync(billId);
                    await AskPrintAsync(billId, change);
                }
                catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
                finally { view.SetBusy(false); }
            };
            await _host.Panel.ShowAsync(view, ReceivePaymentView.PanelWidth);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t receive a payment right now", DatabaseError, "circle-alert");
        }
    }

    /// <summary>"Print receipt?" — Not now / Print.</summary>
    private Task AskPrintAsync(int billId, decimal change)
    {
        _pendingConfirm = async _ =>
        {
            await CloseDialogAsync();
            await PrintReceiptAsync(billId, change);
        };
        return _host.Dialog.ShowConfirmAsync("Print receipt?", "Give the owner a printed official receipt.", "Print",
            danger: false, icon: "printer", cancelText: "Not now");
    }

    // Void ----------------------------------------------------------------------------------------------------------------------

    private async Task ConfirmVoidAsync(Bill bill)
    {
        if (!CanManage) return;
        if (!bill.CanVoid)
        {
            await ShowInfoAsync("Can’t void " + bill.NumberText, "This bill already has payments, so it can't be voided.", "circle-alert");
            return;
        }
        _pendingConfirm = async values =>
        {
            values.TryGetValue(VoidReasonField, out var reason);
            _host.Dialog.SetBusy(true);
            try
            {
                var result = await VoidBillAsync(bill, reason);
                if (!result.Ok) { _host.Dialog.ShowResult(result); return; }
                await CloseDialogAsync();
                _toast.Show(result.Message);
                await ReloadDetailsAsync(bill.BillId);
            }
            catch (SqlException) { _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { _host.Dialog.SetBusy(false); }
        };
        await _host.Dialog.ShowConfirmWithNoteAsync("Void bill " + bill.NumberText + "?",
            "It will be kept in the records but won't count as income. Dispensed medicines aren't returned to stock — use Adjust stock if they really came back.",
            "Void bill", danger: true, icon: "x", VoidReasonField, "Reason (optional)", "e.g. Duplicate bill", 255);
    }

    // Printing ------------------------------------------------------------------------------------------------------------------

    public async Task PrintReceiptAsync(int billId, decimal? change = null)
    {
        try
        {
            var bill = await GetBillByIdAsync(billId);
            if (bill == null) return;
            string html = ReceiptPrintDocument.Build(bill, await _clinics.GetByIdAsync(ClinicId), DateTime.Now, change);
            string path = Path.Combine(FileSystem.CacheDirectory, "VetCare-receipt-" + bill.NumberText + ".html");
            await File.WriteAllTextAsync(path, html);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Print", new ReadOnlyFile(path)));
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t print right now", DatabaseError, "circle-alert");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowInfoAsync("Can’t print right now", "The receipt couldn’t be created: " + ex.Message, "circle-alert");
        }
    }

    // Dialogs -------------------------------------------------------------------------------------------------------------------

    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync(IDictionary<string, string> values)
    {
        var action = _pendingConfirm;
        if (action == null) { await CloseDialogAsync(); return; }
        await action(values ?? new Dictionary<string, string>());
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
