# VetCare – Owner Records Module ("Owners")

**Module:** 1 – Owner Records (Core)
**Sidebar location:** PET CARE → Owners
**Used by:** Cashier/Staff (full access), Administrator (view only)
**Tables:** Owner (linked to Pet, Appointment, Bill)

---

## 1. What this module does

The Owner module stores the information of every pet owner (client) in the clinic. It's the starting point of the process, since every pet, appointment, and bill is linked back to an owner.

- Pet owners **are not system users**. They don't log in. The staff manages their records for them.
- Each owner belongs to **one clinic** only (`ClinicID`).

| Role | Access |
|---|---|
| Cashier/Staff | Add, edit, view, archive owners |
| Administrator | View only |
| Veterinarian | No access to this module, but sees the owner's name and contact number on the pet and appointment screens |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Owner table
| Field | Type | Notes |
|---|---|---|
| OwnerID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | Set automatically from `Session.ClinicID` |
| FirstName / LastName | VARCHAR(50) | Required |
| ContactNo | VARCHAR(20) | Required (e.g., 09XXXXXXXXX) |
| Email | VARCHAR(100) | Optional, must be a valid format |
| Address | VARCHAR(255) | Optional |
| DateRegistered | DATETIME | Set automatically when saved |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown in this module
| Table | Shown as |
|---|---|
| Pet | The owner's pets (one owner → one or more pets) |
| Appointment | The visit history and upcoming visits of the owner's pets |
| Bill / Payment | Billing history and any unpaid balance |

---

## 3. Screens

### A. Owners list (main page)
- **Summary cards:** Total owners, New this month, With upcoming appointments, With unpaid balance.
- **Toolbar:** search (name, contact no., email), "Sort by" (Newest, Name A–Z), and an **Add owner** button.
- **Table columns:** Owner (initials avatar, full name, email), Contact no., Address, Pets (count + pet names), Date registered, Sync status, Actions.
- **Row actions:** View profile, Edit, Add pet, Book appointment, Archive.

### B. Add / Edit owner panel
- First name, Last name, Contact no., Email, Address.
- Buttons: **Cancel**, **Save owner**, and **Save and add pet**. The last one opens the Add Pet form with this owner already selected, which is the usual flow for a new client.
- A duplicate warning appears if an owner with the same contact number already exists in the clinic.

### C. Owner profile page
- **Header card:** avatar, full name, contact no., email, address, date registered, and sync status.
- **Quick buttons:** Edit, Add pet, Book appointment.
- **Quick stats:** Number of pets, Next appointment, Unpaid balance.
- **Tabs:**
  - **Pets:** cards showing each pet's name, species, breed, age, and next vaccine due date. Clicking a card opens the pet's profile.
  - **Appointments:** the date, pet, vet, reason, and status for this owner's pets.
  - **Billing history:** bill date, amount, status (Paid / Unpaid / Partial), and a link to the receipt.

---

## 4. Rules (checked in OwnerController)

1. Only **Cashier/Staff** can add, edit, or archive owners. Administrators can only view.
2. Staff only see owners from **their own clinic** (filtered by `Session.ClinicID`).
3. **First name, last name, and contact number are required.**
4. The contact number must be a valid PH mobile number (11 digits, starts with **09**). The email, if given, must be in a valid format.
5. **Duplicate check:** if the same contact number already exists in the clinic, show a warning before saving.
6. Owners are **archived, not deleted** (`IsDeleted = 1`), so old appointments and bills stay complete.
7. An owner **can't be archived** if they have an unpaid bill or an upcoming appointment.
8. An archived owner's pets are hidden from booking but kept in the records.
9. **New clients don't need to be registered to book.** They're booked as guests in the Appointments module, and they're registered (owner and pet) only when they **arrive and check in**. This keeps no-show clients out of the Owner records.

---

## 5. Offline / online sync

- Owners **can be added offline**. The GUID ID makes sure no two computers create the same ID.
- New and edited owners are saved with SyncStatus = **Pending** and added to the SyncQueue.
- When online, they're sent to the server and marked **Synced**, and owners added on other clinic computers are downloaded.
- If two computers edit the same owner while offline, the **newer edit wins**, based on `UpdatedAt`.
- If two computers register the same person offline, both records stay. Each gets a **"Possible duplicate"** flag (same contact number), so staff can check and archive one.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Owner.cs` |
| Model | `Models/Repositories/OwnerRepository.cs` |
| Controller | `Controllers/OwnerController.cs` |
| View | `Views/Staff/OwnersPage.xaml` (list) |
| View | `Views/Staff/OwnerDetailsPage.xaml` (profile) |
| View | `Views/Shared/OwnerFormView.xaml` (add/edit panel) |
| Helper | `Helpers/Validator.cs` (contact number and email checks) |

**OwnerController methods**
- `GetOwners(search, sortBy)`
- `GetOwnerById(ownerId)`
- `GetOwnerSummary(ownerId)`: pet count, next appointment, unpaid balance
- `AddOwner(owner)`
- `UpdateOwner(owner)`
- `ArchiveOwner(ownerId)`
- `CheckDuplicate(contactNo)`

---

## 7. UI build prompt

```
Build the "Owners" page for the VetCare veterinary clinic management system. It sits to the right of the existing VetCare sidebar, with "Owners" active in the sidebar under PET CARE (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500)
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PET CARE"
- Title (32px, Nunito 800, navy): "Owners"
- Subtitle (gray): "Keep every pet parent's details in one place."
- Right side: orange button "+ Add owner"

SUMMARY CARDS (4 in a row)
- "Total owners" (users icon, cyan circle)
- "New this month" (user-plus icon, mint circle)
- "With upcoming appointments" (calendar-days icon, pale-blue circle)
- "With unpaid balance" (circle-dollar-sign icon, light-orange circle, orange icon)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label.

TOOLBAR CARD
- Search input with a search icon, placeholder "Search name, contact no., or email"
- "Sort by" dropdown: Newest first, Name A–Z

OWNERS TABLE (inside a white card)
- Columns: Owner (40px rounded-square avatar with initials on a cyan background, full name in bold navy, email below in gray), Contact no., Address (truncate long text), Pets (count pill + up to 2 pet names in small gray text, e.g. "2 · Bantay, Mingming"), Date registered, Sync, and an actions menu (three-dot icon).
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Possible duplicate" (alert icon, amber outline).
- Row hover: background #F2F8FB. Clicking a row opens the owner profile.
- Actions menu: View profile, Edit, Add pet, Book appointment, Archive (red text).
- Pagination: "Showing 1–10 of 128 owners" with Prev/Next.
- Empty state: paw-print icon in a cyan circle, "No owners yet", subtext "Add your first pet parent to get started.", orange "Add owner" button.

ADD / EDIT OWNER PANEL (slides in from the right, 440px wide, white, soft shadow, dimmed background)
- Title "Add owner" / "Edit owner" with a close (x) icon.
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  First name*, Last name*, Contact no.* (placeholder "09XX XXX XXXX"), Email, Address (textarea, 3 rows).
- Inline red error text under invalid fields: "Required", "Enter a valid 11-digit mobile number starting with 09", "Enter a valid email".
- Duplicate warning box (amber background #FFF4E0, amber border, alert icon): "An owner with this contact number already exists: Juan Dela Cruz." with a link "View owner".
- Footer: gray outline "Cancel", navy outline "Save and add pet", orange "Save owner".

OWNER PROFILE PAGE (opens when a row is clicked)
- Back link "← Owners" at the top.
- Header card: 64px avatar with initials (cyan), full name (26px Nunito 800 navy), and below it icon rows for contact no. (phone), email (mail), address (map-pin), registered date (calendar). Sync badge on the right.
- Buttons on the right of the header: outline "Edit", outline "Add pet", orange "Book appointment".
- 3 stat chips under the header: "Pets: 2", "Next appointment: Nov 20, 10:00 AM", "Unpaid balance: ₱0.00" (orange text when above zero).
- Tabs: "Pets" (active: orange text + orange underline), "Appointments", "Billing history".
  - Pets tab: grid of pet cards (3 per row). Each card has a 48px cyan circle with a paw icon, pet name (bold navy), species · breed, age, and a small mint pill "Next vaccine: Dec 05, 2026". A dashed-border card "+ Add pet" at the end.
  - Appointments tab: table with Date, Time, Pet, Vet, Reason, Status pill (Scheduled = pale blue, Checked In = cyan, Completed = mint, Cancelled = gray, No-show = light orange).
  - Billing history tab: table with Bill date, Pet, Amount (₱), Status pill (Paid = mint, Unpaid = light orange, Partial = amber), and a "View receipt" link.

ARCHIVE DIALOG
- "Archive Juan Dela Cruz? Their records will be kept but hidden from new bookings." Buttons: Cancel / red "Archive".
- If blocked: "This owner has an unpaid bill or an upcoming appointment. Settle these first." with only an "OK" button.

SAMPLE DATA (for preview)
- Juan Dela Cruz, 09171234567, Matina, Davao City, 2 pets (Bantay, Mingming), Synced
- Maria Clara Santos, 09281234567, Lanang, Davao City, 1 pet (Choco), Synced
- Pedro Reyes, 09391234567, Toril, Davao City, 3 pets (Max, Luna, Kiko), Pending
- Ana Villanueva, 09451234567, Buhangin, Davao City, 1 pet (Snow), Possible duplicate
- Carlo Mendoza, 09561234567, Bajada, Davao City, 2 pets (Rocky, Bella), Synced

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, pet cards wrap to 1–2 per row, and the table scrolls sideways if needed.
```
