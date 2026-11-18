# VetCare – Devices & Sync Monitoring Module ("Devices & sync logs")

**Module:** 9 – User & Clinic Management (devices and sync, Super Admin side)
**Sidebar location:** OFFLINE / ONLINE SYNC → Devices & sync logs
**Used by:** Super Admin (all clinics); Administrator (own clinic only, view and device control)
**Tables:** Device, SyncLog, Clinic, User (SyncQueue lives on each computer)

---

## 1. What this module does

VetCare works **offline** on every clinic computer and **syncs** with the central server when online. This module lets the Super Admin **watch every computer in every clinic** and make sure the data is actually reaching the server.

- See **every registered computer** (device), which clinic it belongs to, and **when it last synced**.
- Spot **problem computers**: ones that haven't synced in a long time, or keep failing.
- Read the **sync log**: every push and pull, how many records moved, and any errors.
- **Turn off** a lost, stolen, or replaced computer so it can't log in or sync anymore.

**Privacy rule:** this module shows only **device and sync information** (times, counts, errors). It never shows the owners, pets, medical records, or bills inside the synced data.

| Role | Access |
|---|---|
| Super Admin | All devices and sync logs of **all clinics**, can deactivate or reactivate any device |
| Administrator | Devices and sync logs of **their own clinic only**, can rename, deactivate, or reactivate their clinic's devices |
| Veterinarian / Cashier/Staff | No access (they only see the sync status pill on their dashboard) |

---

## 2. Data used (from the ERD)

### Device table
| Field | Type | Notes |
|---|---|---|
| DeviceID | UNIQUEIDENTIFIER (PK) | Created when the computer is registered |
| ClinicID | UNIQUEIDENTIFIER (FK) | The clinic that owns the computer |
| DeviceName | VARCHAR(100) | e.g., "Front Desk PC" |
| MachineCode | VARCHAR(100) | A unique code for the computer |
| LastSyncAt | DATETIME2 | Last **successful** sync |
| IsActive | BIT | 0 = blocked from logging in and syncing |
| RegisteredAt | DATETIME2 | When the computer was registered |

### SyncLog table
| Field | Type | Notes |
|---|---|---|
| SyncLogID | BIGINT (PK) | Created automatically |
| DeviceID | UNIQUEIDENTIFIER (FK) | The computer that synced |
| UserID | UNIQUEIDENTIFIER (FK) | Who was logged in during the sync |
| Direction | VARCHAR(10) | **Push** (send changes) or **Pull** (download changes) |
| StartedAt / FinishedAt | DATETIME2 | How long the sync took |
| RecordsSent / RecordsReceived | INT | How many records moved |
| ConflictsFound | INT | How many records were marked Conflict |
| Status | VARCHAR(20) | Success or Failed |
| ErrorMessage | VARCHAR(MAX) | Reason for a failed sync (e.g., "Connection timed out") |

### SyncQueue (on each computer)
- Holds the changes **waiting to be sent**. The server can't read it directly, so each computer reports its **pending changes count** when it syncs.

### Device health (calculated, not saved)
| Health | Rule |
|---|---|
| **Healthy** | Active, and last successful sync was within **24 hours** |
| **Stale** | Active, but no successful sync for **more than 24 hours** |
| **Failing** | Active, and its **last 3 syncs failed** |
| **Never synced** | Registered but has never synced successfully |
| **Inactive** | Turned off (`IsActive = 0`), or its clinic is Inactive |

---

## 3. How a computer becomes a device

1. VetCare is installed on a clinic computer.
2. The **clinic's Administrator logs in on it for the first time** (internet needed).
3. The system creates a **Device** record with a name (e.g., "Consultation Room 1") and the computer's **MachineCode**.
4. The computer downloads its clinic's data. After that, the clinic's Vets and Staff can log in on it, even offline.
5. Every sync is saved in the **SyncLog**, and `LastSyncAt` is updated when a sync succeeds.

---

## 4. Screens

### A. Devices & sync page (main page, 3 tabs)
- **Summary cards:** Total devices, Healthy, Stale (no sync in 24h+), Failing, Failed syncs today, Conflicts today.
- **Chart:** syncs per day for the last 7 days (Success vs. Failed).
- **Tab 1 – Devices:**
  - Toolbar: search (device name, machine code), Clinic filter, Health filter.
  - Table columns: Device, Clinic, Machine code, Registered, Last sync, Pending changes, Health, Active, Actions.
  - Actions: View details, Deactivate / Reactivate.
- **Tab 2 – Sync log:**
  - Filters: date range, Clinic, Device, Direction, Status.
  - Table columns: Date & time, Clinic, Device, User, Direction, Records sent, Records received, Conflicts, Duration, Status, Error.
- **Tab 3 – Clinic sync health:**
  - One row per clinic: number of devices, healthy, stale, failing, failed syncs (last 7 days), conflicts (last 7 days), and last sync.
  - Clicking a clinic filters the Devices tab to that clinic.

### B. Device details panel
- Device name, clinic, machine code, registered date, health, and active status.
- **Last sync**, **pending changes**, and the **success rate** for the last 7 days.
- The device's **last 10 syncs** (time, direction, records, status, error).
- Buttons: **Deactivate** / **Reactivate**.

### C. Deactivate device dialog
- Shows the device, its clinic, and its **pending changes**.
- Two choices for the reason:
  - **Replaced / no longer used:** a warning appears if it still has pending changes ("Sync this computer first so no data is lost").
  - **Lost or stolen:** the device is blocked right away, and any changes it hasn't synced are **refused**.

---

## 5. Rules (checked in SyncMonitorController)

1. The **Super Admin** sees all clinics. An **Administrator** sees only devices and logs where `ClinicID = Session.ClinicID`.
2. This module is **read-only** for clinic data. The only change it makes is turning a device **on or off** (and renaming, for Administrators).
3. A **deactivated device** can't log in, and its next sync is **refused**.
4. Deactivating a **lost or stolen** device needs a confirmation, and its unsynced changes are **not accepted**.
5. A device can only be **reactivated** if its clinic is **Active**.
6. If a clinic is **deactivated**, all its devices are treated as **Inactive** until the clinic is activated again.
7. Device health uses the **24-hour** and **3 failed syncs** rules above.
8. **Conflict details stay private.** The Super Admin only sees **conflict counts**, and the clinic's own staff fix the conflicts in their modules.
9. **Error messages must not contain patient or billing data.** They only describe the technical problem.
10. Sync logs are kept for **90 days**. Older logs are removed, keeping a daily total per device.
11. This module works **online only**, because it reads data from the central server.

> **Optional ERD additions:**
> - `PendingChanges INT` and `LastSyncAttemptAt DATETIME2` on Device, to save the pending count and the last try (even when it failed).
> - `DeactivatedReason VARCHAR(50)` on Device (e.g., Replaced, Lost/Stolen).
> - An `AppVersion VARCHAR(20)` on Device, to spot computers running an old version of VetCare.

---

## 6. Offline / online sync (how it connects)

- Each clinic computer **pushes** its pending changes, then **pulls** new changes, and each step is saved in the **SyncLog**.
- The **dashboard sync pill** on every clinic computer reads from the same data (last sync, pending changes).
- The Super Admin's **Dashboard** and the **Clinic details → Devices / Sync log** tabs show summaries of this module.
- **Recommended safety rule:** a computer only allows **offline login** if it synced within the last **7 days**.

---

## 7. MVC files

| Layer | File |
|---|---|
| Model | `Models/Entities/Device.cs`, `SyncLog.cs` |
| Model | `Models/Repositories/DeviceRepository.cs` |
| Model | `Models/Repositories/SyncLogRepository.cs` |
| Controller | `Controllers/SyncMonitorController.cs` |
| View | `Views/SuperAdmin/DevicesSyncPage.xaml` (all clinics) |
| View | `Views/Admin/ClinicDevicesPage.xaml` (own clinic, same layout) |
| View | `Views/Shared/DeviceDetailsView.xaml`, `DeactivateDeviceDialog.xaml` |
| Helper | `Helpers/SyncHealthHelper.cs` (Healthy, Stale, Failing, Never synced) |

**SyncMonitorController methods**
- `GetSyncSummary()`: devices, healthy, stale, failing, failed today, conflicts today
- `GetSyncChart(days)`
- `GetDevices(search, clinicId, health)`
- `GetDeviceById(deviceId)`
- `GetDeviceSyncHistory(deviceId, count)`
- `GetSyncLogs(dateFrom, dateTo, clinicId, deviceId, direction, status)`
- `GetClinicSyncHealth()`
- `DeactivateDevice(deviceId, reason)`
- `ReactivateDevice(deviceId)`
- `RenameDevice(deviceId, newName)` (Administrator)
- `CleanupOldLogs()` (runs daily; keeps 90 days)

---

## 8. UI build prompt

```
Build the "Devices & sync logs" page for the VetCare veterinary clinic management system, Super Admin version (all clinics). It sits to the right of the existing VetCare sidebar, with "Devices & sync logs" active under OFFLINE / ONLINE SYNC (light cyan #B8F0F2 background, bold navy text). The sidebar's clinic switcher card shows "All clinics" / "Super Admin workspace". Keep the same clean, light, rounded style.
(The Administrator version uses the same page, but without the Clinic column, the Clinic filter, and the "Clinic sync health" tab.)

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4, Light red: #FDE2E2
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500); machine codes use a monospace font
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall
- Charts: flat — success = navy #0A2E4F, failed = orange #D4421C, light gridlines #E6EEF2

DEVICE HEALTH PILLS (use everywhere)
- Healthy = mint #C4F1E1, dark green text #1E6B4F, cloud-check icon
- Stale = amber #FFF4E0, dark amber text #8A5A00, clock icon
- Failing = light red #FDE2E2, red text #B42318, cloud-off icon
- Never synced = pale blue #DCEBFB, navy text, circle-dashed icon
- Inactive = light gray #EEF1F4, gray text, ban icon
SYNC STATUS PILLS: Success = mint, Failed = light red. DIRECTION PILLS: Push = pale blue (upload icon), Pull = cyan (download icon).

PAGE HEADER
- Eyebrow (13px, uppercase, orange): "OFFLINE / ONLINE SYNC"
- Title (32px, Nunito 800, navy): "Devices & sync logs"
- Subtitle (gray): "Make sure every clinic computer is saving to the cloud."
- Right side: small gray text "Updated 11:13 AM" and an outline icon button "Refresh" (refresh-cw).

SUMMARY CARDS (6 in a row, or 3 per row on smaller widths; 44px icon circle, big number 28px Nunito 800 navy, gray label)
- "Total devices" — 8 (monitor icon, cyan circle)
- "Healthy" — 4 (cloud-check icon, mint circle)
- "Stale (24h+)" — 1 (clock icon, amber circle)
- "Failing" — 1 (cloud-off icon, light-red circle, red icon)
- "Failed syncs today" — 3 (alert-triangle icon, light-orange circle, orange icon)
- "Conflicts today" — 2 (git-merge icon, pale-blue circle)
Clicking a health card filters the Devices tab.

CHART CARD (full width, 220px tall)
- Title "Syncs – last 7 days". Stacked bar chart per day: Success (navy) and Failed (orange). Legend on the top right. Tooltip: "Nov 17 · 412 success · 5 failed".

TABS
- "Devices" (active: orange text + 2px orange underline), "Sync log", "Clinic sync health".

TAB 1 – DEVICES
- Toolbar: search ("Search device name or machine code"), "All clinics" dropdown, "All health" dropdown.
- Table inside a white card:
  - Columns: Device (36px rounded-square tile with a monitor icon on cyan, device name in bold navy), Clinic (name), Machine code (monospace gray), Registered (date), Last sync ("2 min ago"; amber when over 24 hours, red when over 3 days, "Never" in blue), Pending changes (number; amber pill when above 0), Health pill, Active (toggle switch, cyan when on), actions menu (View details, Deactivate / Reactivate).
  - Row hover: #F2F8FB. Clicking a row opens the device details panel.
  - Rows sorted by problems first: Failing, Stale, Never synced, then Healthy, then Inactive.
- Empty state: monitor icon in a cyan circle, "No devices yet", subtext "Devices appear when a clinic admin logs in on a new computer."

TAB 2 – SYNC LOG
- Toolbar: date range picker, "All clinics", "All devices", "All directions" (Push, Pull), "All status" (Success, Failed).
- Table columns: Date & time, Clinic, Device, User, Direction pill, Sent, Received, Conflicts (orange number when above 0), Duration ("3.2 s"), Status pill, Error (red-gray text, cut off with "…", full text in a tooltip).
- Failed rows have a faint light-red background #FFF6F6.
- Pagination: "Showing 1–20 of 2,418 logs". Note under the table: "Logs older than 90 days are removed automatically."

TAB 3 – CLINIC SYNC HEALTH
- Table: Clinic (initials tile + name + clinic status pill), Devices, Healthy, Stale, Failing, Failed syncs (7 days), Conflicts (7 days), Last sync, and an overall pill: "All good" (mint), "Needs attention" (amber), "Problem" (light red), "Clinic inactive" (gray).
- Clicking a row switches to the Devices tab filtered by that clinic.

DEVICE DETAILS PANEL (slides in from the right, 480px wide, white, soft shadow, dimmed background)
- Header: 56px monitor icon tile, device name (22px Nunito 800 navy), health pill, and an "Active" toggle.
- Info rows with icons: building-2 (clinic), hash (machine code, monospace), calendar (registered), cloud (last successful sync), upload (pending changes), activity (success rate last 7 days, e.g., "97%").
- "Recent syncs" list (last 10): time, direction pill, "↑ 12 / ↓ 48", conflicts, status pill, and the error text in red-gray under failed rows.
- Small gray lock note: "Only sync details are shown. Clinic records stay private."
- Footer: red outline "Deactivate device" (or green outline "Reactivate device").

DEACTIVATE DEVICE DIALOG (centered modal, 480px)
- Title: "Deactivate Vet Room PC?"
- Reason as 2 radio cards:
  - "Replaced or no longer used" — gray subtext "It will stop syncing after this."
  - "Lost or stolen" — gray subtext "Block it now. Unsynced changes will be refused."
- If the device has pending changes and "Replaced" is chosen: amber box "This computer still has 4 changes that haven't synced. Sync it first so no data is lost."
- If "Lost or stolen" is chosen: light-red box "Any of its 4 unsynced changes will be lost."
- Buttons: Cancel / red "Deactivate".
- Reactivate dialog: "Reactivate Vet Room PC? It can log in and sync again." Buttons: Cancel / green-outlined "Reactivate". Blocked if the clinic is inactive: "Activate Buhangin Vet Clinic first."

SAMPLE DATA (for preview, today = Nov 18, 2026, 11:13 AM)
Devices:
- Front Desk PC — VetCare Clinic – Matina — VC-FD-01 — Nov 2, 2025 — 2 min ago — 0 — Healthy — Active
- Consultation Room 1 — VetCare Clinic – Matina — VC-CR-01 — Nov 2, 2025 — 10 min ago — 3 — Healthy — Active
- Consultation Room 2 — VetCare Clinic – Matina — VC-CR-02 — Jan 15, 2026 — 3 days ago — 27 — Stale — Active
- Reception PC — Happy Paws Veterinary Clinic — HP-RC-01 — Feb 14, 2026 — 15 min ago — 0 — Healthy — Active
- Vet Room PC — Happy Paws Veterinary Clinic — HP-VR-01 — Feb 20, 2026 — 6 hours ago — 4 — Failing (last 3 syncs: "Connection timed out") — Active
- Front Desk PC — Bajada Pet Care Center — BJ-FD-01 — Jul 1, 2026 — 1 hour ago — 0 — Healthy — Active
- Front Desk PC — Buhangin Vet Clinic — BH-FD-01 — Mar 9, 2026 — 12 days ago — 0 — Inactive (clinic inactive)
- Consultation PC — Buhangin Vet Clinic — BH-CR-01 — Mar 9, 2026 — 12 days ago — 0 — Inactive (clinic inactive)
Sync log (latest first):
- 11:11 AM — Matina — Front Desk PC — John Dela Cruz — Push — 6 / 0 — 0 — 1.4 s — Success
- 11:11 AM — Matina — Front Desk PC — John Dela Cruz — Pull — 0 / 14 — 0 — 2.1 s — Success
- 11:03 AM — Matina — Consultation Room 1 — Dr. Maria Santos — Push — 9 / 0 — 1 — 1.8 s — Success
- 10:58 AM — Happy Paws — Vet Room PC — Dr. Paolo Tan — Push — 0 / 0 — 0 — 30.0 s — Failed — "Connection timed out"
- 10:45 AM — Happy Paws — Reception PC — Liza Go — Pull — 0 / 22 — 0 — 2.6 s — Success
- 10:12 AM — Bajada — Front Desk PC — Joy Lim — Push — 4 / 0 — 1 — 1.2 s — Success
Clinic sync health:
- VetCare Clinic – Matina — 3 devices — 2 healthy — 1 stale — 0 failing — 1 failed — 2 conflicts — 2 min ago — Needs attention
- Happy Paws Veterinary Clinic — 2 — 1 — 0 — 1 — 9 failed — 0 — 15 min ago — Problem
- Bajada Pet Care Center — 1 — 1 — 0 — 0 — 0 — 1 — 1 hour ago — All good
- Buhangin Vet Clinic — 2 — 0 — 0 — 0 — 0 — 0 — 12 days ago — Clinic inactive

RESPONSIVE
- Summary cards wrap to 3 then 2 per row, the side panel becomes full width, and tables scroll sideways if needed.
```
