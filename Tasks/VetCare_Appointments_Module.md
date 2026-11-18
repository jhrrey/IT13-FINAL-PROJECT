# VetCare – Appointments Module ("Appointments")

**Module:** 3 – Appointments (Core)
**Sidebar location:** PET CARE → Appointments
**Used by:** Cashier/Staff (full access), Veterinarian (own schedule), Administrator (view only)
**Tables:** Appointment (linked to Pet, Owner, User, MedicalRecord, Bill)

---

## 1. What this module does

The Appointments module manages every clinic visit, from booking to completion. It connects the front desk (staff), the consultation room (vet), and the cashier (billing).

- Every appointment is for **one pet** with **one vet** at a set date and time.
- **Two kinds of booking:**
  - **Guest booking (new clients):** only the owner's name, contact no., pet name, and species are saved **on the appointment itself**. No Owner or Pet record is created yet.
  - **Registered booking (returning clients):** the staff picks the existing owner and pet.
- A guest's owner and pet are **registered only when they actually arrive** (at check-in). If they don't show up, no Owner or Pet record is ever created.
- Walk-in clients are booked for "now" and checked in right away.

| Role | Access |
|---|---|
| Cashier/Staff | Book, edit, reschedule, check in, cancel, mark no-show, view all |
| Veterinarian | View own schedule, start consultation, complete |
| Administrator | View all (read only) |
| Super Admin | No access (clinic data is private) |

---

## 2. Data used (from the ERD)

### Appointment table
| Field | Type | Notes |
|---|---|---|
| AppointmentID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | Set automatically from `Session.ClinicID` |
| PetID | UNIQUEIDENTIFIER (FK, nullable) | Empty for guest bookings until check-in; the owner comes from the pet |
| VetID | UNIQUEIDENTIFIER (FK → User) | Required; must be an active Veterinarian in the clinic |
| CreatedBy | UNIQUEIDENTIFIER (FK → User) | Set automatically to the logged-in staff |
| GuestOwnerName | VARCHAR(100) | Guest bookings only: the owner's name |
| GuestContactNo | VARCHAR(20) | Guest bookings only: the owner's contact no. |
| GuestPetName | VARCHAR(50) | Guest bookings only: the pet's name |
| GuestSpecies | VARCHAR(30) | Guest bookings only: Dog, Cat, Bird, etc. |
| AppointmentDate | DATE | Required |
| AppointmentTime | TIME | Required |
| Reason | VARCHAR(255) | Required (e.g., Check-up, Vaccination, Skin problem) |
| Status | VARCHAR(20) | See the status flow below |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown in this module
| Table | Shown as |
|---|---|
| Pet / Owner | Pet name, species, owner name, and contact no. (from the guest fields if not registered yet) |
| User | Vet name and the staff member who booked it |
| MedicalRecord | The consultation record created for the visit |
| Bill | The bill and payment status after the visit |

---

## 3. Status flow

```
Scheduled ──► Checked In ──► In Consultation ──► Completed
    │
    ├──► Cancelled
    └──► No-show
```

| Status | Meaning | Who sets it |
|---|---|---|
| **Scheduled** | Booked and waiting for the visit day | Staff (when booking) |
| **Checked In** | The pet has arrived at the clinic (guests are registered at this step) | Staff |
| **In Consultation** | The vet has started the check-up | Vet ("Start consultation") |
| **Completed** | The visit is done and ready for billing | Vet ("Complete") |
| **Cancelled** | The owner or the clinic cancelled the visit | Staff |
| **No-show** | The owner didn't arrive | Staff |

---

## 4. Screens

### A. Appointments page (main page, 3 views)
- **Summary cards:** Today's appointments, Waiting (Checked In), In consultation, Completed today.
- **Toolbar:** date picker (with Today and ◀ ▶ buttons), Vet filter, Status filter, search (pet or owner), a view switch (**Queue / Calendar / List**), and buttons for **Walk-in** and **Book appointment**.
- **Queue view (default for today):** 4 columns (Scheduled, Checked In, In Consultation, Completed). Each appointment is a card showing time, pet, owner, vet, and reason, with the next action button on it, like "Check in."
- **Calendar view:** Day or Week calendar with time rows (e.g., 8:00 AM–5:00 PM) and one column per vet in Day view. Appointments appear as colored blocks by status.
- **List view:** a table with Date, Time, Pet, Owner, Vet, Reason, Status, Sync, and Actions.

### B. Book / Edit appointment panel
- A switch at the top: **New client (guest)** or **Returning client**.
  - **New client (guest):** type the owner's name, contact no., pet name, and species. Nothing is saved to Owners or Pets.
  - **Returning client:** search the owner, then pick from that owner's active pets. If it's opened from a pet profile, both are already filled in.
- While typing a guest's contact no., the system checks whether it already belongs to a registered owner and suggests switching to **Returning client**, to avoid duplicates.
- **Vet** dropdown (active vets in the clinic).
- **Date**, then **available time slots** as buttons. Taken slots are grayed out.
- **Reason**: quick-pick chips (Check-up, Vaccination, Follow-up, Deworming, Grooming, Skin problem, Other), plus a text box.
- Buttons: **Cancel** and **Book appointment** (or **Save changes** when editing).

### C. Walk-in panel
- The same form, but the date and time are set to **now**, and it shows the next available vet.
- A walk-in client is already at the clinic, so a new client is **registered right away** (owner and pet), and the appointment is **checked in at once**.

### C2. Check-in panel (for guest bookings)
- Opens when the staff clicks **Check in** on a guest booking.
- Shows the guest info typed during booking.
- **Step 1 – Owner:** the system searches for a registered owner with the same contact no.
  - **Match found:** pick **Use this owner**.
  - **No match:** the Add owner form opens, already filled in with the guest name and contact no. The staff completes the rest (email, address).
- **Step 2 – Pet:** pick one of the owner's pets, or the Add pet form opens with the guest pet name and species filled in.
- **Check in:** links the new or existing `PetID` to the appointment, clears the guest fields, and sets the status to **Checked In**.

### D. Appointment details panel
- Pet and owner info (with contact no.), vet, date and time, reason, status, booked by, and sync status.
- A **status timeline** showing when it was booked, checked in, started, and completed.
- **Action buttons** depend on the status and role:
  - Staff: Check in, Reschedule, Cancel, Mark no-show
  - Vet: Start consultation (opens Medical Records), Complete
- Links appear when they exist: **View medical record** and **View bill**.

### E. My Schedule (Vet)
- The vet sees **only their own appointments** for the day, in time order.
- Each one has a **Start consultation** button once the pet is Checked In.

---

## 5. Rules (checked in AppointmentController)

1. Only **Cashier/Staff** can book, edit, check in, cancel, or mark no-show. Only the **assigned vet** can start and complete a consultation.
2. Staff only see appointments from **their own clinic** (`Session.ClinicID`).
3. **Vet, date, time, and reason are always required.** For a guest booking, the **owner name, contact no., pet name, and species** are required. For a returning client, the **pet** is required.
4. **No double booking:** a vet can't have two active appointments (Scheduled, Checked In, In Consultation) at the same date and time.
5. You can't book a date or time that has **already passed** (except walk-ins, which use "now").
6. For returning clients, the pet must be **active** (not archived), and its owner must belong to the clinic.
7. The vet must be an **active Veterinarian** in the same clinic.
8. Check-in is only allowed on the **appointment day**. **A guest booking can't be checked in until its owner and pet are registered or matched.** After check-in, every appointment has a `PetID`, so medical records, vaccinations, and bills always link to real records.
9. **No-show** can only be set after the appointment time has passed.
10. **Completed** needs a **Medical Record** for the visit.
11. **Completed, Cancelled, and No-show** appointments can't be edited.
12. **Rescheduling** keeps the same appointment and only changes the date and time, with the same double-booking check.
13. Cancelling **changes the status** instead of deleting, so the history stays.
14. Time slots follow the clinic's hours, e.g., **8:00 AM–5:00 PM** in **30-minute** steps. These can be saved as system settings.
15. **Guest info cleanup:** for guest bookings that end as **Cancelled** or **No-show**, the guest name and contact no. are cleared after **30 days**. The appointment row stays (with its date, vet, reason, and status) so no-show counts still show up in Reports, but no personal details are kept.

---

## 6. Offline / online sync

- Appointments **can be booked, checked in, and updated offline**.
- Changes are saved with SyncStatus = **Pending** and added to the SyncQueue.
- When online, they're sent to the server and marked **Synced**, and bookings from other clinic computers are downloaded.
- **Double-booking conflict:** if two computers book the same vet and time slot while offline, the later booking is marked **Conflict**. It shows a red badge and a "Reschedule" prompt so the staff can fix it.
- If the same appointment is changed on two computers, the **newer change wins**, based on `UpdatedAt`.

---

## 7. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Appointment.cs` |
| Model | `Models/Repositories/AppointmentRepository.cs` |
| Controller | `Controllers/AppointmentController.cs` |
| View | `Views/Staff/AppointmentsPage.xaml` (queue, calendar, list) |
| View | `Views/Veterinarian/VetSchedulePage.xaml` (my schedule) |
| View | `Views/Shared/AppointmentFormView.xaml` (book/edit/walk-in panel) |
| View | `Views/Shared/AppointmentDetailsView.xaml` (details panel) |
| Helper | `Helpers/TimeSlotHelper.cs` (builds available slots) |

**AppointmentController methods**
- `GetAppointments(date, vetId, status, search)`
- `GetVetSchedule(vetId, date)`
- `GetAppointmentById(appointmentId)`
- `GetTodaySummary()`
- `GetAvailableSlots(vetId, date)`
- `BookAppointment(appointment)`
- `BookWalkIn(appointment)`
- `UpdateAppointment(appointment)`
- `Reschedule(appointmentId, newDate, newTime)`
- `CheckIn(appointmentId)` (returning clients)
- `FindOwnerByContact(contactNo)` (used while booking and at check-in)
- `CheckInGuest(appointmentId, ownerId, petId)` (links the registered or matched owner and pet)
- `ClearOldGuestInfo()` (runs daily; clears guest details of old no-shows and cancellations)
- `StartConsultation(appointmentId)`
- `Complete(appointmentId)`
- `Cancel(appointmentId)`
- `MarkNoShow(appointmentId)`

---

## 8. UI build prompt

```
Build the "Appointments" page for the VetCare veterinary clinic management system. It sits to the right of the existing VetCare sidebar, with "Appointments" active in the sidebar under PET CARE (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

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

STATUS COLORS (use everywhere: pills, card borders, calendar blocks)
- Scheduled = pale blue #DCEBFB, navy text
- Checked In = cyan #B8F0F2, navy text
- In Consultation = amber #FFF4E0, dark amber text #8A5A00
- Completed = mint #C4F1E1, dark green text #1E6B4F
- Cancelled = light gray #EEF1F4, gray text, strikethrough time
- No-show = light orange #FDE8E1, orange text

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "PET CARE"
- Title (32px, Nunito 800, navy): "Appointments"
- Subtitle (gray): "Every visit, from hello to all done."
- Right side: navy outline button "Walk-in" (footprints icon) and orange button "+ Book appointment"

SUMMARY CARDS (4 in a row)
- "Today's appointments" (calendar-days icon, cyan circle)
- "Waiting" (clock icon, pale-blue circle)
- "In consultation" (stethoscope icon, amber circle)
- "Completed today" (circle-check icon, mint circle)
Each card: 44px icon circle, big number (28px navy Nunito 800), gray label.

TOOLBAR CARD
- Left: "Today" outline button, ◀ ▶ icon buttons, and the selected date in bold navy ("Tuesday, Nov 17, 2026") which opens a date picker.
- Middle: "All vets" dropdown, "All status" dropdown, search input ("Search pet or owner").
- Right: segmented view switch with 3 options: "Queue" (layout-columns icon), "Calendar" (calendar icon), "List" (list icon). Active option has a cyan background and bold navy text.

QUEUE VIEW (default)
- 4 columns side by side: "Scheduled", "Checked In", "In Consultation", "Completed". Each column has a header with its name, a count pill, and a 3px top border in its status color.
- Appointment cards (white, 10px radius, 1px border, 4px left border in status color, padding 14px):
  - Time in bold navy ("9:30 AM"), sync badge on the right if Pending or Conflict
  - Pet row: 32px species icon circle + pet name (bold navy) + "Dog · Aspin" (gray)
  - Owner name with a user icon, vet name with a stethoscope icon ("Dr. Maria Santos")
  - Reason pill (light gray)
  - One action button at the bottom based on status: Scheduled → "Check in" (navy outline); Checked In → "Start consultation" (orange); In Consultation → "Complete" (mint); Completed → "Create bill" (navy text link)
- Guest bookings show a small light-gray pill "Guest" next to the pet name, and the pet row uses a generic paw icon.
- Clicking a card opens the details panel.
- Empty column text: "Nothing here yet" in gray.

CALENDAR VIEW
- "Day / Week" small toggle at the top.
- Day view: time rows from 8:00 AM to 5:00 PM every 30 minutes on the left, one column per vet (vet name + small avatar in the header). Appointments are rounded blocks in their status color, showing time, pet name, and reason. Clicking an empty slot opens "Book appointment" with that vet, date, and time filled in.
- Week view: 7 day columns, blocks show time + pet name, colored by status.
- A red horizontal line marks the current time.

LIST VIEW
- Table columns: Date, Time, Pet (avatar + name), Owner, Vet, Reason, Status pill, Sync badge, actions menu.
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red, with tooltip "Double booking — please reschedule").
- Row hover: #F2F8FB. Pagination: "Showing 1–10 of 46 appointments".
- Actions menu: View details, Check in, Reschedule, Cancel (red), Mark no-show.

BOOK / EDIT APPOINTMENT PANEL (slides in from the right, 480px wide, white, soft shadow, dimmed background)
- Title "Book appointment" / "Edit appointment" with a close (x) icon.
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  0. Client type switch at the top (segmented control, full width): "New client (guest)" | "Returning client". Selected = cyan background, bold navy text. Default: "New client (guest)".
  If "New client (guest)":
  1. Owner name* (text)
  2. Contact no.* (placeholder "09XX XXX XXXX"). If the number matches a registered owner, show a cyan info box under it: "This number belongs to Juan Dela Cruz (registered). Use Returning client?" with a link "Switch".
  3. Pet name* (text) and Species* (dropdown: Dog, Cat, Bird, Rabbit, Hamster, Other) side by side
  - Small gray note under these fields: "The owner and pet will be registered when they arrive."
  If "Returning client":
  1. Owner* (searchable dropdown showing name + contact no.)
  2. Pet* (dropdown of that owner's pets, each with a species icon; disabled until an owner is chosen)
  Then, for both:
  3. Vet* (dropdown with vet avatars and names)
  4. Date* (date picker, past dates disabled)
  5. Time* — a grid of time-slot buttons (4 per row, e.g. "8:00 AM", "8:30 AM"). Available = white with border; Selected = orange background, white text; Taken = light gray, strikethrough, disabled. Text above the grid: "Available slots for Dr. Maria Santos".
  6. Reason* — quick-pick chips (Check-up, Vaccination, Follow-up, Deworming, Grooming, Skin problem, Other); selected chip = cyan background, bold navy text. Below: a textarea "Add details (optional)".
- Inline red errors: "Required", "This slot was just taken — pick another time".
- Footer: gray outline "Cancel", orange "Book appointment" (or "Save changes").

WALK-IN PANEL
- Same as the booking panel, with a cyan info bar at the top: "Walk-in · Today, 10:42 AM". The Date and Time fields are hidden, and the vet dropdown shows "Next available" first.
- For a new client, the panel shows the full Add owner fields (first name, last name, contact no., email, address) and Add pet fields (pet name, species, breed, sex, birth date) because they're already at the clinic.
- Footer button: orange "Register & check in" (new client) or "Book & check in" (returning client).

CHECK-IN PANEL FOR GUEST BOOKINGS (slides in from the right, 480px wide)
- Opens when "Check in" is clicked on a card with the "Guest" pill.
- Title "Check in guest" + time and vet in gray.
- Cyan info box with what was typed during booking: "Pedro Garcia · 09671234567 · Pet: Bruno (Dog)".
- Step indicator at the top: "1 Owner" → "2 Pet" (active step = orange circle, done step = mint circle with check).
- Step 1 – Owner:
  - If a registered owner has the same contact no.: a white card showing that owner (name, contact no., number of pets) with an orange button "Use this owner", and a text link "Register as a new owner instead".
  - If no match: the Add owner form, already filled in with First name and Last name (split from the guest name) and Contact no. Email and Address are optional.
- Step 2 – Pet:
  - If the owner has pets: pet cards to pick from (species icon, name, breed), plus a dashed-border card "+ Add new pet".
  - Add pet form, already filled in with Pet name and Species: Breed, Sex*, Birth date or estimated age, Color, Weight.
- Footer: gray outline "Back", orange "Check in" (enabled only when both an owner and a pet are chosen or filled in).
- After success: green toast "Bruno is checked in. Owner and pet saved to records."

APPOINTMENT DETAILS PANEL (slides in from the right, 460px wide)
- Top: status pill + sync badge, and a close icon.
- Pet card: 48px species icon circle, pet name (22px Nunito 800), "Dog · Aspin · Male", and a link "View pet →".
- Owner row: name, phone icon + contact no.
- Info rows with icons: calendar (date), clock (time), stethoscope (vet), message-square (reason), user (booked by).
- Status timeline (vertical, small dots): Booked — Nov 15, 2:10 PM by John Dela Cruz; Checked in — 9:24 AM; Consultation started — 9:35 AM; Completed — 10:05 AM. Future steps are shown as gray hollow dots.
- Links (when available): "View medical record →", "View bill →" in orange.
- Footer buttons depend on status:
  - Scheduled: outline "Reschedule", red text "Cancel", navy "Check in"
  - Checked In: orange "Start consultation" (vet only)
  - In Consultation: mint "Complete" (vet only)
  - Completed: orange "Create bill" (staff)
  - If the user's role can't do the action, hide the button.

CONFIRM DIALOGS
- Cancel: "Cancel Bantay's appointment on Nov 20 at 10:00 AM?" with an optional "Reason for cancelling" text box. Buttons: "Keep it" / red "Cancel appointment".
- No-show: "Mark this appointment as no-show?" Buttons: Cancel / orange "Mark no-show".
- Complete blocked: "Add a medical record before completing this visit." with an orange button "Open medical record".

SAMPLE DATA (for preview, today = Nov 17, 2026)
- 8:30 AM — Choco (Dog, Shih Tzu) — Maria Clara Santos — Dr. Maria Santos — Check-up — Completed
- 9:00 AM — Bantay (Dog, Aspin) — Juan Dela Cruz — Dr. Maria Santos — Vaccination — In Consultation
- 9:30 AM — Luna (Cat, Persian) — Pedro Reyes — Dr. Grace Lim — Skin problem — Checked In
- 10:00 AM — Max (Dog, Labrador) — Pedro Reyes — Dr. Grace Lim — Vaccination — Checked In
- 10:30 AM — Snow (Cat, Siamese) — Ana Villanueva — Dr. Maria Santos — Follow-up — Scheduled
- 1:00 PM — Rocky (Dog, Beagle) — Carlo Mendoza — Dr. Grace Lim — Deworming — Scheduled (Pending sync)
- 1:30 PM — Bella (Dog, Pomeranian) — Carlo Mendoza — Dr. Grace Lim — Grooming — Scheduled (Conflict)
- 3:00 PM — Mingming (Cat, Puspin) — Juan Dela Cruz — Dr. Maria Santos — Check-up — Cancelled
- 8:00 AM — Kiko (Bird, Lovebird) — Pedro Reyes — Dr. Maria Santos — Check-up — No-show
- 11:00 AM — Bruno (Dog) — Pedro Garcia (guest, 09671234567) — Dr. Grace Lim — Check-up — Scheduled (Guest)
- 2:00 PM — Whiskers (Cat) — Liza Tan (guest, 09781234567) — Dr. Maria Santos — Vaccination — Scheduled (Guest)

RESPONSIVE
- Summary cards wrap to 2 per row, the queue columns scroll sideways, the calendar shows Day view only on small widths, and tables scroll sideways if needed.
```
