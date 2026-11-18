# VetCare – User Management Module ("Users & roles")

**Part of:** Module 9 – User & Clinic Management
**Sidebar location:** CLINIC & TEAM → Users & roles
**Used by:** Super Admin, Administrator
**Tables:** User, Role (plus Clinic for the clinic name)

---

## 1. What this module does

User Management is where system accounts are created and controlled. It decides **who can log in** and **what they are allowed to do**.

Who manages whom:

| Logged in as | Can manage | Scope |
|---|---|---|
| **Super Admin** | Administrator accounts only | All clinics |
| **Administrator** | Veterinarian and Cashier/Staff accounts | Own clinic only |
| Veterinarian / Cashier/Staff | No access (can only edit their own profile and password) | — |

> Pet owners are **not** users. They are managed in the Owners module.

---

## 2. Data used (from the ERD)

### User table
| Field | Type | Notes |
|---|---|---|
| UserID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK) | NULL for Super Admin; set from the session for Admins |
| RoleID | INT (FK) | Links to Role |
| FirstName / LastName | VARCHAR(50) | Required |
| Username | VARCHAR(50) | Required, unique across the whole system |
| PasswordHash | VARCHAR(255) | Never stores the real password |
| Email | VARCHAR(100) | Required, unique |
| ContactNo | VARCHAR(20) | Optional |
| Status | VARCHAR(20) | Active / Inactive |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

### Role table (fixed data, read-only)
| RoleID | RoleName |
|---|---|
| 1 | Super Admin |
| 2 | Administrator |
| 3 | Veterinarian |
| 4 | Cashier/Staff |

---

## 3. Screens

### A. Users tab (main page)
- **Summary cards:** Total users, Active, Inactive, and a count per role.
- **Toolbar:** search (name, username, email), Role filter, Status filter, **Clinic filter (Super Admin only)**, and an **Add user** button.
- **Users table columns:** Avatar (initials), Name + email, Username, Role, Clinic (Super Admin only), Contact no., Status, Sync status, Last updated, Actions.
- **Row actions:** View, Edit, Reset password, Deactivate / Activate.

### B. Add / Edit user form (side panel)
- First name, Last name, Username, Email, Contact no.
- Role dropdown:
  - Super Admin sees only **Administrator**, plus a **Clinic** dropdown.
  - Administrator sees only **Veterinarian** and **Cashier/Staff**. The clinic is set automatically.
- Temporary password + Confirm password (Add only).
- Status toggle (Active / Inactive).

### C. User details
- Profile info, role, clinic, status, created/updated dates, and sync status.
- Buttons: Edit, Reset password, Deactivate / Activate.

### D. Roles & permissions tab (read-only)
- One card per role with a short description.
- A permission table showing which modules each role can use.

| Module | Super Admin | Administrator | Veterinarian | Cashier/Staff |
|---|---|---|---|---|
| Clinics | Full | — | — | — |
| Users & roles | Admins only | Vets & staff | — | — |
| Owners | — | View | — | Full |
| Pets | — | View | View | Full |
| Appointments | — | View | Own schedule | Full |
| Medical records | — | View | Full | View & print |
| Vaccinations | — | View | Full | View & print |
| Medicines & stock | — | View | View | Full |
| Prescriptions | — | View | Create | Dispense |
| Bills & payments | — | View | — | Full |
| Reports | — | Full | — | — |
| Devices & sync logs | All clinics | Own clinic | — | — |

---

## 4. Rules (checked in UserController)

1. Only Super Admin and Administrator can open this module.
2. Admins can only see and manage users in **their own clinic** (filtered by `Session.ClinicID`).
3. Super Admin can only create **Administrator** accounts. Admins can only create **Veterinarian** and **Cashier/Staff** accounts.
4. **Username and email must be unique** across the whole system.
5. Passwords need at least 8 characters, with at least 1 letter and 1 number. They are saved as a hash (e.g., BCrypt), never as plain text.
6. Users are **deactivated, not deleted**, so their name stays on old appointments, records, and bills.
7. A user **cannot deactivate themselves** or change their own role.
8. A clinic must always have **at least one active Administrator**.
9. Inactive users can't log in.
10. **Creating users and resetting passwords need an internet connection.** The server must check that the username is unique and send the new account to every clinic computer.

---

## 5. Offline / online sync

- User accounts are **downloaded** to each clinic computer, so staff can still log in while offline.
- New or edited users are saved with SyncStatus = Pending and sent to the server on the next sync.
- If a user is deactivated, that change reaches the other clinic computers on their next sync. After that, the user can't log in on them.

---

## 6. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/User.cs`, `Models/Entities/Role.cs` |
| Model | `Models/Repositories/UserRepository.cs` |
| Controller | `Controllers/UserController.cs` |
| View | `Views/Admin/StaffAccountsPage.xaml` (Admin) |
| View | `Views/SuperAdmin/ClinicAdminsPage.xaml` (Super Admin) |
| View | `Views/Shared/UserFormView.xaml` (shared add/edit form) |
| Helper | `Helpers/PasswordHasher.cs`, `Helpers/Validator.cs` |

**UserController methods**
- `GetUsers(search, roleId, status, clinicId)`
- `GetUserById(userId)`
- `AddUser(user, password)`
- `UpdateUser(user)`
- `ResetPassword(userId, newPassword)`
- `SetStatus(userId, isActive)`
- `GetRoles()`

---

## 7. UI build prompt

```
Build the "Users & roles" page for the VetCare veterinary clinic management system. It sits to the right of the existing VetCare sidebar, with "Users & roles" active in the sidebar (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style as the sidebar.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500)
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "CLINIC & TEAM"
- Title (32px, Nunito 800, navy): "Users & roles"
- Subtitle (gray): "Manage who can access VetCare and what they can do."
- Right side: orange button "+ Add user"

TABS (under the header)
- "Users" (active: orange text, 2px orange bottom border) and "Roles & permissions" (gray).

USERS TAB
1. Summary cards (4 in a row): "Total users", "Active", "Inactive", "Veterinarians". Each card has a 44px icon circle (users / user-check / user-x / stethoscope, with cyan, mint, pale-blue, and cyan backgrounds), a big number (28px navy, Nunito 800), and a gray label.
2. Toolbar card: search input with a search icon (placeholder "Search name, username, or email"), "All roles" dropdown, "All status" dropdown, and "All clinics" dropdown (shown only to Super Admin).
3. Users table inside a white card:
   - Columns: User (40px rounded-square avatar with initials on a cyan background, full name in bold navy, email below in gray), Username, Role, Clinic (Super Admin only), Contact no., Status, Sync, Last updated, and an actions menu (three-dot icon).
   - Role badges (pill, 12px, bold): Administrator = navy text on pale blue; Veterinarian = teal text on mint; Cashier/Staff = orange text on light orange #FDE8E1; Super Admin = purple text on light purple.
   - Status pill: Active = green dot + "Active"; Inactive = gray dot + "Inactive".
   - Sync badge: "Synced" (check icon, green), "Pending" (clock icon, amber), "Conflict" (alert icon, red).
   - Row hover: background #F2F8FB.
   - Actions menu: View, Edit, Reset password, Deactivate (red text) / Activate.
   - Pagination at the bottom: "Showing 1–10 of 24 users" with Prev/Next buttons.
   - Empty state: users icon in a cyan circle, "No users yet", and an orange "Add user" button.

ADD / EDIT USER PANEL (slides in from the right, 440px wide, white, with a soft shadow and a dimmed background)
- Title: "Add user" or "Edit user", with a close (x) icon.
- Fields (labels bold navy 15px; inputs 48px tall, background #F2F8FB, 1px #CFE3EC border, 6px radius):
  First name, Last name, Username, Email, Contact no., Role (dropdown), Clinic (dropdown, Super Admin only), Temporary password, Confirm password (Add only), and a Status toggle (Active/Inactive).
- Role dropdown options depend on who is logged in:
  - Super Admin: only "Administrator"
  - Administrator: only "Veterinarian" and "Cashier/Staff"
- Inline red error text under any invalid field (required, invalid email, username taken, passwords don't match, password under 8 characters or missing a letter/number).
- Footer: gray outline "Cancel" button and orange "Save user" button.
- When offline: disable "Save user" and show a small amber note: "Connect to the internet to create or reset accounts."

CONFIRM DIALOGS
- Deactivate: "Deactivate Maria Santos? They won't be able to log in, but their records will stay." Buttons: Cancel / red "Deactivate".
- Reset password: field for the new temporary password + "Reset password" orange button.

ROLES & PERMISSIONS TAB (read-only)
- 4 role cards in a row: Super Admin, Administrator, Veterinarian, Cashier/Staff. Each has an icon circle, the role name, a one-line description, and the number of users with that role.
- Below: a permission table. Rows are the modules (Clinics, Users & roles, Owners, Pets, Appointments, Medical records, Vaccinations, Medicines & stock, Prescriptions, Bills & payments, Reports, Devices & sync logs). Columns are the 4 roles. Cells show small pills: "Full" (mint), "View" (pale blue), "Own" (cyan), or a gray dash for no access.
- Note above the table: "Roles are fixed by the system and can't be edited."

SAMPLE DATA (for preview)
- Maria Santos, msantos, Veterinarian, Active, Synced
- John Dela Cruz, jdelacruz, Cashier/Staff, Active, Pending
- Ana Reyes, areyes, Administrator, Active, Synced
- Mark Villanueva, mvillanueva, Cashier/Staff, Inactive, Synced
- Grace Lim, glim, Veterinarian, Active, Conflict

RESPONSIVE
- Summary cards wrap to 2 per row on smaller widths, and the table scrolls sideways if needed.
```
