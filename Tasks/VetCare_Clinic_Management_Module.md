# VetCare – Clinic Management Module ("Clinics")

**Module:** 9 – User & Clinic Management (clinic side, Super Admin)
**Sidebar location:** CLINIC & TEAM → Clinics
**Used by:** Super Admin only
**Tables:** Clinic (linked to User, Device, SyncLog; reads counts from Owner, Pet, Appointment)

---

## 1. What this module does

Clinic Management is where the **Super Admin runs the whole VetCare platform**. Since VetCare is multi-tenant, every clinic that uses the system is a **tenant**, and this module controls them.

- **Review new clinic sign-ups** from the "Create account" form and **approve or reject** them.
- **Add, edit, activate, and deactivate** clinics.
- **Manage each clinic's Administrator accounts** (using the User Management rules).
- **Watch each clinic's computers and sync health.**

**Privacy rule:** the Super Admin only sees **counts and statuses** for each clinic (e.g., "214 pets"). They **never** see a clinic's owners, pets, medical records, or bills.

| Role | Access |
|---|---|
| Super Admin | Full access to all clinics |
| Administrator / Veterinarian / Cashier/Staff | No access (each clinic only sees its own data) |

---

## 2. Data used (from the ERD)

### Clinic table
| Field | Type | Notes |
|---|---|---|
| ClinicID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicName | VARCHAR(100) | Required |
| Address | VARCHAR(255) | Required |
| ContactNo | VARCHAR(20) | Required |
| Email | VARCHAR(100) | Required, unique |
| Status | VARCHAR(20) | Pending, Active, Inactive, Rejected |
| DateCreated | DATETIME | Set automatically |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown
| Table | Shown as |
|---|---|
| User | The clinic's Administrator(s), and the number of users by role |
| Device | The clinic's registered computers and their last sync time |
| SyncLog | Recent syncs of the clinic's computers (success or failed) |
| Owner, Pet, Appointment | **Counts only** (e.g., number of pets, appointments this month) |

### Clinic status
| Status | Meaning | Can log in? |
|---|---|---|
| **Pending** | Signed up, waiting for review | No |
| **Active** | Approved and using VetCare | Yes |
| **Inactive** | Turned off by the Super Admin (e.g., stopped using the system) | No |
| **Rejected** | Sign-up was not approved | No |

---

## 3. How a clinic joins VetCare

1. A clinic fills out **Create account** on the login screen: clinic name, address, contact no., administrator name, email, and password.
2. The system creates the **Clinic (Pending)** and its **Administrator account (Inactive)**.
3. The Super Admin **reviews** the sign-up in the **Pending approval** tab.
4. **Approve:** the clinic becomes **Active**, and its Administrator can log in.
   **Reject:** the clinic becomes **Rejected** with a reason, and the admin account stays inactive.
5. Once logged in, the clinic's Administrator creates the Vet and Staff accounts and registers the clinic's computers.

---

## 4. Screens

### A. Clinics page (main page, 2 tabs)
- **Summary cards:** Total clinics, Active, Pending approval, Inactive.
- **Tab 1 – All clinics:**
  - Toolbar: search (clinic name, email, address), Status filter, and an **Add clinic** button.
  - Table columns: Clinic (name + email), Address, Contact no., Administrator, Users, Devices, Last sync, Status, Date registered, Actions.
  - Actions: View, Edit, Deactivate / Activate.
- **Tab 2 – Pending approval:** new sign-ups waiting for review, oldest first.
  - Columns: Clinic, Address, Contact no., Administrator (name + email), Date signed up, and a **Review** button.

### B. Review sign-up panel
- **Clinic details:** name, address, contact no., email, and date signed up.
- **Administrator details:** name, email, and username.
- **Duplicate check:** a warning if another clinic has the same name, email, or contact no.
- Buttons: **Reject** (with a required reason) and **Approve**.

### C. Add / Edit clinic panel
- Clinic name, Address, Contact no., Email.
- When adding: the **first Administrator account** (first name, last name, username, email, temporary password). A clinic added by the Super Admin is **Active right away**.

### D. Clinic details page
- **Header:** clinic name, status, address, contact no., email, date registered, and buttons for Edit and Deactivate / Activate.
- **Tabs:**
  - **Overview:** counts only: users by role, owners, pets, appointments this month, and last activity.
  - **Administrators:** the clinic's admin accounts, with **Add admin**, **Reset password**, and **Deactivate**.
  - **Devices:** the clinic's computers (device name, last sync, active or not), with **Deactivate device** for lost or replaced computers.
  - **Sync log:** the clinic's recent syncs (device, user, direction, records sent and received, status, error message).

---

## 5. Rules (checked in ClinicController)

1. Only the **Super Admin** can open this module.
2. **Clinic name, address, contact no., and email are required.** The email must be **unique**, and the contact no. must be a valid PH number.
3. A **duplicate warning** shows if another clinic has the same name and address, email, or contact no.
4. Only **Pending** clinics can be approved or rejected.
5. **Approving** sets the clinic to **Active** and turns on its Administrator account.
6. **Rejecting** needs a **reason**. Rejected sign-ups are cleared (soft-deleted) after **30 days**, so no unused data is kept.
7. **Deactivating** a clinic needs a **confirmation and a reason**:
   - All of the clinic's users are **blocked from logging in**.
   - All of its devices are **blocked from syncing**.
   - Its data is **kept**, not deleted, and comes back when the clinic is activated again.
8. **Activating** a clinic again needs at least **one active Administrator**.
9. **Clinics with data are never deleted**, only deactivated.
10. The Super Admin sees **counts only**, never a clinic's owners, pets, medical records, or bills.
11. Approve, reject, activate, and deactivate **need an internet connection**, because the central server must apply the change for every clinic computer.

> **Optional ERD additions:**
> - `Remarks VARCHAR(255)` on Clinic, to save the reject or deactivate reason.
> - `ApprovedBy` (FK → User) and `ApprovedAt DATETIME` on Clinic, to record who approved it and when.

---

## 6. Offline / online sync

- Clinic changes are made **online**, on the central server.
- Each clinic computer **downloads its clinic's status** on every sync and login.
- If a clinic is **deactivated**, its computers keep working **offline** until they reconnect. On the next sync, their **pending changes are still accepted** (so no data is lost), and then the users are logged out and blocked.
- If a device is **deactivated** (e.g., stolen laptop), its next sync is refused, and it can no longer log in.
- The **Devices** and **Sync log** tabs help the Super Admin spot clinics whose computers haven't synced for a long time or keep failing.

---

## 7. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Clinic.cs` |
| Model | `Models/Repositories/ClinicRepository.cs` |
| Model (used) | `UserRepository.cs`, `DeviceRepository.cs`, `SyncLogRepository.cs` |
| Controller | `Controllers/ClinicController.cs` |
| View | `Views/SuperAdmin/ClinicsPage.xaml` (All clinics + Pending approval) |
| View | `Views/SuperAdmin/ClinicDetailsPage.xaml` |
| View | `Views/SuperAdmin/ReviewClinicView.xaml` (review sign-up panel) |
| View | `Views/Shared/ClinicFormView.xaml` (add/edit clinic) |
| Helper | `Helpers/Validator.cs` |

**ClinicController methods**
- `GetClinics(search, status)`
- `GetPendingClinics()`
- `GetClinicSummary()`: total, active, pending, inactive
- `GetClinicById(clinicId)`
- `GetClinicOverview(clinicId)`: counts only
- `CheckDuplicate(name, address, email, contactNo)`
- `RegisterClinic(clinic, admin, password)` (called from the "Create account" form)
- `AddClinic(clinic, admin, password)` (Super Admin)
- `UpdateClinic(clinic)`
- `ApproveClinic(clinicId)`
- `RejectClinic(clinicId, reason)`
- `DeactivateClinic(clinicId, reason)`
- `ActivateClinic(clinicId)`
- `GetClinicAdmins(clinicId)`
- `GetClinicDevices(clinicId)`
- `SetDeviceActive(deviceId, isActive)`
- `GetClinicSyncLogs(clinicId)`

---

## 8. UI build prompt

```
Build the "Clinics" pages for the VetCare veterinary clinic management system. Only the Super Admin uses them. They sit to the right of the existing VetCare sidebar, with "Clinics" active under CLINIC & TEAM (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style. The sidebar's clinic switcher card shows "All clinics" / "Super Admin workspace" for this role.

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

CLINIC STATUS PILLS (use everywhere)
- Active = mint #C4F1E1, dark green text #1E6B4F, dot
- Pending = amber #FFF4E0, dark amber text #8A5A00, clock icon
- Inactive = light gray #EEF1F4, gray text, pause icon
- Rejected = light red #FDE2E2, red text #B42318, x icon

=== PAGE 1: CLINICS ===

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "CLINIC & TEAM"
- Title (32px, Nunito 800, navy): "Clinics"
- Subtitle (gray): "Every clinic on VetCare, in one place."
- Right side: orange button "+ Add clinic"

SUMMARY CARDS (4 in a row; 44px icon circle, big number 28px Nunito 800 navy, gray label)
- "Total clinics" — 6 (building-2 icon, cyan circle)
- "Active" — 3 (circle-check icon, mint circle)
- "Pending approval" — 2 (clock icon, amber circle) — clicking opens the Pending tab
- "Inactive" — 1 (pause-circle icon, light-gray circle)

TABS
- "All clinics" (active: orange text + 2px orange underline) and "Pending approval" with an amber count pill "2".

TAB 1 – ALL CLINICS
- Toolbar card: search ("Search clinic name, email, or address"), "All status" dropdown.
- Table inside a white card:
  - Columns: Clinic (40px rounded-square tile with the clinic's initials on cyan, clinic name in bold navy, email below in gray), Address, Contact no., Administrator (name), Users (number), Devices (number), Last sync ("2 min ago"; red text "3 days ago" when over 24 hours), Status pill, Date registered, actions menu.
  - Actions menu: View details, Edit, Deactivate (red) / Activate (green).
  - Row hover: #F2F8FB. Clicking a row opens the clinic details page.
  - Pagination: "Showing 1–6 of 6 clinics".

TAB 2 – PENDING APPROVAL
- Cards list (one card per sign-up, full width, white):
  - Left: clinic initials tile, clinic name (18px bold navy), address and contact no. in gray, "Signed up Nov 16, 2026 · 2 days ago".
  - Middle: "Administrator" label (small uppercase gray), admin name and email.
  - Right: orange button "Review".
  - If a duplicate is found: amber pill "Possible duplicate" on the card.
- Empty state: building-2 icon in a mint circle, "No pending sign-ups", subtext "New clinic registrations will appear here."

REVIEW SIGN-UP PANEL (slides in from the right, 480px wide, white, soft shadow, dimmed background)
- Title "Review sign-up" + amber "Pending" pill, close icon.
- Section "Clinic" (building-2 icon): name, address, contact no., email, date signed up — as label/value rows.
- Section "Administrator" (user-cog icon): full name, username, email.
- Duplicate check box:
  - No duplicates: mint box with check icon "No matching clinics found."
  - Duplicate: amber box with alert icon "A clinic with the same contact no. already exists: VetCare Clinic – Matina (Active)." and a link "View clinic".
- Footer: red outline "Reject", orange "Approve clinic".
- Reject dialog: "Reject PetLife Animal Clinic?" with a required textarea "Reason (sent to the applicant)" and buttons Cancel / red "Reject". Small gray note: "Rejected sign-ups are removed after 30 days."
- Approve dialog: "Approve PetLife Animal Clinic? Their administrator, Rafael Cruz, will be able to log in right away." Buttons Cancel / orange "Approve".
- After success: green toast "PetLife Animal Clinic is now active."

ADD / EDIT CLINIC PANEL (slides in from the right, 480px wide)
- Title "Add clinic" / "Edit clinic".
- Section "Clinic details": Clinic name*, Address* (textarea), Contact no.* (placeholder "09XX XXX XXXX" or landline), Email*.
- Section "First administrator" (only when adding): First name*, Last name*, Username*, Email*, Temporary password*, Confirm password*.
- Inputs: 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius; labels bold navy 15px.
- Inline red errors: "Required", "This email is already used by another clinic", "Enter a valid contact number", "Passwords don't match".
- Footer: gray outline "Cancel", orange "Save clinic". Note under the title when adding: "Clinics added here are active right away."

DEACTIVATE / ACTIVATE DIALOGS
- Deactivate: "Deactivate Buhangin Vet Clinic? Its 5 users won't be able to log in and its 2 computers will stop syncing. All data will be kept." Required textarea "Reason". Buttons Cancel / red "Deactivate".
- Activate: "Activate Buhangin Vet Clinic again? Users with active accounts can log in right away." Buttons Cancel / green-outlined "Activate". If there's no active admin: "Add or activate an administrator first."

=== PAGE 2: CLINIC DETAILS ===
- Back link "← Clinics".
- Header card: 64px initials tile, clinic name (28px Nunito 800 navy), status pill, and info rows with icons: map-pin (address), phone (contact no.), mail (email), calendar (registered Nov 2, 2025). Right side: outline "Edit", red outline "Deactivate" (or green outline "Activate").
- Privacy note under the header (small gray text with a lock icon): "Super Admins can see counts only. Patient and billing details stay private to the clinic."
- Tabs: "Overview" (active), "Administrators", "Devices", "Sync log".

OVERVIEW TAB
- 6 stat cards (3 per row): "Users" 7 (with "1 admin · 2 vets · 4 staff" in gray), "Owners" 128, "Pets" 214, "Appointments this month" 142, "Devices" 3, "Last activity" "Today, 10:42 AM".
- Small card "Sync health": mint "All devices synced in the last 24 hours" or amber "1 device hasn't synced in 3 days".

ADMINISTRATORS TAB
- Orange button "+ Add admin" on the right.
- Table: Admin (initials avatar + name + email), Username, Status pill (Active = mint, Inactive = gray), Last login, actions menu (Edit, Reset password, Deactivate).

DEVICES TAB
- Table: Device (monitor icon + device name), Machine code (gray, monospace), Registered, Last sync, Status pill (Active = mint, Inactive = gray), and a toggle "Active". Turning it off shows: "Deactivate Consultation Room 2? It won't be able to sync or log in again."
- Rows: Front Desk PC — VC-FD-01 — Synced 2 min ago — Active; Consultation Room 1 — VC-CR-01 — 10 min ago — Active; Consultation Room 2 — VC-CR-02 — 3 days ago (red) — Active.

SYNC LOG TAB
- Filters: date range, "All devices", "All status".
- Table: Date & time, Device, User, Direction pill (Push = pale blue, Pull = cyan), Records sent, Records received, Conflicts, Status pill (Success = mint, Failed = light red), and an error message in red gray text when failed (e.g., "Connection timed out").

SAMPLE DATA (for preview, today = Nov 18, 2026)
- VetCare Clinic – Matina — Matina, Davao City — 0822 123 4567 — matina@vetcare.ph — Admin: Ana Reyes — 7 users — 3 devices — 2 min ago — Active — Nov 2, 2025
- Happy Paws Veterinary Clinic — Lanang, Davao City — 0917 222 3344 — hello@happypaws.ph — Admin: Mark Uy — 5 users — 2 devices — 15 min ago — Active — Feb 14, 2026
- Bajada Pet Care Center — Bajada, Davao City — 0918 555 1212 — care@bajadapets.ph — Admin: Joy Lim — 4 users — 1 device — 1 hour ago — Active — Jul 1, 2026
- Buhangin Vet Clinic — Buhangin, Davao City — 0920 777 8899 — buhanginvet@gmail.com — Admin: Leo Ramos — 5 users — 2 devices — 12 days ago — Inactive — Mar 9, 2026
- PetLife Animal Clinic — Toril, Davao City — 0919 333 4455 — petlife.toril@gmail.com — Admin: Rafael Cruz (rcruz) — signed up Nov 16, 2026 — Pending
- Mintal Animal Wellness — Mintal, Davao City — 0822 123 4567 — mintalvet@gmail.com — Admin: Carla Diaz — signed up Nov 17, 2026 — Pending (Possible duplicate: same contact no. as VetCare Clinic – Matina)

RESPONSIVE
- Summary cards wrap to 2 per row, pending cards stack their parts, side panels become full width, and tables scroll sideways if needed.
```
