# VetCare – Medical Records Module ("Medical records")

**Module:** 4 – Medical Records (Core)
**Sidebar location:** PET CARE → Medical records
**Used by:** Veterinarian (full access), Cashier/Staff (view and print), Administrator (view only)
**Tables:** MedicalRecord (linked to Appointment, Pet, User, Prescription, Vaccination)

---

## 1. What this module does

The Medical Records module is where the vet writes down what happened during a visit: the pet's vital signs, the diagnosis, the treatment, and any notes. It's also where the vet **prescribes medicine** and **records vaccines** given during the consultation.

- Every record is linked to **one appointment** and **one pet**.
- A pet's records together make up its **full health history**.
- This module **connects** Appointments (before), and Pharmacy, Vaccination, and Billing (after).

| Role | Access |
|---|---|
| Veterinarian | Create and edit their own records, view all records in the clinic |
| Cashier/Staff | View and print records (to give a copy to the owner) |
| Administrator | View only |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### MedicalRecord table
| Field | Type | Notes |
|---|---|---|
| RecordID | UNIQUEIDENTIFIER (PK) | Created automatically |
| AppointmentID | UNIQUEIDENTIFIER (FK) | Required; one record per appointment |
| PetID | UNIQUEIDENTIFIER (FK) | Taken from the appointment |
| VetID | UNIQUEIDENTIFIER (FK → User) | Set to the logged-in vet |
| Weight | DECIMAL(5,2) | In kg; also updates `Pet.Weight` |
| Temperature | DECIMAL(4,1) | In °C |
| Diagnosis | VARCHAR(MAX) | Required |
| Treatment | VARCHAR(MAX) | Required |
| Notes | VARCHAR(MAX) | Optional (advice, observations, follow-up) |
| RecordDate | DATETIME | Set automatically |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked tables saved from the consultation
| Table | What the vet adds |
|---|---|
| Prescription | Medicine, quantity, dosage, instructions (sent to Pharmacy for dispensing) |
| Vaccination | Vaccine, dose number, date given, next due date |

### Linked data shown
| Table | Shown as |
|---|---|
| Appointment | Visit date, time, and reason |
| Pet / Owner | Pet profile, owner name, and contact no. |
| Past MedicalRecords | Previous visits of the same pet |

---

## 3. Screens

### A. Medical records list (main page)
- **Summary cards:** Records today, Records this week, Ongoing consultations, Prescriptions not yet dispensed.
- **Toolbar:** search (pet, owner, diagnosis), Vet filter, date range, and a "My records only" toggle (vets).
- **Table columns:** Date, Pet (avatar + name), Owner, Vet, Diagnosis (shortened), Prescriptions (count), Vaccines (count), Sync, Actions.
- **Row actions:** View, Edit (owner vet only, same day), Print.

### B. Consultation page (opens from "Start consultation" in Appointments)
This is the vet's main working screen, split into two sides:

**Left side – Pet summary (read only)**
- Pet info: name, species · breed, sex, age, last weight.
- Owner name and contact no.
- Visit info: date, time, and reason from the appointment.
- Vaccination status: last vaccines and anything due or overdue.
- **Previous visits:** the last 5 records, with date, diagnosis, and vet. Each can be expanded.

**Right side – Consultation form**
1. **Vital signs:** Weight (kg) and Temperature (°C), with the normal range shown by species (e.g., dog 38.3–39.2 °C).
2. **Diagnosis** (required).
3. **Treatment** (required).
4. **Notes** (optional).
5. **Prescriptions:** add one or more rows with Medicine (shows current stock), Quantity, Dosage, and Instructions.
6. **Vaccinations:** add one or more rows with Vaccine, Dose no., and Next due date (suggested automatically, e.g., +1 year).
7. **Follow-up (optional):** pick a date to book a follow-up appointment.

**Buttons:** Save draft, and **Save & complete visit**. The second one saves everything and marks the appointment **Completed**.

### C. Record details page
- A clean, read-only view of the record: pet and owner, visit info, vet, vital signs, diagnosis, treatment, notes, prescriptions (with dispensed status), and vaccinations.
- Buttons: **Edit** (if allowed), **Print**, and **Book follow-up**.

### D. Print view
- A printable one-page summary with the clinic name and address, pet and owner details, vet name, visit date, vital signs, diagnosis, treatment, prescriptions, vaccinations, and the next vaccine due date.
- This is the copy given to the owner.

---

## 4. Rules (checked in MedicalRecordController)

1. Only **Veterinarians** can create or edit records. Staff and Admin can only view, and Staff can print.
2. Users only see records from **their own clinic** (through the appointment's `ClinicID`).
3. A record can only be created for an appointment that is **In Consultation** and **assigned to the logged-in vet**.
4. **Only one record per appointment.**
5. **Diagnosis and treatment are required.**
6. Weight must be between 0.01 and 200 kg. Temperature must be between 30.0 and 45.0 °C, and a warning shows if it's outside the normal range for the species.
7. Saving a weight **updates `Pet.Weight`** automatically.
8. **Prescriptions:** the medicine must exist in the clinic's stock list and the quantity must be more than 0. A warning shows if the quantity is more than the current stock. The prescription can still be saved, but it can't be dispensed until stock is added.
9. **Vaccinations:** the item must be a medicine with the category **Vaccine**, and the next due date must be after today.
10. **Save & complete visit** saves the record, prescriptions, and vaccinations in **one SQL transaction**, then sets the appointment to **Completed**.
11. A record can only be **edited by the vet who wrote it**, on the **same day**. After that it's locked, and corrections are added as a new note.
12. Medical records are **never deleted**, because they are permanent health history.

---

## 5. Offline / online sync

- Vets **can write records offline**, since consultations shouldn't stop when the internet goes down.
- The record, prescriptions, and vaccinations are saved together with SyncStatus = **Pending** and added to the SyncQueue.
- When online, they're sent to the server and marked **Synced**.
- **Medical records are never overwritten by a sync.** If the same record was changed on two computers, it's marked **Conflict** and both versions are kept, so the vet can choose the correct one.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/MedicalRecord.cs` |
| Model | `Models/Repositories/MedicalRecordRepository.cs` |
| Model (used) | `PrescriptionRepository.cs`, `VaccinationRepository.cs`, `AppointmentRepository.cs` |
| Controller | `Controllers/MedicalRecordController.cs` |
| View | `Views/Veterinarian/MedicalRecordsPage.xaml` (list) |
| View | `Views/Veterinarian/ConsultationPage.xaml` (consultation form) |
| View | `Views/Shared/MedicalRecordDetailsPage.xaml` (details + print) |
| Helper | `Helpers/Validator.cs`, `Helpers/VitalSignsHelper.cs` (normal ranges by species) |

**MedicalRecordController methods**
- `GetRecords(search, vetId, dateFrom, dateTo, myRecordsOnly)`
- `GetRecordById(recordId)`
- `GetRecordByAppointment(appointmentId)`
- `GetPetHistory(petId)`
- `GetConsultationData(appointmentId)`: pet summary, previous visits, vaccine status
- `SaveDraft(record, prescriptions, vaccinations)`
- `SaveAndComplete(record, prescriptions, vaccinations, followUpDate)`
- `UpdateRecord(record)`
- `AddNote(recordId, note)`
- `GetPrintData(recordId)`

---

## 7. UI build prompt

```
Build the "Medical records" pages for the VetCare veterinary clinic management system. They sit to the right of the existing VetCare sidebar, with "Medical records" active in the sidebar under PET CARE (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

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

=== PAGE 1: MEDICAL RECORDS LIST ===

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PET CARE"
- Title (32px, Nunito 800, navy): "Medical records"
- Subtitle (gray): "Every check-up, diagnosis, and treatment, all in one history."

SUMMARY CARDS (4 in a row)
- "Records today" (clipboard-plus icon, cyan circle)
- "Records this week" (calendar-range icon, pale-blue circle)
- "Ongoing consultations" (stethoscope icon, amber circle)
- "Prescriptions not dispensed" (pill icon, light-orange circle, orange icon)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label.

TOOLBAR CARD
- Search input ("Search pet, owner, or diagnosis"), "All vets" dropdown, date range picker ("Nov 1 – Nov 17, 2026"), and a toggle switch "My records only" (cyan when on).

RECORDS TABLE (inside a white card)
- Columns: Date (date in bold navy, time below in gray), Pet (40px species icon circle + pet name + "Dog · Aspin"), Owner, Vet ("Dr. Maria Santos"), Diagnosis (one line, cut off with "…"), Prescriptions (pill icon + count), Vaccines (syringe icon + count), Sync badge, actions menu.
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red, tooltip "Two versions found — review needed").
- Row hover: #F2F8FB. Clicking a row opens the record details.
- Actions menu: View, Edit (only shown for the vet who wrote it, same day), Print.
- Pagination: "Showing 1–10 of 312 records".
- Empty state: clipboard icon in a cyan circle, "No medical records yet", subtext "Records appear here after a consultation."

=== PAGE 2: CONSULTATION PAGE ===

TOP BAR
- Back link "← Appointments".
- Title (28px Nunito 800 navy): "Consultation · Bantay"
- Amber status pill "In Consultation" and a small live timer "Started 9:35 AM · 12 min".
- Right side: gray outline "Save draft" and orange "Save & complete visit" (check icon).

TWO-COLUMN LAYOUT (left 340px, right fills the rest)

LEFT COLUMN – Pet summary (stacked white cards, read only)
1. Pet card: 64px mint circle with a dog icon, "Bantay" (24px Nunito 800), "Dog · Aspin · Male", info chips: "2 yrs 3 mos", "Last weight 12.1 kg".
2. Owner card: label "OWNER" (small uppercase gray), "Juan Dela Cruz" (bold navy), phone icon + "09171234567".
3. Visit card: calendar icon "Nov 17, 2026 · 9:00 AM", reason pill "Vaccination", vet "Dr. Maria Santos".
4. Vaccination status card: list rows like "Anti-rabies — Dose 1 — Due Dec 05, 2026" with a status pill (Up to date = mint, Due soon = amber, Overdue = light orange).
5. Previous visits card: title "Previous visits" + count. A short vertical timeline of the last 5 visits: date, diagnosis in bold navy, vet name in gray. Each one expands to show the treatment and vital signs. Link at the bottom: "View full history →" (orange).

RIGHT COLUMN – Consultation form (white cards, each with a title row: icon + section name in bold navy)
1. "Vital signs" (activity icon): two inputs side by side
   - Weight (number input with "kg" suffix), hint below in gray "Last: 12.1 kg"
   - Temperature (number input with "°C" suffix), hint below "Normal for dogs: 38.3–39.2 °C". If the value is outside the range, show an amber warning text "Outside normal range".
2. "Diagnosis" (search-check icon): textarea, 4 rows, required.
3. "Treatment" (bandage icon): textarea, 4 rows, required.
4. "Notes" (notebook-pen icon): textarea, 3 rows, placeholder "Advice for the owner, observations, follow-up instructions…".
5. "Prescriptions" (pill icon): a table of editable rows with columns Medicine (searchable dropdown; each option shows the medicine name and "Stock: 24 tabs" in gray), Qty (small number input), Dosage (e.g., "1 tab"), Instructions (e.g., "Twice a day for 7 days"), and a trash icon to remove the row. If the quantity is more than the stock, show an amber note under the row: "Only 5 in stock — can't be dispensed yet." Below the table: dashed-border button "+ Add medicine".
6. "Vaccinations" (syringe icon): editable rows with Vaccine (dropdown of medicines with the category Vaccine), Dose no. (number), Next due date (date picker, filled in automatically 1 year later), and a trash icon. Below: dashed-border button "+ Add vaccine".
7. "Follow-up" (calendar-plus icon): a checkbox "Book a follow-up visit", which shows a date picker and "Same vet" toggle when checked.
- Inline red errors: "Diagnosis is required", "Treatment is required", "Enter a temperature between 30 and 45 °C", "Next due date must be after today".
- Sticky footer bar at the bottom of the right column (white, top border): gray outline "Save draft", orange "Save & complete visit". A small gray text on the left: "Draft saved 9:41 AM" after saving.

COMPLETE CONFIRM DIALOG
- "Complete Bantay's visit? This saves the record, sends 1 prescription to the pharmacy, records 1 vaccine, and marks the appointment as completed." Buttons: "Go back" / orange "Complete visit".
- After success, show a green toast "Visit completed. Ready for billing." with a link "Create bill →".

=== PAGE 3: RECORD DETAILS ===
- Back link "← Medical records". Top-right buttons: outline "Print" (printer icon), outline "Book follow-up", navy "Edit" (only when allowed).
- Header card: pet avatar + name, "Dog · Aspin · Male · 2 yrs 3 mos", owner name, visit date and time, vet name, sync badge, and a small gray lock icon with "Locked" when it can no longer be edited.
- Vital signs row: 2 stat chips "Weight 12.5 kg" and "Temperature 38.6 °C" (amber if outside the normal range).
- Sections with headings (bold navy) and gray body text: Diagnosis, Treatment, Notes.
- "Prescriptions" table: Medicine, Qty, Dosage, Instructions, Dispensed pill (Yes = mint, Not yet = amber).
- "Vaccinations" table: Vaccine, Dose no., Date given, Next due.
- "Added notes" section (if any): small cards with date, vet name, and the note text.

PRINT VIEW (A4, black and white friendly)
- Header: LOGO PLACEHOLDER (black rectangle 160px x 50px), clinic name, address, and contact number on the right.
- Title: "Medical Record".
- Two-column info block: Pet (name, species, breed, sex, age) and Owner (name, contact no.).
- Visit date, vet name, weight, and temperature.
- Diagnosis, Treatment, Notes, Prescriptions table, Vaccinations table, and "Next vaccine due: Dec 05, 2026".
- Footer: "Attending veterinarian: ____________________" signature line and the print date.

SAMPLE DATA (for preview)
- Nov 17, 2026 9:00 AM — Bantay (Dog, Aspin) — Juan Dela Cruz — Dr. Maria Santos — Diagnosis: "Healthy; due for anti-rabies vaccine" — Treatment: "Anti-rabies vaccine given" — 12.5 kg, 38.6 °C — 0 prescriptions, 1 vaccine — Pending
- Nov 17, 2026 8:30 AM — Choco (Dog, Shih Tzu) — Maria Clara Santos — Dr. Maria Santos — "Mild tartar buildup" — "Dental cleaning advised; dental chews" — 6.2 kg, 38.5 °C — 1 prescription — Synced
- Nov 16, 2026 2:00 PM — Luna (Cat, Persian) — Pedro Reyes — Dr. Grace Lim — "Fungal skin infection (ringworm)" — "Antifungal cream and oral medicine for 14 days" — 4.1 kg, 38.9 °C — 2 prescriptions — Synced
- Nov 15, 2026 10:00 AM — Max (Dog, Labrador) — Pedro Reyes — Dr. Grace Lim — "Ear infection (otitis externa)" — "Ear cleaning; ear drops twice daily" — 28.4 kg, 39.4 °C (amber, above normal) — 1 prescription — Synced
- Nov 14, 2026 3:30 PM — Snow (Cat, Siamese) — Ana Villanueva — Dr. Maria Santos — "Upper respiratory infection, improving" — "Continue antibiotics for 5 more days" — 4.5 kg, 38.7 °C — 1 prescription — Conflict

RESPONSIVE
- On smaller widths, the consultation page's left column moves above the form, summary cards wrap to 2 per row, and tables scroll sideways if needed.
```
