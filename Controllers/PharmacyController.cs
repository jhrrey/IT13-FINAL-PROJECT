using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Maui.Controls.Shapes;
using VetCare.Helpers;
using VetCare.Models;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;
using VetCare.Views.Controls;
using VetCare.Views.Shared;
using VetCare.Views.Staff;

namespace VetCare.Controllers;

/// <summary>Which sidebar item the controller is showing.</summary>
public enum PharmacyPage
{
    Medicines,
    Prescriptions
}

/// <summary>
/// Pharmacy: Medicines &amp; stock and Prescriptions. Enforces Tasks/VetCare_Pharmacy_Module.md, section 4:
///  1. Cashier/Staff manage medicines and dispense; Vets view (they prescribe in the consultation); Admins view;
///  2. only the signed-in clinic's medicines and prescriptions;
///  3. name, category, unit and price required; one name + unit per clinic;
///  4. price and reorder level 0 or more;
///  5. the stock only changes through Stock in, Adjust stock or Dispense, each logged;
///  6. dispense only when not dispensed yet, with enough stock, not expired;
///  7. dispensing is one SQL transaction (prescription, stock, log, bill item) — PrescriptionRepository;
///  8. vaccines are used by the vaccination (VisitCharges), not dispensed here;
///  9. an adjustment needs a reason and can't take the stock below 0;
/// 10. expired medicines can't be dispensed or used (also checked in the consultation);
/// 11. low / out / expiring shown with badges and counted on the cards (StockStatusHelper);
/// 12. archived, never deleted, and only at stock 0.
/// </summary>
public sealed class PharmacyController
{
    private const string DatabaseError = "Can’t reach the VetCare database. Check your connection and try again.";

    private readonly UserSession _session;
    private readonly MedicineRepository _medicines;
    private readonly InventoryRepository _inventory;
    private readonly PrescriptionRepository _prescriptions;
    private readonly UserRepository _users;
    private readonly ClinicRepository _clinics;

    private ModuleHostView _host;
    private DispenseDialogView _dispenseDialog;
    private Toast _toast;
    private InventoryPage _inventoryPage;
    private PrescriptionsPage _prescriptionsPage;
    private int _medicinesPage = 1, _historyPage = 1, _dispensedPage = 1;
    private int _refreshNumber;
    private Func<Task> _pendingConfirm;
    private IReadOnlyList<PharmacyPrescription> _dispensing;

    public PharmacyController(UserSession session, MedicineRepository medicines, InventoryRepository inventory,
        PrescriptionRepository prescriptions, UserRepository users, ClinicRepository clinics)
    {
        _session = session;
        _medicines = medicines;
        _inventory = inventory;
        _prescriptions = prescriptions;
        _users = users;
        _clinics = clinics;
    }

    /// <summary>Opens a medical record (group header "View record").</summary>
    public Action<int> OpenRecord { get; set; }

    // Rules ----------------------------------------------------------------------------------------------

    public bool CanView => _session.ClinicId != null &&
        (_session.RoleId == RoleIds.CashierStaff || _session.RoleId == RoleIds.Veterinarian || _session.IsAdministrator);

    /// <summary>Rule 1: only Cashier/Staff add, edit, stock in, adjust, archive and dispense.</summary>
    public bool CanManage => _session.ClinicId != null && _session.RoleId == RoleIds.CashierStaff;

    private int ClinicId => _session.ClinicId ?? 0;

    // Data methods (section 6 of the module document) ----------------------------------------------------------

    public Task<PagedResult<Medicine>> GetMedicinesAsync(string search, string category, StockStatus? status, int page) =>
        _medicines.SearchAsync(new MedicineQuery { ClinicId = ClinicId, Search = search, Category = category, Status = status, Page = page }, DateTime.Today);

    public Task<Medicine> GetMedicineByIdAsync(int medicineId) => _medicines.GetByIdAsync(ClinicId, medicineId);

    public Task<StockSummary> GetStockSummaryAsync() => _medicines.GetSummaryAsync(ClinicId, DateTime.Today);

    public Task<PagedResult<StockLogEntry>> GetStockHistoryAsync(int? medicineId, string type, DateTime? from, DateTime? to, int page, int pageSize = 10) =>
        _inventory.GetHistoryAsync(new StockLogQuery { ClinicId = ClinicId, MedicineId = medicineId, Type = type, From = from, To = to, Page = page, PageSize = pageSize });

    public Task<IReadOnlyList<PharmacyPrescription>> GetPrescriptionsToDispenseAsync(string search, int? vetId, DateTime? from, DateTime? to) =>
        _prescriptions.GetToDispenseAsync(ClinicId, search, vetId, from, to);

    public Task<PagedResult<PharmacyPrescription>> GetDispensedPrescriptionsAsync(string search, int? vetId, DateTime? from, DateTime? to, int page) =>
        _prescriptions.GetDispensedAsync(ClinicId, search, vetId, from, to, page);

    /// <summary>Rules 1, 3 and 4. A starting stock is saved as the first "Stock In".</summary>
    public async Task<OperationResult> AddMedicineAsync(MedicineFormData data)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can add medicines.");
        var (errors, medicine) = await ValidateMedicineAsync(data, null);
        int startingStock = 0;
        if (!string.IsNullOrWhiteSpace(data.StartingStock))
        {
            if (PharmacyKit.ParseCount(data.StartingStock) is not int n) errors[MedicineFormData.StartingStockField] = "Enter a whole number of 0 or more";
            else startingStock = n;
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        medicine.ClinicId = ClinicId;
        await _medicines.InsertAsync(medicine, startingStock, _session.UserId);
        return OperationResult.Success(medicine.MedicineName + " added.");
    }

    /// <summary>Rules 1, 3, 4 and 5 (the stock isn't part of an edit).</summary>
    public async Task<OperationResult> UpdateMedicineAsync(Medicine current, MedicineFormData data)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can edit medicines.");
        var (errors, medicine) = await ValidateMedicineAsync(data, current.MedicineId);
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        medicine.MedicineId = current.MedicineId;
        medicine.ClinicId = ClinicId;
        return await _medicines.UpdateAsync(medicine)
            ? OperationResult.Success(medicine.MedicineName + " saved.")
            : OperationResult.Fail("This medicine was archived or removed.");
    }

    private async Task<(Dictionary<string, string> Errors, Medicine Medicine)> ValidateMedicineAsync(MedicineFormData data, int? excludeId)
    {
        var errors = new Dictionary<string, string>();
        string name = data.Name?.Trim();
        if (string.IsNullOrEmpty(name)) errors[MedicineFormData.NameField] = "Required";
        else if (name.Length > MedicineFormData.NameMaxLength) errors[MedicineFormData.NameField] = "Use 100 characters or fewer";
        if (!Medicine.Categories.Contains(data.Category)) errors[MedicineFormData.CategoryField] = "Required";
        if (!Medicine.Units.Contains(data.Unit)) errors[MedicineFormData.UnitField] = "Required";

        decimal price = 0;
        if (string.IsNullOrWhiteSpace(data.Price)) errors[MedicineFormData.PriceField] = "Required";
        else if (!decimal.TryParse(data.Price.Trim().Replace("₱", string.Empty).Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out price))
            errors[MedicineFormData.PriceField] = "Enter a price, e.g. 12.00";
        else if (price < 0) errors[MedicineFormData.PriceField] = "Price can't be negative";
        else if (price > 99999999.99m) errors[MedicineFormData.PriceField] = "Price is too large";

        int reorder = 0;
        if (string.IsNullOrWhiteSpace(data.ReorderLevel)) errors[MedicineFormData.ReorderField] = "Required";
        else if (!int.TryParse(data.ReorderLevel.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out reorder))
            errors[MedicineFormData.ReorderField] = "Enter a whole number";
        else if (reorder < 0) errors[MedicineFormData.ReorderField] = "Reorder level can't be negative";

        // Rule 3: the same name and unit can't be added twice in one clinic.
        if (!errors.ContainsKey(MedicineFormData.NameField) && !errors.ContainsKey(MedicineFormData.UnitField) &&
            await _medicines.NameExistsAsync(ClinicId, name, data.Unit, excludeId))
            errors[MedicineFormData.NameField] = "This medicine already exists";

        var medicine = new Medicine
        {
            MedicineName = name,
            Category = data.Category,
            Unit = data.Unit,
            UnitPrice = Math.Round(price, 2),
            ReorderLevel = reorder,
            ExpiryDate = data.ExpiryDate?.Date
        };
        return (errors, medicine);
    }

    /// <summary>Rules 1 and 5: + quantity, an optional new expiry date, the supplier / reference as remarks.</summary>
    public async Task<OperationResult> StockInAsync(Medicine medicine, StockChangeData data)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can add stock.");
        var errors = new Dictionary<string, string>();
        int? quantity = PharmacyKit.ParseCount(data.Quantity);
        if (quantity is null or <= 0) errors[StockChangeData.QuantityField] = "Enter a quantity above 0";
        else if (medicine.StockQty + (long)quantity > int.MaxValue) errors[StockChangeData.QuantityField] = "That quantity is too large";
        if (data.NewExpiry != null && data.NewExpiry.Value.Date < DateTime.Today) errors[StockChangeData.ExpiryField] = "The new expiry date can't be in the past";
        if (data.Remarks?.Trim().Length > 255) errors[StockChangeData.RemarksField] = "Use 255 characters or fewer";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var result = await _medicines.StockInAsync(ClinicId, medicine.MedicineId, quantity.Value, data.NewExpiry?.Date, data.Remarks?.Trim(), _session.UserId);
        return result == StockChangeResult.Saved
            ? OperationResult.Success("Added " + quantity.Value.ToString("N0") + " " + medicine.Unit + " to " + medicine.MedicineName + ".")
            : OperationResult.Fail("This medicine was archived or removed.");
    }

    /// <summary>Rules 1, 5 and 9: Count correction (±), Expired or Damaged (−); a reason; never below 0.</summary>
    public async Task<OperationResult> AdjustStockAsync(Medicine medicine, StockChangeData data)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can adjust stock.");
        var errors = new Dictionary<string, string>();
        if (!StockChangeData.AdjustTypes.Contains(data.Type)) errors[StockChangeData.TypeField] = "Pick a type";
        int? quantity = PharmacyKit.ParseCount(data.Quantity);
        if (quantity is null or <= 0) errors[StockChangeData.QuantityField] = "Enter a quantity above 0";
        if (string.IsNullOrWhiteSpace(data.Remarks)) errors[StockChangeData.RemarksField] = "A reason is required";
        else if (data.Remarks.Trim().Length > 255) errors[StockChangeData.RemarksField] = "Use 255 characters or fewer";
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        bool adds = data.Type == StockChangeData.CountCorrection && data.Add;
        int signed = adds ? quantity.Value : -quantity.Value;
        if (!adds && quantity.Value > medicine.StockQty)
            return OperationResult.Invalid(new Dictionary<string, string> { [StockChangeData.QuantityField] = "Stock can't go below 0" });

        string type = data.Type == StockChangeData.CountCorrection ? "Adjustment" : data.Type;
        var result = await _medicines.AdjustAsync(ClinicId, medicine.MedicineId, type, signed, data.Remarks.Trim(), _session.UserId);
        return result switch
        {
            StockChangeResult.Saved => OperationResult.Success("Stock adjusted."),
            // Someone dispensed meanwhile: the database check (under lock) refused it.
            StockChangeResult.BelowZero => OperationResult.Invalid(new Dictionary<string, string> { [StockChangeData.QuantityField] = "Stock can't go below 0 (the stock changed — reopen to see it)" }),
            _ => OperationResult.Fail("This medicine was archived or removed.")
        };
    }

    /// <summary>Rules 1 and 12.</summary>
    public async Task<OperationResult> ArchiveMedicineAsync(Medicine medicine)
    {
        if (!CanManage) return OperationResult.Fail("Only Cashier/Staff can archive medicines.");
        return await _medicines.ArchiveAsync(ClinicId, medicine.MedicineId) switch
        {
            StockChangeResult.Saved => OperationResult.Success(medicine.MedicineName + " archived."),
            StockChangeResult.StockNotZero => OperationResult.Fail("Only medicines with 0 stock can be archived. Remove the stock with an adjustment first."),
            _ => OperationResult.Fail("This medicine was already archived.")
        };
    }

    /// <summary>Rules 1, 6, 7 and 10.</summary>
    public async Task<(OperationResult Result, bool Saved)> DispenseAsync(PharmacyPrescription p)
    {
        if (!CanManage) return (OperationResult.Fail("Only Cashier/Staff can dispense."), false);
        var result = await _prescriptions.DispenseAsync(ClinicId, p.PrescriptionId, _session.UserId);
        return result switch
        {
            DispenseResult.Saved => (OperationResult.Success(null), true),
            DispenseResult.AlreadyDispensed => (OperationResult.Fail(p.MedicineName + " was already dispensed."), false),
            DispenseResult.NotEnoughStock => (OperationResult.Fail("Not enough stock of " + p.MedicineName + "."), false),
            DispenseResult.Expired => (OperationResult.Fail(p.MedicineName + " is expired and can't be dispensed."), false),
            _ => (OperationResult.Fail("This prescription can no longer be dispensed."), false)
        };
    }

    // View ---------------------------------------------------------------------------------------------------

    public View CreateView(PharmacyPage page)
    {
        if (!CanView)
            return new Label
            {
                Text = "The pharmacy is available to Cashier/Staff, Veterinarians and clinic Administrators.",
                FontSize = 16,
                TextColor = ThemeResources.Color("BodyText"),
                Margin = new Thickness(40)
            };

        _host = new ModuleHostView();
        _host.Panel.DismissRequested += async (s, e) => await _host.Panel.HideAsync();
        _host.Dialog.CancelRequested += async (s, e) => await CloseDialogAsync();
        _host.Dialog.Confirmed += async (s, e) => await ConfirmDialogAsync();

        if (page == PharmacyPage.Medicines)
        {
            _inventoryPage = new InventoryPage();
            _inventoryPage.SetCanManage(CanManage);
            _inventoryPage.FiltersChanged += async (s, e) => { _medicinesPage = 1; await RefreshAsync(); };
            _inventoryPage.PageRequested += async (s, p) => { _medicinesPage = p; await RefreshAsync(); };
            _inventoryPage.HistoryFiltersChanged += async (s, e) => { _historyPage = 1; await RefreshAsync(); };
            _inventoryPage.HistoryPageRequested += async (s, p) => { _historyPage = p; await RefreshAsync(); };
            _inventoryPage.TabChanged += async (s, tab) => await RefreshAsync();
            _inventoryPage.AddRequested += async (s, e) => await OpenFormAsync(null);
            _inventoryPage.StockInRequested += async (s, m) => await OpenStockInAsync(m);
            _inventoryPage.DetailsRequested += async (s, m) => await OpenDetailsAsync(m.MedicineId);
            _inventoryPage.MenuRequested += (s, e) => _host.Menu.Show(e.Anchor, MenuFor(e.Medicine));
            _host.ShowPage(_inventoryPage);
            _inventoryPage.ShowLoading();
        }
        else
        {
            _prescriptionsPage = new PrescriptionsPage { CanOpenRecord = OpenRecord != null };
            _prescriptionsPage.SetCanDispense(CanManage);
            _prescriptionsPage.FiltersChanged += async (s, e) => { _dispensedPage = 1; await RefreshAsync(); };
            _prescriptionsPage.PageRequested += async (s, p) => { _dispensedPage = p; await RefreshAsync(); };
            _prescriptionsPage.TabChanged += async (s, tab) => await RefreshAsync();
            _prescriptionsPage.DispenseRequested += async (s, items) => await OpenDispenseAsync(items);
            _prescriptionsPage.ReprintRequested += async (s, p) => await PrintLabelsAsync(new[] { p });
            _prescriptionsPage.RecordRequested += (s, id) => OpenRecord?.Invoke(id);
            _host.ShowPage(_prescriptionsPage);
            _prescriptionsPage.ShowLoading();
        }

        _dispenseDialog = new DispenseDialogView();
        _dispenseDialog.CancelRequested += async (s, e) => await _dispenseDialog.HideAsync();
        _dispenseDialog.Confirmed += async (s, e) => await ConfirmDispenseAsync();

        _toast = new Toast();

        _ = LoadAsync();
        return new Grid { Children = { _host, _dispenseDialog, _toast } };
    }

    private async Task LoadAsync()
    {
        try
        {
            if (_inventoryPage != null) _inventoryPage.SetMedicineOptions(await _medicines.GetActiveAsync(ClinicId));
            else _prescriptionsPage.SetVets(await _users.GetActiveVetsAsync(ClinicId));
        }
        catch (SqlException) { /* shown by the refresh below */ }

        // Dashboard shortcuts: "status:LowStock" filters the medicines; "dispense:12" opens that prescription's dialog.
        string intent = StartIntent ?? string.Empty;
        if (_inventoryPage != null && intent.StartsWith("status:", StringComparison.Ordinal) &&
            Enum.TryParse<StockStatus>(intent.Substring(7), out var status))
        {
            _inventoryPage.ApplyStatus(status);   // refreshes
            return;
        }
        await RefreshAsync();
        if (_prescriptionsPage != null && intent.StartsWith("dispense:", StringComparison.Ordinal) && int.TryParse(intent.Substring(9), out int id) && CanManage)
        {
            try
            {
                var p = (await GetPrescriptionsToDispenseAsync(null, null, null, null)).FirstOrDefault(x => x.PrescriptionId == id);
                if (p?.CanDispense == true) await OpenDispenseAsync(new[] { p });
            }
            catch (SqlException) { /* the list shows the error */ }
        }
    }

    /// <summary>Dashboard shortcut: "status:LowStock" (Medicines) or "dispense:{PrescriptionID}" (Prescriptions).</summary>
    public string StartIntent { get; set; }

    private async Task RefreshAsync()
    {
        int mine = ++_refreshNumber;
        try
        {
            if (_inventoryPage != null) await RefreshInventoryAsync(mine);
            else await RefreshPrescriptionsAsync(mine);
        }
        catch (SqlException)
        {
            if (mine != _refreshNumber) return;
            _inventoryPage?.ShowError(DatabaseError);
            _prescriptionsPage?.ShowError(DatabaseError);
        }
    }

    private async Task RefreshInventoryAsync(int mine)
    {
        var page = _inventoryPage;
        var summary = await GetStockSummaryAsync();
        if (page.CurrentTab == 0)
        {
            var result = await GetMedicinesAsync(page.CurrentSearch, page.CurrentCategory, page.CurrentStatus, _medicinesPage);
            if (result.Items.Count == 0 && _medicinesPage > 1)
            {
                _medicinesPage--;
                result = await GetMedicinesAsync(page.CurrentSearch, page.CurrentCategory, page.CurrentStatus, _medicinesPage);
            }
            if (mine != _refreshNumber) return;
            page.ShowSummary(summary);
            page.ShowMedicines(result);
        }
        else
        {
            var result = await GetStockHistoryAsync(page.HistoryMedicineId, page.HistoryType, page.HistoryFrom, page.HistoryTo, _historyPage);
            if (mine != _refreshNumber) return;
            page.ShowSummary(summary);
            page.ShowHistory(result);
        }
    }

    private async Task RefreshPrescriptionsAsync(int mine)
    {
        var page = _prescriptionsPage;
        var summary = await _prescriptions.GetSummaryAsync(ClinicId, DateTime.Today);
        if (page.CurrentTab == 0)
        {
            var items = await GetPrescriptionsToDispenseAsync(page.CurrentSearch, page.CurrentVetId, page.CurrentFrom, page.CurrentTo);
            if (mine != _refreshNumber) return;
            page.ShowSummary(summary);
            page.ShowToDispense(items);
        }
        else
        {
            var result = await GetDispensedPrescriptionsAsync(page.CurrentSearch, page.CurrentVetId, page.CurrentFrom, page.CurrentTo, _dispensedPage);
            if (result.Items.Count == 0 && _dispensedPage > 1)
            {
                _dispensedPage--;
                result = await GetDispensedPrescriptionsAsync(page.CurrentSearch, page.CurrentVetId, page.CurrentFrom, page.CurrentTo, _dispensedPage);
            }
            if (mine != _refreshNumber) return;
            page.ShowSummary(summary);
            page.ShowDispensed(result);
        }
    }

    /// <summary>After a change: the table, the cards and the history dropdown (names may have changed).</summary>
    private async Task ReloadInventoryAsync()
    {
        try { _inventoryPage.SetMedicineOptions(await _medicines.GetActiveAsync(ClinicId)); }
        catch (SqlException) { /* the refresh shows the error */ }
        await RefreshAsync();
    }

    private IReadOnlyList<MenuEntry> MenuFor(Medicine m)
    {
        var entries = new List<MenuEntry>();
        if (CanManage)
        {
            entries.Add(new MenuEntry("Adjust stock", "package", () => _ = OpenAdjustAsync(m)));
            entries.Add(new MenuEntry("Edit", "pencil", () => _ = OpenFormAsync(m)));
        }
        entries.Add(new MenuEntry("View details", "eye", () => _ = OpenDetailsAsync(m.MedicineId)));
        entries.Add(new MenuEntry("View history", "clock", () => _inventoryPage.ShowHistoryOf(m.MedicineId)));
        if (CanManage)
            entries.Add(new MenuEntry("Archive", "archive", () => ConfirmArchive(m), Danger: true,
                Enabled: m.StockQty == 0, DisabledHint: "Only medicines with 0 stock can be archived"));
        return entries;
    }

    // Panels ---------------------------------------------------------------------------------------------------

    private async Task OpenFormAsync(Medicine medicine)
    {
        if (!CanManage) return;
        var form = new MedicineFormView(medicine);
        form.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        form.SaveRequested += async (s, e) =>
        {
            form.SetBusy(true);
            try
            {
                var result = medicine == null ? await AddMedicineAsync(form.Read()) : await UpdateMedicineAsync(medicine, form.Read());
                if (!result.Ok) { form.ShowResult(result); return; }
                await _host.Panel.HideAsync();
                ShowToast(result.Message);
                await ReloadInventoryAsync();
            }
            catch (SqlException) { form.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { form.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(form, MedicineFormView.PanelWidth);
    }

    private async Task OpenStockInAsync(Medicine medicine)
    {
        if (!CanManage) return;
        var current = await FreshAsync(medicine);
        if (current == null) return;
        var view = new StockInView(current);
        view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        view.SaveRequested += async (s, e) =>
        {
            view.SetBusy(true);
            try
            {
                var result = await StockInAsync(current, view.Read());
                if (!result.Ok) { view.ShowResult(result); return; }
                await _host.Panel.HideAsync();
                ShowToast(result.Message);
                await RefreshAsync();
            }
            catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { view.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(view, StockInView.PanelWidth);
    }

    private async Task OpenAdjustAsync(Medicine medicine)
    {
        if (!CanManage) return;
        var current = await FreshAsync(medicine);
        if (current == null) return;
        var view = new AdjustStockView(current);
        view.CancelRequested += async (s, e) => await _host.Panel.HideAsync();
        view.SaveRequested += async (s, e) =>
        {
            view.SetBusy(true);
            try
            {
                var result = await AdjustStockAsync(current, view.Read());
                if (!result.Ok) { view.ShowResult(result); return; }
                await _host.Panel.HideAsync();
                ShowToast(result.Message);
                await RefreshAsync();
            }
            catch (SqlException) { view.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { view.SetBusy(false); }
        };
        await _host.Panel.ShowAsync(view, AdjustStockView.PanelWidth);
    }

    private async Task OpenDetailsAsync(int medicineId)
    {
        try
        {
            var m = await GetMedicineByIdAsync(medicineId);
            if (m == null) { await RefreshAsync(); return; }
            var history = await GetStockHistoryAsync(medicineId, null, null, null, 1, 20);
            var view = new MedicineDetailsView(m, history.Items, history.TotalCount, CanManage);
            view.CloseRequested += async (s, e) => await _host.Panel.HideAsync();
            view.EditRequested += async (s, e) => await OpenFormAsync(m);
            view.AdjustRequested += async (s, e) => await OpenAdjustAsync(m);
            view.StockInRequested += async (s, e) => await OpenStockInAsync(m);
            view.AllHistoryRequested += async (s, e) => { await _host.Panel.HideAsync(); _inventoryPage.ShowHistoryOf(medicineId); };
            await _host.Panel.ShowAsync(view, MedicineDetailsView.PanelWidth);
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t open this medicine", DatabaseError, "circle-alert");
        }
    }

    /// <summary>The medicine as saved now (the stock may have changed since the table loaded).</summary>
    private async Task<Medicine> FreshAsync(Medicine medicine)
    {
        try
        {
            var current = await GetMedicineByIdAsync(medicine.MedicineId);
            if (current == null) await RefreshAsync();
            return current;
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t open this medicine", DatabaseError, "circle-alert");
            return null;
        }
    }

    private void ConfirmArchive(Medicine m)
    {
        if (!CanManage || m.StockQty != 0) return;
        _pendingConfirm = async () =>
        {
            _host.Dialog.SetBusy(true);
            try
            {
                var result = await ArchiveMedicineAsync(m);
                if (!result.Ok) { _host.Dialog.ShowResult(result); return; }
                await CloseDialogAsync();
                ShowToast(result.Message);
                await ReloadInventoryAsync();
            }
            catch (SqlException) { _host.Dialog.ShowResult(OperationResult.Fail(DatabaseError)); }
            finally { _host.Dialog.SetBusy(false); }
        };
        _ = _host.Dialog.ShowConfirmAsync("Archive " + m.MedicineName + "?",
            "It disappears from the stock list and the consultation. Old prescriptions and bills keep showing it.",
            "Archive", danger: true, icon: "archive");
    }

    // Dispense ---------------------------------------------------------------------------------------------------

    private async Task OpenDispenseAsync(IReadOnlyList<PharmacyPrescription> items)
    {
        if (!CanManage || items.Count == 0) return;
        _dispensing = items;
        await _dispenseDialog.ShowAsync(items);
    }

    private async Task ConfirmDispenseAsync()
    {
        var items = _dispensing;
        if (items == null) return;
        _dispenseDialog.SetBusy(true);
        var done = new List<PharmacyPrescription>();
        var problems = new List<string>();
        try
        {
            foreach (var p in items)
            {
                var (result, saved) = await DispenseAsync(p);
                if (saved) done.Add(p);
                else problems.Add(result.Message);
            }
        }
        catch (SqlException)
        {
            problems.Add(DatabaseError);
        }
        finally
        {
            _dispenseDialog.SetBusy(false);
        }

        if (done.Count == 0)
        {
            _dispenseDialog.ShowError(string.Join(" ", problems));
            await RefreshAsync();
            return;
        }

        bool print = _dispenseDialog.PrintLabel;
        await _dispenseDialog.HideAsync();
        _dispensing = null;
        ShowToast("Dispensed. It goes on " + Possessive(done[0].OwnerName) + " bill." + (problems.Count > 0 ? " " + string.Join(" ", problems) : string.Empty));
        await RefreshAsync();
        if (print)
        {
            foreach (var p in done) p.DispensedAt = DateTime.Now;
            await PrintLabelsAsync(done);
        }
    }

    private static string Possessive(string name) => string.IsNullOrEmpty(name) ? "the owner's" : name + (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? "'" : "'s");

    // Printing ---------------------------------------------------------------------------------------------------

    private async Task PrintLabelsAsync(IReadOnlyList<PharmacyPrescription> items)
    {
        if (!CanManage) return;
        try
        {
            var clinic = await _clinics.GetByIdAsync(ClinicId);
            string html = MedicineLabelDocument.Build(items, clinic, DateTime.Now);
            string path = System.IO.Path.Combine(FileSystem.CacheDirectory, "VetCare-medicine-label-" + items[0].PrescriptionId + ".html");
            await File.WriteAllTextAsync(path, html);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Print", new ReadOnlyFile(path)));
        }
        catch (SqlException)
        {
            await ShowInfoAsync("Can’t print right now", DatabaseError, "circle-alert");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowInfoAsync("Can’t print right now", "The label couldn’t be created: " + ex.Message, "circle-alert");
        }
    }

    // Dialogs and toast ------------------------------------------------------------------------------------------

    private void ShowToast(string message) => _toast?.Show(message);

    private Task ShowInfoAsync(string title, string message, string icon)
    {
        _pendingConfirm = null;
        return _host.Dialog.ShowConfirmAsync(title, message, "OK", danger: false, icon: icon, showCancel: false);
    }

    private async Task ConfirmDialogAsync()
    {
        var action = _pendingConfirm;
        if (action == null) { await CloseDialogAsync(); return; }
        await action();
    }

    private async Task CloseDialogAsync()
    {
        _pendingConfirm = null;
        await _host.Dialog.HideAsync();
    }
}
