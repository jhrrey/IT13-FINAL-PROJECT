# VetCare – Pharmacy Module ("Medicines & stock" and "Prescriptions")

**Module:** 6 – Pharmacy (Core)
**Sidebar location:** PHARMACY & BILLING → Medicines & stock, Prescriptions
**Used by:** Cashier/Staff (full access), Veterinarian (prescribe, view stock), Administrator (view only)
**Tables:** Medicine, Prescription, InventoryTransaction (linked to MedicalRecord, Vaccination, BillItem, User)

---

## 1. What this module does

The Pharmacy module manages the clinic's medicines from the moment they arrive until they're given to a pet. It has three jobs:

1. **Medicines & stock:** keep a list of all medicines and vaccines, with their price, stock level, and expiry date.
2. **Prescriptions:** the vet prescribes medicine during a consultation, and the staff **dispenses** it.
3. **Stock history:** every stock change (stock in, dispense, adjustment) is saved, so the count can always be checked.

| Role | Access |
|---|---|
| Cashier/Staff | Add/edit medicines, stock in, adjust stock, dispense prescriptions, view history |
| Veterinarian | Create prescriptions (during consultation), view medicines and stock |
| Administrator | View only (medicines, prescriptions, stock history) |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Medicine table
| Field | Type | Notes |
|---|---|---|
| MedicineID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | Set automatically from `Session.ClinicID` |
| MedicineName | VARCHAR(100) | Required (e.g., "Amoxicillin 250 mg") |
| Category | VARCHAR(50) | Antibiotic, Antifungal, Vaccine, Dewormer, Anti-parasitic, Pain relief, Topical, Supplement, Other |
| Unit | VARCHAR(20) | Tablet, Capsule, Bottle, Vial, Tube, Sachet, mL |
| UnitPrice | DECIMAL(10,2) | Selling price per unit (₱) |
| StockQuantity | INT | Current stock; only changed through transactions |
| ReorderLevel | INT | "Low stock" warning when stock reaches this number |
| ExpiryDate | DATE | Nearest expiry date of the current stock |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Prescription table
| Field | Type | Notes |
|---|---|---|
| PrescriptionID | UNIQUEIDENTIFIER (PK) | Created automatically |
| RecordID | UNIQUEIDENTIFIER (FK) | The consultation it came from |
| MedicineID | UNIQUEIDENTIFIER (FK) | The prescribed medicine |
| Quantity | INT | How many units to give |
| Dosage | VARCHAR(100) | e.g., "1 tablet" |
| Instructions | VARCHAR(255) | e.g., "Twice a day for 7 days" |
| IsDispensed | BIT | 0 = waiting, 1 = given to the owner |
| Sync fields | — | Handled automatically |

### InventoryTransaction table
| Field | Type | Notes |
|---|---|---|
| TransactionID | UNIQUEIDENTIFIER (PK) | Created automatically |
| MedicineID | UNIQUEIDENTIFIER (FK) | The medicine affected |
| UserID | UNIQUEIDENTIFIER (FK) | Who made the change |
| TransactionType | VARCHAR(20) | Stock In, Dispensed, Vaccine Used, Adjustment, Expired, Damaged |
| Quantity | INT | Positive = added, negative = removed |
| TransactionDate | DATETIME | Set automatically |
| Remarks | VARCHAR(255) | Reason or reference (e.g., supplier, prescription) |
| Sync fields | — | Handled automatically |

### Stock status (calculated, not saved)
| Status | Rule |
|---|---|
| **In stock** | Stock is above the reorder level |
| **Low stock** | Stock is at or below the reorder level |
| **Out of stock** | Stock is 0 |
| **Expiring soon** | Expiry date is within 30 days |
| **Expired** | Expiry date has passed |

---

## 3. Screens

### A. Medicines & stock page (2 tabs)
- **Summary cards:** Total items, Low stock, Out of stock, Expiring in 30 days.
- **Tab 1 – Medicines (default)**
  - Toolbar: search (name), Category filter, Status filter, and an **Add medicine** button.
  - Table columns: Medicine (name + unit), Category, Price, Stock (number + a small bar compared to the reorder level), Reorder level, Expiry date, Status, Sync, Actions.
  - Row actions: **Stock in**, **Adjust stock**, Edit, View history, Archive.
- **Tab 2 – Stock history**
  - All inventory transactions with Date, Medicine, Type, Quantity (+/–), Done by, Remarks, Sync.
  - Filters: date range, medicine, transaction type.

### B. Add / Edit medicine panel
- Medicine name, Category, Unit, Unit price, Reorder level, Expiry date.
- When adding: **Starting stock** (saved as the first "Stock In" transaction).
- The stock number **can't be edited here**. It only changes through Stock in, Adjust stock, or Dispense.

### C. Stock in panel
- Quantity received, New expiry date, Supplier / reference (saved in Remarks).
- Shows the stock before and after: "Current: 12 → New: 112".

### D. Adjust stock panel
- Type: **Count correction**, **Expired**, or **Damaged**.
- Quantity (to add or remove) and a **required reason**.
- Shows the stock before and after.

### E. Medicine details panel
- Medicine info, current stock, status, and the full transaction history of that medicine.

### F. Prescriptions page (2 tabs)
- **Tab 1 – To dispense (default):** prescriptions that are waiting.
  - Columns: Date, Pet, Owner, Vet, Medicine, Qty, Dosage, Instructions, Stock available, Action.
  - Action: **Dispense**. It's disabled with an "Not enough stock" note if the stock is too low.
- **Tab 2 – Dispensed:** history of prescriptions already given, with the dispensed date and who gave it.
- Toolbar: search (pet, owner, medicine), date range, vet filter.

### G. Dispense dialog
- Shows the pet, owner, medicine, quantity, dosage, and instructions.
- Stock before and after: "Stock: 24 → 10".
- A checkbox to **print a medicine label** for the bottle or pack.
- Buttons: Cancel, **Dispense**.

### H. Medicine label (print)
- A small sticker-size label with the clinic name, pet name, owner name, medicine, dosage, instructions, date, and vet name.

---

## 4. Rules (checked in PharmacyController)

1. **Cashier/Staff** manage medicines and dispense. **Vets** create prescriptions only during a consultation. **Admins** can only view.
2. Users only see medicines and prescriptions from **their own clinic** (`Session.ClinicID`).
3. **Medicine name, category, unit, and unit price are required.** The same name and unit can't be added twice in one clinic.
4. Unit price must be 0 or more, and the reorder level must be 0 or more.
5. **Stock is never edited directly.** Every change is saved as an **InventoryTransaction**, so there's always a record of who changed it, when, and why.
6. **Dispensing** is only allowed if the prescription isn't dispensed yet, there's **enough stock**, and the medicine **isn't expired**.
7. Dispensing is saved in **one SQL transaction**:
   - `Prescription.IsDispensed = 1`
   - The quantity is subtracted from `Medicine.StockQuantity`
   - A "Dispensed" InventoryTransaction is added
   - The medicine is added to the visit's **bill** as a BillItem (the item type is *Medicine*)
8. **Vaccines** (the category *Vaccine*) are subtracted automatically when the vet records a vaccination, as a "Vaccine Used" transaction. They aren't dispensed here.
9. An **adjustment needs a reason**, and it can't make the stock go below 0.
10. **Expired** medicines can't be dispensed or used. They should be removed with an "Expired" adjustment.
11. Low-stock, out-of-stock, and expiring items are shown with **warning badges** and counted on the summary cards.
12. Medicines are **archived, not deleted**, and only when their stock is 0, so old prescriptions and bills stay complete.

> **Optional ERD additions:**
> - **MedicineBatch** table (MedicineID, BatchNo, Quantity, ExpiryDate) if you want to track a separate expiry date for each delivery.
> - **Supplier** table (SupplierName, ContactNo) if you want to keep a supplier list instead of typing it in Remarks.

---

## 5. Offline / online sync

- Stock in, adjustments, and dispensing **work offline**.
- Each change is saved as an InventoryTransaction with SyncStatus = **Pending** and added to the SyncQueue.
- **The stock count is rebuilt from transactions when syncing.** The server adds up all the transactions for each medicine instead of copying one computer's number. This way, two computers dispensing offline at the same time won't overwrite each other's counts.
- If the rebuilt stock goes **below 0**, for example because two computers dispensed the last units, the medicine is marked **Conflict**. The staff then fixes it with a count correction.
- Prescriptions created offline by the vet show up in the "To dispense" list on other computers after syncing.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Medicine.cs`, `Prescription.cs`, `InventoryTransaction.cs` |
| Model | `Models/Repositories/MedicineRepository.cs` |
| Model | `Models/Repositories/PrescriptionRepository.cs` |
| Model | `Models/Repositories/InventoryRepository.cs` |
| Model (used) | `BillingRepository.cs` (adds the BillItem when dispensing) |
| Controller | `Controllers/PharmacyController.cs` |
| View | `Views/Staff/InventoryPage.xaml` (Medicines & stock) |
| View | `Views/Staff/PrescriptionsPage.xaml` (To dispense / Dispensed) |
| View | `Views/Shared/MedicineFormView.xaml` (add/edit medicine) |
| View | `Views/Shared/StockInView.xaml`, `AdjustStockView.xaml` |
| View | `Views/Shared/DispenseDialog.xaml`, `MedicineLabelPrint.xaml` |
| Helper | `Helpers/StockStatusHelper.cs` (low/out/expiring status) |

**PharmacyController methods**
- `GetMedicines(search, category, status)`
- `GetMedicineById(medicineId)`
- `GetStockSummary()`: total items, low stock, out of stock, expiring
- `AddMedicine(medicine, startingStock)`
- `UpdateMedicine(medicine)`
- `StockIn(medicineId, quantity, expiryDate, remarks)`
- `AdjustStock(medicineId, type, quantity, reason)`
- `ArchiveMedicine(medicineId)`
- `GetStockHistory(medicineId, type, dateFrom, dateTo)`
- `GetPrescriptionsToDispense(search, vetId, dateFrom, dateTo)`
- `GetDispensedPrescriptions(search, dateFrom, dateTo)`
- `AddPrescription(prescription)` (called by MedicalRecordController)
- `Dispense(prescriptionId, printLabel)`
- `UseVaccine(medicineId, vaccinationId)` (called by VaccinationController)

---

## 7. UI build prompt

```
Build the Pharmacy pages for the VetCare veterinary clinic management system: "Medicines & stock" and "Prescriptions". They sit to the right of the existing VetCare sidebar, under PHARMACY & BILLING. The active page has a light cyan #B8F0F2 background and bold navy text in the sidebar. Keep the same clean, light, rounded style.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4, Light red: #FDE2E2
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500)
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

STOCK STATUS PILLS (use everywhere)
- In stock = mint #C4F1E1, dark green text #1E6B4F
- Low stock = amber #FFF4E0, dark amber text #8A5A00
- Out of stock = light red #FDE2E2, red text #B42318
- Expiring soon = light orange #FDE8E1, orange text #D4421C
- Expired = light gray #EEF1F4, gray text, strikethrough expiry date

=== PAGE 1: MEDICINES & STOCK ===

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PHARMACY & BILLING"
- Title (32px, Nunito 800, navy): "Medicines & stock"
- Subtitle (gray): "Know what's on the shelf, and what's running low."
- Right side: orange button "+ Add medicine"

SUMMARY CARDS (4 in a row)
- "Total items" (package icon, cyan circle)
- "Low stock" (trending-down icon, amber circle)
- "Out of stock" (package-x icon, light-red circle, red icon)
- "Expiring in 30 days" (calendar-clock icon, light-orange circle, orange icon)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label. Clicking a card filters the table by that status.

TABS
- "Medicines" (active: orange text + 2px orange underline) and "Stock history" (gray).

TAB 1 – MEDICINES
- Toolbar card: search input ("Search medicine"), "All categories" dropdown (Antibiotic, Antifungal, Vaccine, Dewormer, Anti-parasitic, Pain relief, Topical, Supplement, Other), "All status" dropdown.
- Table inside a white card:
  - Columns: Medicine (40px rounded-square icon tile — pill icon on cyan for most, syringe icon on mint for vaccines — then the name in bold navy and the unit below in gray, e.g. "per tablet"), Category (light gray pill), Price ("₱12.00"), Stock (the number in bold, and below it a thin 80px progress bar: green when above reorder level, amber when low, red when 0), Reorder level, Expiry date, Status pill, Sync badge, actions.
  - Actions: small navy outline button "Stock in" (plus icon), and a three-dot menu with "Adjust stock", "Edit", "View history", "Archive" (red, disabled unless stock is 0).
  - Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red, tooltip "Stock count needs checking").
  - Row hover: #F2F8FB. Clicking a row opens the medicine details panel.
  - Pagination: "Showing 1–10 of 64 medicines".
  - Empty state: package icon in a cyan circle, "No medicines yet", orange "Add medicine" button.

TAB 2 – STOCK HISTORY
- Toolbar: date range picker, "All medicines" dropdown, "All types" dropdown (Stock In, Dispensed, Vaccine Used, Adjustment, Expired, Damaged).
- Table columns: Date & time, Medicine, Type pill (Stock In = mint, Dispensed = pale blue, Vaccine Used = cyan, Adjustment = amber, Expired/Damaged = light gray), Quantity (green "+100" or red "−14"), Done by, Remarks, Sync badge.

ADD / EDIT MEDICINE PANEL (slides in from the right, 460px wide, white, soft shadow, dimmed background)
- Title "Add medicine" / "Edit medicine" with a close (x) icon.
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  Medicine name*, Category* (dropdown), Unit* (dropdown: Tablet, Capsule, Bottle, Vial, Tube, Sachet, mL), Unit price* (with "₱" prefix), Reorder level* (number), Expiry date (date picker), and "Starting stock" (number, only when adding).
- When editing, show the current stock as read-only text with a gray note: "Use Stock in or Adjust stock to change the quantity."
- Inline red errors: "Required", "Price can't be negative", "This medicine already exists".
- Footer: gray outline "Cancel", orange "Save medicine".

STOCK IN PANEL (slides in, 420px)
- Title "Stock in" + the medicine name and unit under it.
- Fields: Quantity received* (number), New expiry date (date picker), Supplier / reference (text).
- A cyan info box: "Current stock: 12 → New stock: 112" (updates live).
- Footer: "Cancel", orange "Add stock".

ADJUST STOCK PANEL (slides in, 420px)
- Title "Adjust stock" + the medicine name.
- Type* as 3 toggle buttons: "Count correction", "Expired", "Damaged" (selected = cyan background, bold navy).
- For Count correction: a +/− switch and a Quantity input. For Expired/Damaged: Quantity to remove.
- Reason* (textarea, required).
- Amber info box: "Current stock: 24 → New stock: 20". Red error if the result goes below 0: "Stock can't go below 0".
- Footer: "Cancel", orange "Save adjustment".

MEDICINE DETAILS PANEL (slides in, 480px)
- Header: icon tile, medicine name (22px Nunito 800), category pill, unit, price.
- 3 stat chips: "Stock: 24 tablets", "Reorder at: 20", "Expires: Mar 15, 2027" (colored by status).
- Buttons: outline "Edit", outline "Adjust stock", orange "Stock in".
- "History" list: each row shows the date, type pill, quantity (+/− colored), who did it, and remarks.

=== PAGE 2: PRESCRIPTIONS ===

PAGE HEADER
- Eyebrow: "PHARMACY & BILLING"
- Title: "Prescriptions"
- Subtitle: "From the vet's note to the owner's hands."

SUMMARY CARDS (3 in a row)
- "To dispense" (clipboard-list icon, amber circle)
- "Dispensed today" (package-check icon, mint circle)
- "Waiting for stock" (package-x icon, light-red circle, red icon)

TABS
- "To dispense" (active, with an orange count pill) and "Dispensed".
- Toolbar: search ("Search pet, owner, or medicine"), date range, "All vets" dropdown.

TAB 1 – TO DISPENSE
- Table columns: Date (from the consultation), Pet (species icon + name), Owner, Vet, Medicine (name + unit), Qty, Dosage, Instructions (gray, cut off with "…"), Stock (e.g. "24 available" in green, or "5 available" in red when less than the quantity), Action.
- Action: orange small button "Dispense" (package-check icon). If the stock is too low, show a disabled gray button with a red note "Not enough stock".
- Rows from the same visit are grouped with a light gray group header: "Luna · Pedro Reyes · Dr. Grace Lim · Nov 16, 2026" and a button "Dispense all" on the right.
- Empty state: package-check icon in a mint circle, "All prescriptions dispensed", subtext "New prescriptions appear here after a consultation."

TAB 2 – DISPENSED
- Table columns: Dispensed date, Pet, Owner, Medicine, Qty, Dispensed by, Sync badge, and a "Reprint label" link.

DISPENSE DIALOG (centered modal, 480px, white, 12px radius)
- Title: "Dispense medicine"
- Info block: Pet + owner, Medicine (bold navy), Quantity, Dosage, Instructions (in a light cyan box so they're easy to read).
- Stock line: "Stock: 24 → 10 tablets" (amber if the new stock will be low).
- If the medicine is expiring soon, an amber warning: "Expires on Dec 01, 2026."
- Checkbox (checked by default): "Print medicine label".
- Note in gray: "This will also add ₱168.00 to the visit's bill."
- Buttons: "Cancel", orange "Dispense".
- After success: green toast "Dispensed. Added to Pedro Reyes's bill."

MEDICINE LABEL (print, 4 x 2.5 inch sticker, black and white)
- Top: clinic name (bold) and contact number.
- Pet name + owner name.
- Medicine name in large bold text, quantity.
- Dosage and instructions (large, easy to read).
- Bottom: date, "Dr. Grace Lim", and "For animal use only".

SAMPLE DATA (for preview, today = Nov 17, 2026)
Medicines:
- Amoxicillin 250 mg — Antibiotic — Capsule — ₱12.00 — Stock 140 — Reorder 50 — Exp Aug 20, 2027 — In stock
- Doxycycline 100 mg — Antibiotic — Tablet — ₱15.00 — Stock 18 — Reorder 30 — Exp May 02, 2027 — Low stock
- Itraconazole 100 mg — Antifungal — Capsule — ₱35.00 — Stock 24 — Reorder 20 — Exp Mar 15, 2027 — In stock
- Miconazole Cream 2% — Topical — Tube — ₱180.00 — Stock 6 — Reorder 5 — Exp Dec 01, 2026 — Expiring soon
- Otomax Ear Drops — Topical — Bottle — ₱650.00 — Stock 0 — Reorder 3 — Out of stock
- Pyrantel Dewormer — Dewormer — Tablet — ₱25.00 — Stock 95 — Reorder 40 — Exp Jan 10, 2028 — In stock
- Anti-rabies Vaccine — Vaccine — Vial — ₱350.00 — Stock 18 — Reorder 10 — Exp Jun 30, 2027 — In stock
- 5-in-1 (DHPPiL) Vaccine — Vaccine — Vial — ₱550.00 — Stock 9 — Reorder 10 — Exp Apr 18, 2027 — Low stock
- 4-in-1 (FVRCP) Vaccine — Vaccine — Vial — ₱600.00 — Stock 12 — Reorder 8 — Exp Feb 28, 2027 — In stock
- Multivitamin Syrup — Supplement — Bottle — ₱220.00 — Stock 3 — Reorder 5 — Exp Oct 05, 2026 — Expired
Prescriptions to dispense:
- Nov 16 — Luna — Pedro Reyes — Dr. Grace Lim — Itraconazole 100 mg — 14 — 1 capsule — Once a day for 14 days — 24 available
- Nov 16 — Luna — Pedro Reyes — Dr. Grace Lim — Miconazole Cream 2% — 1 — Thin layer — Apply twice a day on affected skin — 6 available
- Nov 15 — Max — Pedro Reyes — Dr. Grace Lim — Otomax Ear Drops — 1 — 4 drops — Each ear twice a day for 7 days — 0 available (Not enough stock)
- Nov 14 — Snow — Ana Villanueva — Dr. Maria Santos — Doxycycline 100 mg — 5 — 1/2 tablet — Once a day for 5 days — 18 available

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, side panels become full width, and tables scroll sideways if needed.
```
