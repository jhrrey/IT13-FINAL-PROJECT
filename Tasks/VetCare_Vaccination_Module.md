# VetCare – Vaccination Module ("Vaccinations")

**Module:** 5 – Vaccination (Core)
**Sidebar location:** PET CARE → Vaccinations
**Used by:** Veterinarian (full access), Cashier/Staff (view, print, follow up), Administrator (view only)
**Tables:** Vaccination (linked to MedicalRecord, Pet, Medicine, User)

---

## 1. What this module does

The Vaccination module keeps track of every vaccine a pet receives and **when the next one is due**. It helps the clinic make sure no pet misses a shot.

- Vaccines are **given and recorded by the vet during a consultation**, in the Medical Records module. This module shows those records in one place.
- It lists the pets that are **due soon or overdue**, so staff can contact the owners and book a visit.
- It prints a **vaccination card** for the owner.
- Each vaccine is a **medicine in stock** (category *Vaccine*), so giving one lowers the stock and adds it to the bill.

| Role | Access |
|---|---|
| Veterinarian | Record vaccines (during consultation), edit own records on the same day, view all |
| Cashier/Staff | View records, see due/overdue list, print vaccination cards, book follow-up visits |
| Administrator | View only |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Vaccination table
| Field | Type | Notes |
|---|---|---|
| VaccinationID | UNIQUEIDENTIFIER (PK) | Created automatically |
| RecordID | UNIQUEIDENTIFIER (FK) | The consultation where the vaccine was given |
| PetID | UNIQUEIDENTIFIER (FK) | The vaccinated pet |
| MedicineID | UNIQUEIDENTIFIER (FK) | The vaccine used (Medicine with category *Vaccine*) |
| VetID | UNIQUEIDENTIFIER (FK → User) | The vet who gave it |
| DoseNumber | INT | 1st, 2nd, 3rd dose, or booster |
| DateGiven | DATE | Date of the shot |
| NextDueDate | DATE | When the next dose or booster is due |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown
| Table | Shown as |
|---|---|
| Pet / Owner | Pet name, species, owner name, and contact no. |
| Medicine | Vaccine name and stock |
| User | Vet name ("Given by") |
| MedicalRecord | Link to the consultation where it was given |

### Vaccine status (calculated, not saved)
| Status | Rule |
|---|---|
| **Up to date** | Next due date is more than 30 days away |
| **Due soon** | Next due date is within the next 30 days |
| **Overdue** | Next due date has already passed |
| **Completed** | Final dose of a series with no next due date |

---

## 3. Screens

### A. Vaccinations page (main page, 2 tabs)
- **Summary cards:** Given this month, Due in 30 days, Overdue, Pets up to date.
- **Tab 1 – Due & overdue (default):** a follow-up list of pets that need their next shot.
  - Columns: Pet, Owner, Contact no., Vaccine, Dose, Due date, Days left / overdue, Status, Actions.
  - Filters: "Overdue", "Due in 7 days", "Due in 30 days", vaccine type, species.
  - Actions: **Book appointment** (opens the booking panel with the pet already selected and "Vaccination" as the reason), **View pet**, and **Copy contact no.**
- **Tab 2 – All records:** the history of every vaccine given.
  - Columns: Date given, Pet, Owner, Vaccine, Dose, Given by, Next due, Sync, Actions.
  - Filters: date range, vaccine, vet, search (pet or owner).
  - Actions: View, Edit (vet who gave it, same day), Print card.

### B. Record vaccination (inside the Consultation page)
- In the **Vaccinations** section of the consultation form, the vet adds a row: Vaccine, Dose no., and Next due date.
- The **dose number is filled in automatically** (the last dose of that vaccine + 1).
- The **next due date is suggested automatically** from the vaccine type, and the vet can change it.
- The current vaccine stock is shown in the dropdown.

### C. Pet vaccination card
- Opened from a pet profile or a vaccination record.
- Shows the pet and owner details and a table of all vaccines: vaccine, dose, date given, given by, and next due date.
- A status pill for each vaccine, and an overall status at the top ("Up to date", "1 due soon", "1 overdue").
- Buttons: **Print card** and **Book vaccination**.

### D. Print view (vaccination card)
- A small printable card (A5 or half of A4) with the clinic name, pet and owner details, a vaccine table, the vet signature line, and the next due date in large text.

---

## 4. Rules (checked in VaccinationController)

1. Only **Veterinarians** can record or edit vaccinations. Staff and Admin can only view, and Staff can print and book follow-ups.
2. Users only see vaccinations from **their own clinic** (through the pet's owner).
3. A vaccination is recorded **during a consultation** and must be linked to its **Medical Record**.
4. The selected item must be a **Medicine with the category Vaccine**, and it must have **at least 1 in stock**.
5. **Recording a vaccine lowers its stock by 1** and adds a "Stock Out" entry to InventoryTransaction automatically.
6. The vaccine is **added to the visit's bill** as a BillItem with the item type *Vaccine*.
7. **Date given can't be in the future.** The next due date must be **after** the date given.
8. The dose number is suggested automatically but can be changed. It must be 1 or higher.
9. **Default next due dates** (suggested, can be changed), for example:
   - Anti-rabies: 1 year
   - 5-in-1 / DHPPiL (puppy series): 3 weeks between doses, then a yearly booster
   - 4-in-1 / FVRCP (kitten series): 3 weeks between doses, then a yearly booster
   - Kennel cough (Bordetella): 1 year
10. A vaccination can only be **edited by the vet who gave it, on the same day**. After that it's locked.
11. Vaccination records are **never deleted**, since they're part of the pet's health history.

> **Optional ERD additions:**
> - To record **past vaccines given at another clinic**, make `RecordID` nullable and add a `Source` column (This clinic / Other clinic).
> - To save the default intervals in the database instead of in code, add a small `VaccineSchedule` table (MedicineID, Species, DoseNumber, IntervalDays).

---

## 5. Offline / online sync

- Vets **can record vaccinations offline** during a consultation.
- The vaccination, stock deduction, and inventory entry are saved together with SyncStatus = **Pending** and added to the SyncQueue.
- When online, they're sent to the server and marked **Synced**.
- **Stock check after sync:** if two offline computers used the last dose of the same vaccine, the stock may drop below zero after syncing. The medicine is then flagged as **Conflict / Out of stock** so the staff can fix the count in Pharmacy.
- Vaccination records are **never overwritten** by a sync. If they clash, both versions are kept for the vet to review.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Vaccination.cs` |
| Model | `Models/Repositories/VaccinationRepository.cs` |
| Model (used) | `MedicineRepository.cs`, `InventoryRepository.cs`, `BillingRepository.cs` |
| Controller | `Controllers/VaccinationController.cs` |
| View | `Views/Veterinarian/VaccinationPage.xaml` (due list + all records) |
| View | `Views/Shared/VaccinationCardView.xaml` (pet vaccination card + print) |
| View (used) | `Views/Veterinarian/ConsultationPage.xaml` (vaccination section) |
| Helper | `Helpers/VaccineScheduleHelper.cs` (default intervals and status) |

**VaccinationController methods**
- `GetDueList(filter, vaccineId, species)`
- `GetAllRecords(search, vaccineId, vetId, dateFrom, dateTo)`
- `GetPetVaccinations(petId)`
- `GetSummary()`: given this month, due in 30 days, overdue, up to date
- `GetVaccineOptions()`: medicines with the category Vaccine and their stock
- `SuggestDose(petId, medicineId)`
- `SuggestNextDueDate(medicineId, species, doseNumber, dateGiven)`
- `AddVaccination(vaccination)` (called by MedicalRecordController during "Save & complete visit")
- `UpdateVaccination(vaccination)`
- `GetCardPrintData(petId)`

---

## 7. UI build prompt

```
Build the "Vaccinations" page for the VetCare veterinary clinic management system. It sits to the right of the existing VetCare sidebar, with "Vaccinations" active in the sidebar under PET CARE (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500)
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

VACCINE STATUS PILLS (use everywhere)
- Up to date = mint #C4F1E1, dark green text #1E6B4F, check icon
- Due soon = amber #FFF4E0, dark amber text #8A5A00, clock icon
- Overdue = light orange #FDE8E1, orange text #D4421C, alert-circle icon
- Completed = pale blue #DCEBFB, navy text, badge-check icon

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PET CARE"
- Title (32px, Nunito 800, navy): "Vaccinations"
- Subtitle (gray): "Keep every shot on time, and every pet protected."
- Right side: navy outline button "Print due list" (printer icon)

SUMMARY CARDS (4 in a row)
- "Given this month" (syringe icon, cyan circle)
- "Due in 30 days" (clock icon, amber circle)
- "Overdue" (alert-circle icon, light-orange circle, orange icon)
- "Pets up to date" (shield-check icon, mint circle)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label. Clicking "Due in 30 days" or "Overdue" applies that filter to the due list.

TABS
- "Due & overdue" (active: orange text + 2px orange underline) with a small orange count pill, and "All records" (gray).

TAB 1 – DUE & OVERDUE
- Filter chips row: "All", "Overdue", "Due in 7 days", "Due in 30 days" (selected chip = cyan background, bold navy text). Plus a "All vaccines" dropdown and an "All species" dropdown on the right.
- Table inside a white card:
  - Columns: Pet (40px species icon circle + pet name + "Dog · Aspin"), Owner, Contact no. (with a small copy icon button), Vaccine, Dose ("Dose 3" or "Booster"), Due date, Days (e.g. "in 11 days" in amber, "18 days overdue" in orange bold), Status pill, actions.
  - Actions: orange small button "Book" (calendar-plus icon), and a three-dot menu with "View pet" and "Print card".
  - Overdue rows are sorted first, then the nearest due dates.
  - Row hover: #F2F8FB.
- Empty state: shield-check icon in a mint circle, "All caught up!", subtext "No pets are due or overdue right now."

TAB 2 – ALL RECORDS
- Toolbar: search ("Search pet or owner"), date range picker, "All vaccines" dropdown, "All vets" dropdown.
- Table columns: Date given, Pet (avatar + name), Owner, Vaccine, Dose, Given by ("Dr. Maria Santos"), Next due (with its status pill), Sync badge, actions menu.
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red).
- Actions menu: View record, Edit (only for the vet who gave it, same day), Print card.
- Pagination: "Showing 1–10 of 486 records".

BOOK VACCINATION
- Clicking "Book" opens the existing Book appointment panel with the owner and pet filled in, the reason chip "Vaccination" selected, and the details box filled with "Due: 5-in-1 (DHPPiL) Dose 3".

PET VACCINATION CARD (opens as a right side panel, 520px wide, or as a full page from the pet profile)
- Top: 56px species icon circle, pet name (24px Nunito 800 navy), "Dog · Aspin · Male · 2 yrs 3 mos", owner name + contact no.
- Overall status banner (rounded, full width): mint "Up to date", or amber "1 vaccine due soon", or light orange "1 vaccine overdue", with an icon.
- Vaccine table: Vaccine, Dose, Date given, Given by, Next due, Status pill. Rows grouped by vaccine name with a small bold navy group title (e.g. "Anti-rabies", "5-in-1 (DHPPiL)").
- Footer buttons: outline "Print card" (printer icon), orange "Book vaccination".

CONSULTATION PAGE – VACCINATIONS SECTION (update the existing consultation form)
- Section title: syringe icon + "Vaccinations".
- Each row: Vaccine (dropdown showing vaccine name + "Stock: 18" in gray; out-of-stock vaccines disabled with "Out of stock" in red), Dose no. (number input, filled in automatically, with gray hint "Last: Dose 2 on Nov 10"), Next due date (date picker, filled in automatically, with gray hint "Suggested: +3 weeks"), and a trash icon.
- Dashed-border button "+ Add vaccine".
- Red errors: "Next due date must be after today", "This vaccine is out of stock".

PRINT VIEW – VACCINATION CARD (A5, black and white friendly)
- Header: LOGO PLACEHOLDER (black rectangle 140px x 44px), clinic name, address, and contact number.
- Title: "Pet Vaccination Card".
- Pet box: name, species, breed, sex, birth date, color. Owner box: name, contact no.
- Table: Vaccine, Dose, Date given, Next due, Vet signature (empty column for signing).
- Big text at the bottom: "Next vaccine due: Dec 05, 2026".
- Footer: print date.

SAMPLE DATA (for preview, today = Nov 17, 2026)
Due & overdue:
- Max (Dog, Labrador) — Pedro Reyes — 09391234567 — Anti-rabies — Booster — Due Oct 30, 2026 — 18 days overdue — Overdue
- Mingming (Cat, Puspin) — Juan Dela Cruz — 09171234567 — 4-in-1 (FVRCP) — Dose 2 — Due Nov 28, 2026 — in 11 days — Due soon
- Bantay (Dog, Aspin) — Juan Dela Cruz — 09171234567 — 5-in-1 (DHPPiL) — Dose 3 — Due Dec 05, 2026 — in 18 days — Due soon
- Rocky (Dog, Beagle) — Carlo Mendoza — 09561234567 — 5-in-1 (DHPPiL) — Booster — Due Dec 10, 2026 — in 23 days — Due soon
All records:
- Nov 17, 2026 — Bantay — Anti-rabies — Dose 1 — Dr. Maria Santos — Next due Nov 17, 2027 — Up to date — Pending
- Nov 14, 2026 — Bantay — 5-in-1 (DHPPiL) — Dose 2 — Dr. Maria Santos — Next due Dec 05, 2026 — Due soon — Synced
- Nov 07, 2026 — Mingming — 4-in-1 (FVRCP) — Dose 1 — Dr. Grace Lim — Next due Nov 28, 2026 — Due soon — Synced
- Sep 12, 2026 — Choco — Anti-rabies — Booster — Dr. Maria Santos — Next due Sep 12, 2027 — Up to date — Synced
- Aug 20, 2026 — Luna — 4-in-1 (FVRCP) — Booster — Dr. Grace Lim — Next due Aug 20, 2027 — Up to date — Synced
- Oct 30, 2025 — Max — Anti-rabies — Booster — Dr. Grace Lim — Next due Oct 30, 2026 — Overdue — Synced

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, the vaccination card panel becomes full width, and tables scroll sideways if needed.
```
