# VetCare – Pet Profiles Module ("Pets")

**Module:** 2 – Pet Profiles (Core)
**Sidebar location:** PET CARE → Pets
**Used by:** Cashier/Staff (full access), Veterinarian (view), Administrator (view)
**Tables:** Pet (linked to Owner, Appointment, MedicalRecord, Vaccination, Prescription)

---

## 1. What this module does

The Pet module stores the profile of every animal the clinic takes care of. It's the center of the system, because appointments, medical records, vaccinations, and prescriptions are all linked to a pet.

- Every pet **belongs to one owner**. An owner can have one or more pets.
- A pet belongs to a clinic **through its owner**, since the Pet table has no ClinicID of its own.
- The pet profile shows the pet's **full health history** in one place.

| Role | Access |
|---|---|
| Cashier/Staff | Add, edit, view, archive pets |
| Veterinarian | View pet profiles and full health history |
| Administrator | View only |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Pet table
| Field | Type | Notes |
|---|---|---|
| PetID | UNIQUEIDENTIFIER (PK) | Created automatically |
| OwnerID | UNIQUEIDENTIFIER (FK) | Required, must be an owner in the same clinic |
| PetName | VARCHAR(50) | Required |
| Species | VARCHAR(30) | Required (Dog, Cat, Bird, Rabbit, Hamster, Other) |
| Breed | VARCHAR(50) | Optional (e.g., Aspin, Puspin, Shih Tzu) |
| Sex | VARCHAR(10) | Required (Male / Female) |
| BirthDate | DATE | Optional; age is calculated from this |
| Color | VARCHAR(30) | Optional (helps identify the pet) |
| Weight | DECIMAL(5,2) | Latest weight in kg |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown in this module
| Table | Shown as |
|---|---|
| Owner | Owner name and contact number |
| Appointment | Past and upcoming visits |
| MedicalRecord | Consultation history (diagnosis, treatment, weight, temperature) |
| Vaccination | Vaccines given and next due dates |
| Prescription | Medicines prescribed |

---

## 3. Screens

### A. Pets list (main page)
- **Summary cards:** Total pets, Dogs, Cats, Vaccines due this month.
- **Toolbar:** search (pet name, owner name), Species filter, Sex filter, a Card/Table view toggle, and an **Add pet** button.
- **Card view (default):** pet name, species · breed, sex, age, owner name, and a "Next vaccine" pill.
- **Table view columns:** Pet (avatar + name), Species, Breed, Sex, Age, Weight, Owner, Last visit, Sync, Actions.
- **Row/card actions:** View profile, Edit, Book appointment, Archive.

### B. Add / Edit pet panel
- **Owner** (searchable dropdown, already filled in when opened from an Owner profile).
- Pet name, Species, Breed, Sex, Birth date (or an **"Estimated age"** option if the exact date is unknown), Color, Weight (kg).
- Buttons: **Cancel**, **Save pet**, and **Save and book appointment**.

### C. Pet profile page
- **Header card:** pet avatar, name, species · breed, sex, age, color, current weight, and sync status.
- **Owner card:** owner name, contact number, and a link to the owner profile.
- **Quick buttons:** Edit, Book appointment.
- **Quick stats:** Last visit, Next appointment, Next vaccine due, Total visits.
- **Tabs:**
  - **Medical history:** a timeline of consultations (date, vet, diagnosis, treatment, weight, temperature). Clicking an entry opens the full record.
  - **Vaccinations:** vaccine name, dose number, date given, next due date, and given by. A status pill shows Up to date, Due soon, or Overdue.
  - **Appointments:** date, time, vet, reason, and status.
  - **Prescriptions:** medicine, dosage, quantity, date, and dispensed status.
  - **Weight trend:** a small line chart of the pet's weight across visits, using `MedicalRecord.Weight`.

---

## 4. Rules (checked in PetController)

1. Only **Cashier/Staff** can add, edit, or archive pets. Vets and Admins can only view.
2. Staff only see pets whose **owner belongs to their clinic** (`Owner.ClinicID = Session.ClinicID`).
3. **Owner, pet name, species, and sex are required.**
4. The birth date can't be in the future. If only an estimated age is given, the system works out an approximate birth date.
5. Weight must be greater than 0 and less than 200 kg.
6. A pet's **weight is updated automatically** when a vet records a new weight in a consultation.
7. Pets are **archived, not deleted** (`IsDeleted = 1`), so their medical history is kept.
8. A pet **can't be archived** if it has an upcoming appointment.
9. Archived pets don't appear in the booking list, but their records stay searchable.
10. Pets can be moved to another owner, but only within the same clinic (for example, when a pet is given to a family member).

> **Optional ERD addition:** if you want to mark pets as *Deceased* or *Transferred* instead of just archiving them, add a `Status` column (Active / Deceased / Transferred) to the Pet table.

---

## 5. Offline / online sync

- Pets **can be added and edited offline**. The GUID ID prevents clashes between computers.
- Changes are saved with SyncStatus = **Pending** and added to the SyncQueue.
- When online, they're sent to the server and marked **Synced**, and pets added on other clinic computers are downloaded.
- If two computers edit the same pet while offline, the **newer edit wins**, based on `UpdatedAt`.
- If a pet is added offline for an owner who was archived on another computer, the pet is marked **Conflict** so the staff can check it.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Pet.cs` |
| Model | `Models/Repositories/PetRepository.cs` |
| Controller | `Controllers/PetController.cs` |
| View | `Views/Staff/PetsPage.xaml` (list) |
| View | `Views/Staff/PetDetailsPage.xaml` (profile) |
| View | `Views/Shared/PetFormView.xaml` (add/edit panel) |
| Helper | `Helpers/Validator.cs`, `Helpers/AgeCalculator.cs` |

**PetController methods**
- `GetPets(search, species, sex)`
- `GetPetsOfOwner(ownerId)`
- `GetPetById(petId)`
- `GetPetSummary(petId)`: last visit, next appointment, next vaccine, total visits
- `GetMedicalHistory(petId)`
- `GetVaccinations(petId)`
- `GetWeightHistory(petId)`
- `AddPet(pet)`
- `UpdatePet(pet)`
- `TransferPet(petId, newOwnerId)`
- `ArchivePet(petId)`

---

## 7. UI build prompt

```
Build the "Pets" page for the VetCare veterinary clinic management system. It sits to the right of the existing VetCare sidebar, with "Pets" active in the sidebar under PET CARE (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500)
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PET CARE"
- Title (32px, Nunito 800, navy): "Pets"
- Subtitle (gray): "Every furry, feathered, and fluffy patient in one place."
- Right side: orange button "+ Add pet"

SUMMARY CARDS (4 in a row)
- "Total pets" (paw-print icon, cyan circle)
- "Dogs" (dog icon, mint circle)
- "Cats" (cat icon, pale-blue circle)
- "Vaccines due this month" (syringe icon, light-orange circle, orange icon)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label.

TOOLBAR CARD
- Search input with a search icon, placeholder "Search pet or owner name"
- "All species" dropdown (Dog, Cat, Bird, Rabbit, Hamster, Other)
- "All sex" dropdown (Male, Female)
- View toggle on the right: two icon buttons (layout-grid / list), active one has a cyan background

CARD VIEW (default, 3 cards per row)
- White card, 12px radius, 1px border, padding 20px, hover lifts slightly with a stronger border #9FD4E3.
- Top row: 52px circle avatar with a species icon (dog = mint, cat = pale blue, bird = light orange, others = cyan) and a three-dot actions menu on the right.
- Pet name (20px Nunito 800 navy), then "Dog · Aspin" in gray.
- Small info row with icons: sex (mars/venus icon), age ("2 yrs 3 mos"), weight ("12.5 kg").
- Divider line, then the owner row: user icon + "Juan Dela Cruz" in navy.
- Bottom pill: "Next vaccine: Dec 05, 2026" — mint if more than 30 days away, amber "Due soon" if within 30 days, light orange "Overdue" if past.
- Small sync badge in the top-right corner of the card when Pending or Conflict.

TABLE VIEW
- Columns: Pet (40px avatar + name), Species, Breed, Sex, Age, Weight, Owner, Last visit, Sync, actions menu.
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red).
- Row hover: background #F2F8FB. Clicking a row opens the pet profile.
- Pagination: "Showing 1–12 of 214 pets" with Prev/Next.
- Actions menu: View profile, Edit, Book appointment, Archive (red text).
- Empty state: paw-print icon in a cyan circle, "No pets yet", subtext "Add a pet to start their health record.", orange "Add pet" button.

ADD / EDIT PET PANEL (slides in from the right, 460px wide, white, soft shadow, dimmed background)
- Title "Add pet" / "Edit pet" with a close (x) icon.
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  Owner* (searchable dropdown showing name + contact no.), Pet name*, Species* (dropdown), Breed, Sex* (two toggle buttons: Male / Female; active = cyan background, navy bold text), Birth date (date picker) with a checkbox "I don't know the exact date" that swaps the date picker for two small inputs "Years" and "Months", Color, Weight (number input with "kg" suffix).
- Inline red error text: "Required", "Birth date can't be in the future", "Enter a weight between 0.1 and 200 kg".
- Footer: gray outline "Cancel", navy outline "Save and book appointment", orange "Save pet".

PET PROFILE PAGE (opens when a card or row is clicked)
- Back link "← Pets" at the top.
- Two cards side by side:
  - Left (wide) header card: 72px circle avatar with the species icon, pet name (28px Nunito 800 navy), "Dog · Aspin" in gray, and info chips: Male, 2 yrs 3 mos, Brown & white, 12.5 kg. Sync badge on the right. Buttons: outline "Edit", orange "Book appointment".
  - Right (narrow) owner card: label "OWNER" (small uppercase gray), owner name (bold navy), phone icon + contact no., and a link "View owner →" in orange.
- 4 stat chips below: "Last visit: Nov 10, 2026", "Next appointment: Nov 20, 10:00 AM", "Next vaccine: Dec 05, 2026", "Total visits: 6".
- Tabs: "Medical history" (active: orange text + orange underline), "Vaccinations", "Appointments", "Prescriptions", "Weight trend".
  - Medical history: vertical timeline. Each entry has a date dot on the left line, then a white card with date + vet name ("Dr. Maria Santos"), the diagnosis in bold navy, the treatment in gray, and small chips for weight and temperature ("12.5 kg", "38.6 °C"). Clicking opens the full record.
  - Vaccinations: table with Vaccine, Dose no., Date given, Next due, Given by, and a Status pill (Up to date = mint, Due soon = amber, Overdue = light orange).
  - Appointments: table with Date, Time, Vet, Reason, Status pill (Scheduled = pale blue, Checked In = cyan, Completed = mint, Cancelled = gray, No-show = light orange).
  - Prescriptions: table with Date, Medicine, Dosage, Quantity, and a Dispensed pill (Yes = mint, No = amber).
  - Weight trend: a simple line chart (navy line, cyan dots) of weight in kg over the visit dates, with the latest weight shown above it.

ARCHIVE DIALOG
- "Archive Bantay? Their health records will be kept but they won't appear in new bookings." Buttons: Cancel / red "Archive".
- If blocked: "Bantay has an upcoming appointment. Cancel or complete it first." with only an "OK" button.

SAMPLE DATA (for preview)
- Bantay — Dog, Aspin, Male, 2 yrs 3 mos, 12.5 kg — Owner: Juan Dela Cruz — Next vaccine Dec 05, 2026
- Mingming — Cat, Puspin, Female, 1 yr 6 mos, 3.8 kg — Owner: Juan Dela Cruz — Due soon
- Choco — Dog, Shih Tzu, Male, 4 yrs, 6.2 kg — Owner: Maria Clara Santos — Up to date
- Max — Dog, Labrador, Male, 3 yrs, 28.4 kg — Owner: Pedro Reyes — Overdue
- Luna — Cat, Persian, Female, 2 yrs, 4.1 kg — Owner: Pedro Reyes — Up to date
- Kiko — Bird, Lovebird, Male, 1 yr, 0.05 kg — Owner: Pedro Reyes — Pending sync
- Snow — Cat, Siamese, Female, 5 yrs, 4.5 kg — Owner: Ana Villanueva — Up to date
- Rocky — Dog, Beagle, Male, 6 yrs, 11.0 kg — Owner: Carlo Mendoza — Due soon
- Bella — Dog, Pomeranian, Female, 3 yrs, 2.9 kg — Owner: Carlo Mendoza — Up to date

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, pet cards wrap to 1–2 per row, the profile's two header cards stack, and tables scroll sideways if needed.
```
