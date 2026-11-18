# VetCare – Clinic Admin Accounts Module ("Clinic admins")

**Module:** 9 – User & Clinic Management (admin accounts, Super Admin side)
**Sidebar location:** CLINIC & TEAM → Users & roles (shown as **"Clinic admins"** when the Super Admin is logged in)
**Used by:** Super Admin only
**Tables:** User, Role, Clinic

---

## 1. What this module does

Clinic Admin Accounts is where the **Super Admin manages the Administrator account of every clinic**. Each clinic's Administrator then manages their own Vet and Staff accounts (see the User Management module).

- See **every clinic's Administrator(s)** in one list.
- **Add** an admin to a clinic, **edit** their details, **reset** their password, and **activate or deactivate** them.
- Make sure **every active clinic always has at least one active admin**.

**Who manages whom:**
```
Super Admin ──► Clinic Administrators ──► Veterinarians and Cashier/Staff
 (this module)      (User Management module)
```

**Privacy rule:** the Super Admin can manage admin **accounts**, but can **never log in as them** or see their clinic's owners, pets, medical records, or bills.

| Role | Access |
|---|---|
| Super Admin | Full access to all Administrator accounts |
| Administrator | Can only edit their **own** profile and password (from their profile menu) |
| Veterinarian / Cashier/Staff | No access |

> Super Admin accounts are **not** managed here. The first Super Admin is created by the database seed script (`02_SeedData.sql`).

---

## 2. Data used (from the ERD)

### User table (Administrator accounts only, RoleID = 2)
| Field | Type | Notes |
|---|---|---|
| UserID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | Required; the clinic this admin manages |
| RoleID | INT (FK) | Always **2 – Administrator** in this module |
| FirstName / LastName | VARCHAR(50) | Required |
| Username | VARCHAR(50) | Required, unique across the whole system |
| PasswordHash | VARCHAR(255) | Saved as a hash, never plain text |
| Email | VARCHAR(100) | Required, unique |
| ContactNo | VARCHAR(20) | Optional |
| Status | VARCHAR(20) | Active / Inactive |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Linked data shown
| Table | Shown as |
|---|---|
| Clinic | Clinic name and clinic status beside each admin |
| Role | Always "Administrator" (fixed) |

### Account status shown on screen (calculated)
| Shown as | Rule | Can log in? |
|---|---|---|
| **Active** | User is Active and the clinic is Active | Yes |
| **Inactive** | User is Inactive | No |
| **Waiting for approval** | The clinic is still Pending | No |
| **Clinic inactive** | User is Active, but the clinic is Inactive | No |

---

## 3. Screens

### A. Clinic admins page (main page)
- **Summary cards:** Total admins, Active, Inactive, Clinics without an active admin.
- **Warning card** (only when needed): a list of **active clinics with no active admin**, each with an **Add admin** button.
- **Toolbar:** search (name, username, email), Clinic filter, Status filter, a **Group by clinic** switch, and an **Add admin** button.
- **Table columns:** Admin (initials avatar, name, email), Username, Clinic (name + small clinic status pill), Contact no., Status, Date created, Sync, Actions.
- **Row actions:** View, Edit, Reset password, Deactivate / Activate.

### B. Add / Edit admin panel
- **Clinic** dropdown (only **Active** and **Pending** clinics).
- First name, Last name, Username, Email, Contact no.
- **Temporary password** (with a **Generate** button) and Confirm password (Add only).
- Status toggle (Active / Inactive).

### C. Reset password panel
- Shows the admin's name, username, and clinic.
- **Generate temporary password** button. The password is shown **once**, with a **Copy** button.
- A note reminding the Super Admin to give the password to the admin privately.

### D. Admin details panel
- Profile info, clinic and clinic status, account status, date created, last updated, and sync status.
- Buttons: Edit, Reset password, Deactivate / Activate.

---

## 4. Rules (checked in UserController)

1. Only the **Super Admin** can open this module.
2. This module only creates and manages **Administrator** accounts (RoleID = 2). Vet and Staff accounts are managed by each clinic's Administrator.
3. An admin must belong to **one clinic**. Admins can't be moved to another clinic. Instead, a new account is created for the other clinic.
4. Admins can only be added to **Active** or **Pending** clinics, not to Rejected ones.
5. **Username and email must be unique** across the whole system.
6. **Password rules:** at least 8 characters, with at least 1 letter and 1 number. Passwords are saved as a **hash** (e.g., BCrypt).
7. **Every Active clinic must keep at least one active admin.** The last active admin of a clinic can't be deactivated until another admin is added.
8. Deactivating an admin **doesn't affect** the clinic's Vet and Staff accounts.
9. Admins of a **Pending** or **Inactive** clinic **can't log in**, even if their own account is Active.
10. Admin accounts are **deactivated, not deleted**, so their name stays on past records.
11. The Super Admin **can't log in as** an admin and never sees their clinic's private records.
12. Adding an admin and resetting a password **need an internet connection**, so the server can check that the username is unique and send the account to the clinic's computers.

> **Optional ERD additions:**
> - `LastLoginAt DATETIME` on User, to show when each admin last logged in.
> - `MustChangePassword BIT` on User, to force the admin to change a temporary password at their next login.

---

## 5. Offline / online sync

- Admin accounts are created and changed **online**, on the central server.
- New or changed accounts reach the clinic's computers on their **next sync**, so the admin can then log in there, even offline.
- If an admin is **deactivated** or their **password is reset**, the change applies on each clinic computer at its next sync.
- **Recommended safety rule:** a computer only allows **offline login** if it has synced within the last **7 days**, so a deactivated account can't keep working offline forever.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/User.cs`, `Role.cs`, `Clinic.cs` |
| Model | `Models/Repositories/UserRepository.cs` |
| Model (used) | `ClinicRepository.cs` |
| Controller | `Controllers/UserController.cs` (shared with User Management) |
| View | `Views/SuperAdmin/ClinicAdminsPage.xaml` |
| View | `Views/Shared/UserFormView.xaml` (add/edit panel, shared) |
| View | `Views/SuperAdmin/ResetPasswordView.xaml` |
| Helper | `Helpers/PasswordHasher.cs`, `Helpers/PasswordGenerator.cs`, `Helpers/Validator.cs` |

**UserController methods (Super Admin side)**
- `GetClinicAdmins(search, clinicId, status)`
- `GetClinicAdminSummary()`: total, active, inactive, clinics without an active admin
- `GetClinicsWithoutActiveAdmin()`
- `AddClinicAdmin(user, clinicId, tempPassword)`
- `UpdateClinicAdmin(user)`
- `ResetPassword(userId)`: returns a new temporary password
- `SetStatus(userId, isActive)`
- `CountActiveAdmins(clinicId)`

---

## 7. UI build prompt

```
Build the "Clinic admins" page for the VetCare veterinary clinic management system. Only the Super Admin uses it. It sits to the right of the existing VetCare sidebar. For the Super Admin, the sidebar item under CLINIC & TEAM is labeled "Clinic admins" (icon: user-cog) and is active (light cyan #B8F0F2 background, bold navy text). The sidebar's clinic switcher card shows "All clinics" / "Super Admin workspace". Keep the same clean, light, rounded style.

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

ACCOUNT STATUS PILLS
- Active = mint #C4F1E1, dark green text #1E6B4F, dot
- Inactive = light gray #EEF1F4, gray text
- Waiting for approval = amber #FFF4E0, dark amber text #8A5A00, clock icon
- Clinic inactive = light red #FDE2E2, red text #B42318, pause icon
Small clinic status pills (12px) next to clinic names use the same colors: Active, Pending, Inactive.

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "CLINIC & TEAM"
- Title (32px, Nunito 800, navy): "Clinic admins"
- Subtitle (gray): "The people who run each clinic on VetCare."
- Right side: orange button "+ Add admin"

SUMMARY CARDS (4 in a row; 44px icon circle, big number 28px Nunito 800 navy, gray label)
- "Total admins" — 7 (user-cog icon, cyan circle)
- "Active" — 3 (user-check icon, mint circle)
- "Inactive" — 1 (user-x icon, light-gray circle)
- "Clinics without an active admin" — 0 (building-2 icon, light-orange circle, orange icon). When above 0, the number is orange.

WARNING CARD (only when "Clinics without an active admin" is above 0)
- Amber card with an alert icon and the title "These clinics have no active admin". Rows: clinic name, clinic status pill, and an orange small button "Add admin" (opens the Add admin panel with that clinic selected).
- When it's 0, show a slim mint bar instead: check icon + "Every active clinic has an active admin."

TOOLBAR CARD
- Search input ("Search name, username, or email"), "All clinics" dropdown, "All status" dropdown, and a toggle switch "Group by clinic" (cyan when on).

ADMINS TABLE (inside a white card)
- Columns: Admin (40px rounded-square avatar with initials on cyan, full name in bold navy, email in gray below), Username, Clinic (clinic name + small clinic status pill), Contact no., Status pill, Date created, Sync badge, actions menu.
- Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber).
- Actions menu: View, Edit, Reset password, Deactivate (red) / Activate (green). "Deactivate" is disabled with a tooltip "This is the clinic's only active admin" when that's the case.
- Row hover: #F2F8FB. Clicking a row opens the admin details panel.
- When "Group by clinic" is on: rows are grouped under light gray group headers showing the clinic initials tile, clinic name, clinic status pill, and the count "2 admins", with a small "+ Add admin" text button on the right of each header.
- Pagination: "Showing 1–7 of 7 admins".
- Empty state: user-cog icon in a cyan circle, "No clinic admins yet", orange "Add admin" button.

ADD / EDIT ADMIN PANEL (slides in from the right, 460px wide, white, soft shadow, dimmed background)
- Title "Add admin" / "Edit admin" with a close (x) icon. A small pill under the title: "Role: Administrator" (pale blue, fixed).
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  Clinic* (searchable dropdown showing clinic name + status pill; only Active and Pending clinics; locked when editing), First name*, Last name*, Username*, Email*, Contact no., Temporary password* with a small navy text button "Generate" and an eye icon to show/hide, Confirm password* (Add only), Status toggle (Active / Inactive).
- If the selected clinic is Pending, show an amber note: "This admin can log in once the clinic is approved."
- Inline red errors: "Required", "This username is already taken", "This email is already used", "Password needs at least 8 characters with a letter and a number", "Passwords don't match".
- When offline: disable "Save admin" and show an amber note "Connect to the internet to create or change admin accounts."
- Footer: gray outline "Cancel", orange "Save admin".

RESET PASSWORD PANEL (slides in from the right, 420px)
- Title "Reset password". Admin card: avatar, name, username, clinic.
- Orange button "Generate temporary password".
- After generating: a light cyan box with the password in large monospace text (e.g., "Vc7-paws-4821") and a "Copy" button, plus a gray note: "This password is shown only once. Give it to the admin privately."
- Footer: "Done".

DEACTIVATE / ACTIVATE DIALOGS
- Deactivate: "Deactivate Ben Santos? They won't be able to log in. Their clinic's vets and staff are not affected." Buttons: Cancel / red "Deactivate".
- Blocked: "Ana Reyes is the only active admin of VetCare Clinic – Matina. Add another admin first." with an orange button "Add admin" and "OK".
- Activate: "Activate Ben Santos? They can log in again once their clinic's computers sync." Buttons: Cancel / green-outlined "Activate".

ADMIN DETAILS PANEL (slides in from the right, 440px)
- 64px initials avatar, full name (22px Nunito 800 navy), status pill, "Administrator" pill.
- Rows with icons: building-2 (clinic + clinic status pill), at-sign (username), mail (email), phone (contact no.), calendar (created Nov 2, 2025), refresh-cw (sync status).
- Small gray lock note: "Super Admins can manage this account but can't sign in as this user."
- Footer: outline "Reset password", outline "Edit", red text "Deactivate".

SAMPLE DATA (for preview, today = Nov 18, 2026)
- Ana Reyes — areyes — ana.reyes@vetcare.ph — VetCare Clinic – Matina (Active) — 0917 111 2233 — Active — Nov 2, 2025 — Synced
- Ben Santos — bsantos — ben.santos@vetcare.ph — VetCare Clinic – Matina (Active) — 0917 444 5566 — Inactive — Jan 10, 2026 — Synced
- Mark Uy — muy — mark@happypaws.ph — Happy Paws Veterinary Clinic (Active) — 0917 222 3344 — Active — Feb 14, 2026 — Synced
- Joy Lim — jlim — joy@bajadapets.ph — Bajada Pet Care Center (Active) — 0918 555 1212 — Active — Jul 1, 2026 — Pending
- Leo Ramos — lramos — buhanginvet@gmail.com — Buhangin Vet Clinic (Inactive) — 0920 777 8899 — Clinic inactive — Mar 9, 2026 — Synced
- Rafael Cruz — rcruz — petlife.toril@gmail.com — PetLife Animal Clinic (Pending) — 0919 333 4455 — Waiting for approval — Nov 16, 2026 — Synced
- Carla Diaz — cdiaz — mintalvet@gmail.com — Mintal Animal Wellness (Pending) — 0918 909 1122 — Waiting for approval — Nov 17, 2026 — Synced

RESPONSIVE
- Summary cards wrap to 2 per row, side panels become full width, and the table scrolls sideways if needed.
```
