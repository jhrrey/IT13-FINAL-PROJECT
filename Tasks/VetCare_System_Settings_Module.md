# VetCare – System Settings Module ("System settings")

**Module:** 9 – User & Clinic Management (system settings, Super Admin side)
**Sidebar location:** SYSTEM → System settings (Super Admin only)
**Used by:** Super Admin (all settings); Administrator (only the clinic settings they're allowed to change)
**Tables:** **SystemSetting (new)**, linked to Clinic and User

---

## 1. What this module does

System Settings is where the **Super Admin controls how VetCare works for every clinic**, without changing any code. Many rules in the other modules depend on numbers saved here, like the password length, how often computers sync, the default consultation fee, and clinic hours.

- **Global settings** apply to the whole platform (e.g., security and sync rules).
- **Clinic defaults** are the starting values for every clinic (e.g., consultation fee, clinic hours). A clinic's **Administrator can override** some of them for their own clinic.
- Every change saves **who changed it and when**.

| Role | Access |
|---|---|
| Super Admin | View and change all global settings and clinic defaults |
| Administrator | Change only the clinic settings marked **"Clinic can override"**, for their own clinic (from a "Clinic settings" page) |
| Veterinarian / Cashier/Staff | No access (the settings just apply to them) |

---

## 2. Data used (new table)

### SystemSetting table (add to the ERD)
| Field | Type | Notes |
|---|---|---|
| SettingID | UNIQUEIDENTIFIER (PK) | Created automatically |
| ClinicID | UNIQUEIDENTIFIER (FK, nullable) | **NULL = global/default value.** Filled = override for one clinic |
| SettingKey | VARCHAR(100) | e.g., `Security.PasswordMinLength` |
| SettingValue | VARCHAR(255) | Saved as text, converted to the right type when used |
| DataType | VARCHAR(20) | Number, Text, Boolean, Time, Money |
| Category | VARCHAR(50) | General, Security, Sync, Registration, ClinicDefaults, Receipts, Retention |
| AllowClinicOverride | BIT | 1 = clinic Administrators can change it for their clinic |
| UpdatedBy | UNIQUEIDENTIFIER (FK → User) | Who last changed it |
| CreatedAt, UpdatedAt, IsDeleted, SyncStatus | Sync fields | Handled automatically |

- **One row per setting key** for the global value (ClinicID = NULL).
- **One extra row per clinic** only when that clinic overrides a value.
- **How a value is read:** use the clinic's override if it exists, otherwise the global value.
- Default values are added by the seed script (`02_SeedData.sql`).

---

## 3. Settings list

### A. General
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| System name | `General.SystemName` | VetCare | 3–50 characters | No |
| Support email | `General.SupportEmail` | support@vetcare.ph | Valid email | No |
| Support contact no. | `General.SupportContactNo` | 0917 000 0000 | Valid PH number | No |
| Time zone | `General.TimeZone` | Asia/Manila | Fixed for now | No |
| Currency | `General.Currency` | PHP (₱) | Fixed for now | No |

### B. Security
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Minimum password length | `Security.PasswordMinLength` | 8 | 8–32 | No |
| Require a letter and a number | `Security.PasswordRequireMix` | On | On / Off | No |
| Session timeout (minutes) | `Security.SessionTimeoutMinutes` | 30 | 5–240 | Yes |
| Failed logins before lock | `Security.MaxFailedLogins` | 5 | 3–10 | No |
| Lock time (minutes) | `Security.LockMinutes` | 15 | 5–120 | No |
| Offline login allowed for (days since last sync) | `Security.OfflineLoginMaxDays` | 7 | 1–30 | No |
| Force password change after reset | `Security.ForceChangeAfterReset` | On | On / Off | No |

### C. Sync
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Auto-sync every (minutes) | `Sync.AutoSyncMinutes` | 5 | 1–60 | Yes |
| Device is "Stale" after (hours) | `Sync.StaleAfterHours` | 24 | 1–168 | No |
| Device is "Failing" after (failed syncs in a row) | `Sync.FailingAfterCount` | 3 | 2–10 | No |
| Max devices per clinic | `Sync.MaxDevicesPerClinic` | 5 | 1–50 | No |

### D. Clinic registration
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Allow clinics to sign up | `Registration.AllowSignup` | On | On / Off | No |
| New clinics need approval | `Registration.RequireApproval` | On | On / Off | No |

### E. Clinic defaults
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Consultation fee | `Clinic.ConsultationFee` | ₱400.00 | ₱0–₱10,000 | Yes |
| Opening time | `Clinic.OpenTime` | 8:00 AM | Time | Yes |
| Closing time | `Clinic.CloseTime` | 5:00 PM | After opening time | Yes |
| Appointment slot length (minutes) | `Clinic.SlotMinutes` | 30 | 15, 20, 30, 45, 60 | Yes |
| Vaccine "Due soon" window (days) | `Clinic.VaccineDueSoonDays` | 30 | 7–90 | Yes |
| Medicine "Expiring soon" window (days) | `Clinic.ExpiringSoonDays` | 30 | 7–180 | Yes |

### F. Receipts & numbering
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Bill number prefix | `Receipts.BillNoPrefix` | VC | 2–5 letters | Yes |
| Receipt footer message | `Receipts.FooterMessage` | Thank you for trusting VetCare! | Up to 100 characters | Yes |

### G. Data retention
| Setting | Key | Default | Allowed values | Clinic override |
|---|---|---|---|---|
| Clear guest info of no-shows/cancellations after (days) | `Retention.GuestInfoDays` | 30 | 7–90 | No |
| Remove rejected clinic sign-ups after (days) | `Retention.RejectedSignupDays` | 30 | 7–90 | No |
| Keep sync logs for (days) | `Retention.SyncLogDays` | 90 | 30–365 | No |

### Where each setting is used
| Module | Settings it reads |
|---|---|
| Login & User Management | Security settings |
| Appointments | Opening/closing time, slot length, guest info retention |
| Medical Records / Vaccination | Vaccine "Due soon" window |
| Pharmacy | "Expiring soon" window |
| Billing | Consultation fee, bill number prefix, receipt footer |
| Clinic Management | Sign-up, approval, rejected sign-up retention |
| Devices & Sync | Auto-sync, stale/failing rules, max devices, log retention, offline login |

---

## 4. Screens

### A. System settings page
- **Left menu:** General, Security, Sync, Clinic registration, Clinic defaults, Receipts & numbering, Data retention, Change history.
- **Right side:** the settings of the selected category, each with:
  - A label and a short gray description.
  - The input (number, switch, time picker, text, or ₱ amount), with the allowed range shown.
  - A small **"Clinic can override"** tag where allowed.
  - **"Reset to default"** link, and "Last changed by Super Admin on Nov 10, 2026".
- **Sticky save bar** at the bottom when there are unsaved changes: "3 unsaved changes", plus **Discard** and **Save changes** buttons.

### B. Clinic overrides (inside Clinic defaults)
- For each overridable setting, a link "**2 clinics use a different value**" opens a small list of clinics and their own values (read-only for the Super Admin, with a **Remove override** option).

### C. Change history tab
- Table: Date & time, Setting, Old value, New value, Changed by, Clinic (Global or the clinic name).

### D. Confirm dialog for risky changes
- Shown when turning off **New clinics need approval**, turning off **Require a letter and a number**, raising **Offline login days** above 14, or lowering a retention period. It explains what will happen and asks the Super Admin to confirm.

---

## 5. Rules (checked in SettingController)

1. Only the **Super Admin** can change global settings and clinic defaults.
2. Administrators can only change settings with **AllowClinicOverride = 1**, and only for **their own clinic**.
3. Every value must be **within its allowed range**, e.g., the closing time must be after the opening time, and the fee can't be negative.
4. Settings **can't be deleted**. They can only be changed or reset to their default value.
5. **Reset to default** puts back the seed value. Removing a clinic override makes that clinic use the global value again.
6. Every change saves **UpdatedBy** and **UpdatedAt**, and appears in the Change history.
7. **Risky changes** (see screen D) need an extra confirmation.
8. Changing settings **needs an internet connection**, since the server stores the main values.
9. **When changes apply:**
   - Server-side settings (sign-ups, approval, retention) apply **right away**.
   - Settings used on clinic computers (security, sync, clinic defaults) apply **after each computer's next sync**.
10. Changes **don't rewrite old records**. For example, a new consultation fee is only used for **new** bills, and old bills keep their amounts.

---

## 6. Offline / online sync

- Clinic computers **download the settings** (global values + their clinic's overrides) on every sync and at login.
- The settings are kept on the computer, so the app can still follow them **offline**.
- If the settings change while a computer is offline, it keeps using the **last downloaded values** until it syncs again.

---

## 7. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/SystemSetting.cs` |
| Model | `Models/Repositories/SettingRepository.cs` |
| Controller | `Controllers/SettingController.cs` |
| View | `Views/SuperAdmin/SystemSettingsPage.xaml` |
| View | `Views/Admin/ClinicSettingsPage.xaml` (overridable settings only) |
| View | `Views/Shared/SettingChangeConfirmDialog.xaml` |
| Helper | `Helpers/SettingsCache.cs` (loads settings at login and after sync; typed getters like `GetInt("Clinic.SlotMinutes")`) |

**SettingController methods**
- `GetSettings(category)`
- `GetEffectiveValue(key, clinicId)` (override if any, else global)
- `UpdateSettings(changes)` (validates every change, then saves them together)
- `ResetToDefault(key)`
- `GetClinicOverrides(key)`
- `SetClinicOverride(key, clinicId, value)` (Administrator)
- `RemoveClinicOverride(key, clinicId)`
- `GetChangeHistory(dateFrom, dateTo, key)`

> **Note on change history:** the SystemSetting table only keeps the latest UpdatedBy and UpdatedAt. To show old and new values, add a small `SettingChangeLog` table (SettingKey, ClinicID, OldValue, NewValue, ChangedBy, ChangedAt), or leave out the Change history tab.

---

## 8. UI build prompt

```
Build the "System settings" page for the VetCare veterinary clinic management system. Only the Super Admin uses it. It sits to the right of the existing VetCare sidebar. Add a sidebar section "SYSTEM" (shown only to the Super Admin) with the item "System settings" (icon: settings), and make it active (light cyan #B8F0F2 background, bold navy text). The sidebar's clinic switcher card shows "All clinics" / "Super Admin workspace". Keep the same clean, light, rounded style.

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
- Toggle switches: 40x22px, cyan #5DCFD6 when on, gray #C9D3DA when off

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "SYSTEM"
- Title (32px, Nunito 800, navy): "System settings"
- Subtitle (gray): "Set the rules VetCare follows for every clinic."
- Right side: small gray text "Changes reach clinic computers on their next sync."

LAYOUT: two columns — left settings menu (240px), right content.

LEFT MENU (white card, padding 8px)
- Items (48px tall, 8px radius, icon + label): General (sliders-horizontal), Security (shield), Sync (refresh-cw), Clinic registration (building-2), Clinic defaults (stethoscope), Receipts & numbering (receipt), Data retention (archive), Change history (history).
- Active item: light cyan background, bold navy text. Hover: #EAF6FA.
- An amber dot next to a category that has unsaved changes.

RIGHT CONTENT
- Category title (24px Nunito 800 navy) + one-line gray description (e.g., Security: "Password and login rules for every user.").
- A white card with setting rows. Each row (padding 20px, thin divider between rows):
  - Left: setting label (16px bold navy), gray description under it (14px), and small meta text in lighter gray: "Last changed by Super Admin · Nov 10, 2026".
  - Optional tag next to the label: pale-blue pill "Clinic can override".
  - Right: the input — number input with a unit suffix ("minutes", "days", "hours") and the allowed range shown below in gray ("8–32"); or a toggle switch; or a time picker; or a ₱ amount input; or a text input.
  - Under the input: a small orange text link "Reset to default (8)" — shown only when the value is different from the default.
  - Changed but unsaved rows get a 3px amber left border.
  - Invalid values show a red border and red text: "Enter a number from 8 to 32", "Closing time must be after opening time".

CATEGORY CONTENT (rows to show)
- General: System name (text "VetCare"), Support email, Support contact no., Time zone (read-only "Asia/Manila"), Currency (read-only "PHP (₱)").
- Security: Minimum password length (8), Require a letter and a number (on), Session timeout (30 minutes, Clinic can override), Failed logins before lock (5), Lock time (15 minutes), Offline login allowed for (7 days since last sync), Force password change after reset (on).
- Sync: Auto-sync every (5 minutes, Clinic can override), Mark device as Stale after (24 hours), Mark device as Failing after (3 failed syncs), Max devices per clinic (5).
- Clinic registration: Allow clinics to sign up (on), New clinics need approval (on).
- Clinic defaults: Consultation fee (₱400.00), Opening time (8:00 AM), Closing time (5:00 PM), Appointment slot length (segmented options 15 / 20 / 30 / 45 / 60 min, 30 selected), Vaccine "Due soon" window (30 days), Medicine "Expiring soon" window (30 days). All have the "Clinic can override" tag. Under each, a small navy link: "2 clinics use a different value" that opens the overrides popover.
- Receipts & numbering: Bill number prefix ("VC", with a live preview pill "VC-2026-0001"), Receipt footer message (text, 100 characters, with a counter "31/100").
- Data retention: Clear guest info of no-shows/cancellations after (30 days), Remove rejected clinic sign-ups after (30 days), Keep sync logs for (90 days).

OVERRIDES POPOVER (small card, 360px, opens under the link)
- Title "Clinics with their own value".
- Rows: clinic name + their value + small red text link "Remove override". Example for Consultation fee: Happy Paws Veterinary Clinic — ₱450.00; Bajada Pet Care Center — ₱350.00.
- Footer note in gray: "Other clinics use the default value."

STICKY SAVE BAR (appears at the bottom of the content when there are unsaved changes)
- White bar with a top border and a soft shadow: amber dot + "3 unsaved changes" on the left; gray outline "Discard" and orange "Save changes" on the right.
- When offline: disable "Save changes" and show "Connect to the internet to save settings."
- After saving: green toast "Settings saved. Clinic computers will update on their next sync."

CONFIRM DIALOG FOR RISKY CHANGES (centered modal, 460px)
- Amber alert icon, title "Turn off clinic approval?", text: "New clinics will become active right after signing up, without your review." Buttons: Cancel / orange "Yes, turn it off".
- Other examples: "Allow offline login for 21 days? A deactivated account could keep working offline for up to 21 days." and "Keep sync logs for 30 days instead of 90? Older logs will be removed tonight."

CHANGE HISTORY TAB
- Toolbar: date range picker, "All settings" dropdown, "Global and clinics" dropdown.
- Table columns: Date & time, Setting (label + key in small monospace gray), Old value (gray, strikethrough), New value (bold navy), Changed by, Scope pill (Global = pale blue, or the clinic name in light gray).
- Sample rows:
  - Nov 10, 2026 9:15 AM — Session timeout — 60 → 30 — Super Admin — Global
  - Nov 3, 2026 2:40 PM — Consultation fee — ₱400.00 → ₱450.00 — Mark Uy — Happy Paws Veterinary Clinic
  - Oct 28, 2026 10:05 AM — Keep sync logs for — 60 → 90 — Super Admin — Global
  - Oct 15, 2026 4:20 PM — Consultation fee — ₱400.00 → ₱350.00 — Joy Lim — Bajada Pet Care Center

RESPONSIVE
- On smaller widths, the left menu turns into a horizontal scrollable tab row above the content, setting rows stack the input under the label, and the save bar stays at the bottom.
```
