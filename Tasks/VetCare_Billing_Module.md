# VetCare – Billing Module ("Bills & payments")

**Module:** 7 – Billing (Core)
**Sidebar location:** PHARMACY & BILLING → Bills & payments
**Used by:** Cashier/Staff (full access), Administrator (view only)
**Tables:** Bill, BillItem, Payment (linked to Appointment, Owner, Medicine, User)

---

## 1. What this module does

The Billing module is the **last step of a visit**. It collects all the charges from a completed appointment into one bill, records the owner's payment, and prints the receipt.

- Every bill is for **one completed appointment** and **one owner**.
- A bill's items come from: the **consultation fee**, **vaccines** given (Vaccination module), and **medicines** dispensed (Pharmacy module).
- An owner can pay **in full or in parts**, using cash or e-payment.
- All payments feed the **income reports** in the Reports module.

| Role | Access |
|---|---|
| Cashier/Staff | Create bills, add service items, receive payments, print receipts, void unpaid bills |
| Administrator | View all bills and payments (read only) |
| Veterinarian | No access |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Bill table
| Field | Type | Notes |
|---|---|---|
| BillID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | Set automatically from `Session.ClinicID` |
| AppointmentID | UNIQUEIDENTIFIER (FK) | The completed visit being billed |
| OwnerID | UNIQUEIDENTIFIER (FK) | Who pays (taken from the pet's owner) |
| CashierID | UNIQUEIDENTIFIER (FK → User) | The staff member who created the bill |
| TotalAmount | DECIMAL(10,2) | Sum of all bill item subtotals |
| Status | VARCHAR(20) | Unpaid, Partial, Paid, Void |
| BillDate | DATETIME | Set automatically |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### BillItem table
| Field | Type | Notes |
|---|---|---|
| BillItemID | UNIQUEIDENTIFIER (PK) | Created automatically |
| BillID | UNIQUEIDENTIFIER (FK) | The bill it belongs to |
| MedicineID | UNIQUEIDENTIFIER (FK, nullable) | Filled for Medicine and Vaccine items only |
| ItemType | VARCHAR(20) | Consultation, Medicine, Vaccine, Service |
| Description | VARCHAR(100) | e.g., "Consultation fee", "Itraconazole 100 mg" |
| Quantity | INT | Number of units |
| UnitPrice | DECIMAL(10,2) | Price per unit (₱) |
| Subtotal | DECIMAL(10,2) | Quantity × Unit price (calculated) |
| Sync fields | — | Handled automatically |

### Payment table
| Field | Type | Notes |
|---|---|---|
| PaymentID | UNIQUEIDENTIFIER (PK) | Created automatically |
| BillID | UNIQUEIDENTIFIER (FK) | The bill being paid |
| ReceivedBy | UNIQUEIDENTIFIER (FK → User) | The cashier who received it |
| AmountPaid | DECIMAL(10,2) | Amount applied to the bill |
| PaymentMethod | VARCHAR(20) | Cash, GCash, Maya, Card, Bank transfer |
| ReferenceNo | VARCHAR(50) | Required for non-cash payments |
| PaymentDate | DATETIME | Set automatically |
| Sync fields | — | Handled automatically |

### Bill status (updated automatically)
| Status | Rule |
|---|---|
| **Unpaid** | No payments yet |
| **Partial** | Some payment made, but there's still a balance |
| **Paid** | Balance is ₱0.00 |
| **Void** | Cancelled before any payment (kept for records) |

**Balance** = TotalAmount − (sum of all payments). It's calculated, not saved.

---

## 3. Screens

### A. Bills & payments page (3 tabs)
- **Summary cards:** Collected today (₱), Unpaid bills, Partially paid, Outstanding balance (₱).
- **Tab 1 – Ready to bill (default):** completed appointments that don't have a bill yet.
  - Columns: Visit date, Pet, Owner, Vet, Reason, Items found (e.g., "1 vaccine, 2 medicines"), Action **Create bill**.
- **Tab 2 – Bills:** every bill in the clinic.
  - Columns: Bill no., Date, Owner, Pet, Total, Paid, Balance, Status, Sync, Actions.
  - Filters: status, date range, search (bill no., owner, pet).
  - Actions: View, **Receive payment**, Print receipt, Void (unpaid only).
- **Tab 3 – Payments:** a list of every payment received.
  - Columns: Date & time, Bill no., Owner, Method, Reference no., Amount, Received by, Sync.
  - Filters: date range, payment method. A total of the filtered payments is shown at the bottom.

### B. Create bill page
- **Header:** owner and contact no., pet, vet, visit date, and reason.
- **Items table (filled in automatically):**
  - Consultation fee: the default amount from system settings, which the cashier can change.
  - Vaccines given during the visit (from Vaccination).
  - Medicines already dispensed for the visit (from Pharmacy).
  - A note for prescriptions **not yet dispensed**: "2 prescriptions not dispensed yet — they'll be added when dispensed."
- **"+ Add service"** button for extra charges (e.g., grooming, wound dressing, lab test), with a description, quantity, and price.
- **Totals box:** Subtotal and **Total**.
- Buttons: **Save as unpaid**, and **Save & receive payment**.

### C. Receive payment panel
- Bill no., owner, total, amount already paid, and **balance due** (large).
- **Payment method** chips: Cash, GCash, Maya, Card, Bank transfer.
- **Amount to pay** (defaults to the full balance; can be lower for partial payment).
- For **Cash**: an "Amount received" field, and the **change** is shown automatically.
- For **non-cash**: a required **Reference no.** field.
- Buttons: Cancel, **Confirm payment**. After saving, the option to print the receipt.

### D. Bill details page
- Bill no., status, sync status, owner, pet, vet, visit date, and cashier.
- Items table with subtotals and the total.
- **Payments** list (date, method, reference, amount, received by).
- **Balance** box.
- Buttons depending on the status: Receive payment, Print receipt, Add service (unpaid only), Void (unpaid only).

### E. Receipt (print)
- An official receipt with the clinic name, address, and contact no., bill no., date, owner, pet, the list of items, total, payments, balance, and the cashier's name.
- Can be printed on an 80 mm receipt printer or on A5 paper.

---

## 4. Rules (checked in BillingController)

1. Only **Cashier/Staff** can create bills, receive payments, and void bills. Administrators can only view.
2. Users only see bills from **their own clinic** (`Session.ClinicID`).
3. A bill can only be created for an appointment that is **Completed**, and **only one bill per appointment**.
4. **Subtotal = Quantity × Unit price**, and **Total = the sum of all subtotals**. Both are calculated by the system, not typed in.
5. Quantity must be more than 0, and unit price must be 0 or more.
6. **Medicine and Vaccine items** come from Pharmacy and Vaccination. They **can't be edited or removed** in Billing, so the bill always matches the stock that was used.
7. Medicines dispensed **after** the bill is created are **added to the bill automatically**, as long as it isn't Paid or Void yet.
8. **Consultation and Service items** can be edited or removed while the bill is **Unpaid**.
9. **Payments:** the amount must be more than ₱0 and can't be more than the balance. For cash, the amount received can be higher, and the change is shown but not saved.
10. A **reference no. is required** for GCash, Maya, Card, and Bank transfer.
11. The **bill status updates automatically** after each payment (Unpaid → Partial → Paid).
12. **Paid bills are locked.** Their items and payments can't be changed.
13. **Payments can't be edited or deleted** once saved.
14. **Void** is only allowed when there are **no payments yet**, and the cashier must confirm. Voided bills are kept, not deleted. Dispensed medicines aren't returned to stock automatically, so the staff can use Adjust stock if they were really returned.
15. An owner with an **unpaid or partial bill** shows the balance in the Owner module, and the owner can't be archived.

> **Optional ERD additions:**
> - `BillNo VARCHAR(20)` on Bill, for a readable receipt number (e.g., **VC-2026-0001**), since GUIDs are too long to print.
> - `DiscountAmount DECIMAL(10,2)` on Bill, for senior citizen, PWD, or promo discounts.
> - `Remarks VARCHAR(255)` on Bill, to save the reason for voiding.
> - A `ServiceFee` table (ServiceName, Price), to keep a price list for consultation and services instead of one default fee.

---

## 5. Offline / online sync

- Bills and payments **can be created offline**, so the clinic can still collect payments without internet.
- They're saved with SyncStatus = **Pending** and added to the SyncQueue.
- **Payments are never overwritten.** Each payment is a new row, so payments from different computers just add up.
- When syncing, the server **recalculates each bill's total and status** from its items and payments, instead of copying one computer's numbers.
- If two computers create a bill for the **same appointment** while offline, the second one is marked **Conflict**, so the cashier can void the duplicate. If both were already paid, the Administrator must check it.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Bill.cs`, `BillItem.cs`, `Payment.cs` |
| Model | `Models/Repositories/BillingRepository.cs` (Bill + BillItem + Payment) |
| Model (used) | `AppointmentRepository.cs`, `PrescriptionRepository.cs`, `VaccinationRepository.cs` |
| Controller | `Controllers/BillingController.cs` |
| View | `Views/Staff/BillingPage.xaml` (Ready to bill, Bills, Payments) |
| View | `Views/Staff/CreateBillPage.xaml` |
| View | `Views/Staff/BillDetailsPage.xaml` |
| View | `Views/Shared/ReceivePaymentView.xaml` |
| View | `Views/Shared/ReceiptPrint.xaml` |
| Helper | `Helpers/CurrencyHelper.cs` (₱ format), `Helpers/BillNumberHelper.cs` |

**BillingController methods**
- `GetReadyToBill(search)`
- `GetBills(search, status, dateFrom, dateTo)`
- `GetPayments(method, dateFrom, dateTo)`
- `GetBillingSummary()`: collected today, unpaid, partial, outstanding balance
- `GetBillById(billId)`
- `PrepareBill(appointmentId)`: builds the item list from the visit
- `CreateBill(appointmentId, items)`
- `AddServiceItem(billId, item)`
- `UpdateServiceItem(billItem)`
- `RemoveServiceItem(billItemId)`
- `AddMedicineItem(appointmentId, prescription)` (called by PharmacyController when dispensing)
- `AddVaccineItem(appointmentId, vaccination)` (called by VaccinationController)
- `ReceivePayment(billId, amount, method, referenceNo)`
- `VoidBill(billId)`
- `GetReceiptData(billId)`

---

## 7. UI build prompt

```
Build the "Bills & payments" pages for the VetCare veterinary clinic management system. They sit to the right of the existing VetCare sidebar, with "Bills & payments" active in the sidebar under PHARMACY & BILLING (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style. All money is in Philippine pesos (₱) with 2 decimals.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500); money amounts use tabular numbers
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

BILL STATUS PILLS (use everywhere)
- Unpaid = light orange #FDE8E1, orange text #D4421C
- Partial = amber #FFF4E0, dark amber text #8A5A00
- Paid = mint #C4F1E1, dark green text #1E6B4F, check icon
- Void = light gray #EEF1F4, gray text, strikethrough amount

=== PAGE 1: BILLS & PAYMENTS ===

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PHARMACY & BILLING"
- Title (32px, Nunito 800, navy): "Bills & payments"
- Subtitle (gray): "Clear bills, easy payments, happy goodbyes."

SUMMARY CARDS (4 in a row)
- "Collected today" (wallet icon, mint circle) — shown as "₱8,450.00"
- "Unpaid bills" (receipt icon, light-orange circle, orange icon)
- "Partially paid" (hourglass icon, amber circle)
- "Outstanding balance" (circle-dollar-sign icon, pale-blue circle) — shown as "₱1,970.00"
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label.

TABS
- "Ready to bill" (active: orange text + 2px orange underline, with an orange count pill), "Bills", "Payments".

TAB 1 – READY TO BILL
- Table inside a white card:
  - Columns: Visit date, Pet (species icon + name), Owner, Vet, Reason pill, Items found (small gray text: "Consultation, 1 vaccine"), Action.
  - Action: orange small button "Create bill" (receipt icon).
- Empty state: receipt icon in a mint circle, "Nothing to bill", subtext "Completed visits will appear here."

TAB 2 – BILLS
- Toolbar: search ("Search bill no., owner, or pet"), "All status" dropdown, date range picker.
- Table columns: Bill no. (bold navy, e.g. "VC-2026-0142"), Date, Owner, Pet, Total, Paid (green), Balance (orange bold if above ₱0, gray "—" if ₱0), Status pill, Sync badge, actions.
- Actions: small navy outline button "Receive payment" (only when Unpaid or Partial), and a three-dot menu with "View", "Print receipt", "Void" (red, only when Unpaid with no payments).
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red, tooltip "Possible duplicate bill").
- Row hover: #F2F8FB. Clicking a row opens the bill details.
- Pagination: "Showing 1–10 of 238 bills".

TAB 3 – PAYMENTS
- Toolbar: date range picker, "All methods" dropdown (Cash, GCash, Maya, Card, Bank transfer).
- Table columns: Date & time, Bill no., Owner, Method (pill with icon: banknote = Cash, smartphone = GCash/Maya, credit-card = Card, landmark = Bank transfer), Reference no. (gray "—" for cash), Amount (bold), Received by, Sync badge.
- Sticky total bar at the bottom of the card: "Total for selected period: ₱24,380.00" (bold navy).

=== PAGE 2: CREATE BILL ===
- Back link "← Bills & payments". Title (28px Nunito 800 navy): "Create bill".
- Visit summary card: pet (species icon + name + "Dog · Aspin"), owner (name + contact no.), vet, visit date and time, reason pill.
- Items card (title "Bill items"):
  - Table columns: Type pill (Consultation = cyan, Medicine = pale blue, Vaccine = mint, Service = light gray), Description, Qty, Unit price, Subtotal, and a trash icon.
  - Consultation and Service rows have editable Qty and Unit price inputs (small, 40px tall). Medicine and Vaccine rows are read-only with a small lock icon and a gray tooltip "From pharmacy — can't be edited here".
  - Below the table: dashed-border button "+ Add service". It adds a row with a Description input, Qty, and Unit price.
  - Amber info box (only if needed): "2 prescriptions not dispensed yet. They'll be added to this bill when dispensed."
- Totals card on the right (sticky, 320px): "Subtotal ₱1,070.00", divider, "Total" in 28px Nunito 800 navy "₱1,070.00".
- Footer buttons: gray outline "Cancel", navy outline "Save as unpaid", orange "Save & receive payment".

=== RECEIVE PAYMENT PANEL (slides in from the right, 440px wide) ===
- Title "Receive payment" + bill no. in gray, close icon.
- Summary box (light cyan background): Total ₱1,070.00, Paid ₱500.00, and "Balance due" in large bold navy "₱570.00".
- Payment method: 5 chips in a row with icons — Cash, GCash, Maya, Card, Bank transfer (selected = cyan background, bold navy text, navy border).
- Amount to pay* (₱ prefix, filled with the full balance; can be changed for a partial payment). Small gray note: "Leave a balance for partial payment."
- If Cash: "Amount received" (₱ input) and a mint box showing "Change: ₱430.00" (updates live).
- If not Cash: "Reference no.*" input (placeholder "e.g. 1029384756").
- Inline red errors: "Enter an amount above ₱0", "Amount can't be more than the balance", "Reference no. is required", "Amount received is less than the amount to pay".
- Footer: "Cancel", orange "Confirm payment".
- After success: green toast "Payment received. Bill is now Paid." and a dialog asking "Print receipt?" with "Not now" / orange "Print".

=== PAGE 3: BILL DETAILS ===
- Back link. Header row: bill no. (28px Nunito 800 navy), status pill, sync badge. Right side buttons: outline "Print receipt", outline "Add service" (Unpaid only), orange "Receive payment" (Unpaid/Partial only), and a three-dot menu with "Void bill" (red).
- Info card in 2 columns: Owner (name, contact no.), Pet, Vet, Visit date, Bill date, Cashier.
- Items table (read-only): Type pill, Description, Qty, Unit price, Subtotal. Total row at the bottom in bold.
- Payments card: list of payments, each row with date & time, method pill, reference no., amount (green), received by. Empty: "No payments yet".
- Balance box on the right: Total, Paid, Balance (large; orange if above ₱0, mint "Fully paid" if ₱0).
- Paid bills show a small gray lock icon with "Locked — paid bills can't be changed".

VOID DIALOG
- "Void bill VC-2026-0145? It will be kept in the records but won't count as income." Buttons: Cancel / red "Void bill".
- If blocked: "This bill already has payments, so it can't be voided." with only an "OK" button.

RECEIPT (print — 80 mm thermal style, black and white; also works on A5)
- Top: LOGO PLACEHOLDER (black rectangle 120px x 40px), clinic name (bold), address, contact no.
- "OFFICIAL RECEIPT", bill no., date and time.
- Owner name, pet name, vet name.
- Dashed divider, then items: description on one line, "qty x price" and subtotal on the next line, right-aligned.
- Dashed divider, then TOTAL (bold, larger), each payment (method + reference + amount), Change (if cash), BALANCE.
- Bottom: "Cashier: John Dela Cruz", "Thank you for trusting VetCare!", and a small paw icon.

SAMPLE DATA (for preview, today = Nov 17, 2026, consultation fee = ₱400.00)
Ready to bill:
- Nov 17, 2026 — Bantay (Dog, Aspin) — Juan Dela Cruz — Dr. Maria Santos — Vaccination — "Consultation, 1 vaccine"
Bills:
- VC-2026-0142 — Nov 17, 2026 — Maria Clara Santos — Choco — Total ₱400.00 — Paid ₱400.00 — Balance ₱0.00 — Paid — GCash ref 1029384756 — Synced
  Items: Consultation fee 1 × ₱400.00
- VC-2026-0141 — Nov 16, 2026 — Pedro Reyes — Luna — Total ₱1,070.00 — Paid ₱500.00 — Balance ₱570.00 — Partial — Synced
  Items: Consultation fee 1 × ₱400.00; Itraconazole 100 mg 14 × ₱35.00 = ₱490.00; Miconazole Cream 2% 1 × ₱180.00
- VC-2026-0140 — Nov 15, 2026 — Pedro Reyes — Max — Total ₱400.00 — Paid ₱0.00 — Balance ₱400.00 — Unpaid — Pending
  Items: Consultation fee 1 × ₱400.00 (note: Otomax Ear Drops not dispensed yet)
- VC-2026-0139 — Nov 14, 2026 — Ana Villanueva — Snow — Total ₱475.00 — Paid ₱475.00 — Paid — Cash (received ₱500.00, change ₱25.00) — Synced
  Items: Consultation fee 1 × ₱400.00; Doxycycline 100 mg 5 × ₱15.00 = ₱75.00
- VC-2026-0138 — Nov 13, 2026 — Carlo Mendoza — Rocky — Total ₱400.00 — Void — Synced

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, the create-bill totals card moves below the items, the payment panel becomes full width, and tables scroll sideways if needed.
```
