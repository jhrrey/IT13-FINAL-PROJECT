# VetCare – Super Admin Dashboard ("Dashboard")

**Type:** Home page for the Super Admin (summarizes the whole platform)
**Sidebar location:** OVERVIEW → Dashboard (first item)
**Used by:** Super Admin only
**Tables:** No new tables. It only **reads** from Clinic, User, Role, Device, SyncLog, and SystemSetting, plus **counts only** from Owner, Pet, Appointment, MedicalRecord, and Bill.

> The Administrator, Veterinarian, and Cashier/Staff dashboards are in **VetCare_Dashboard_Module.md**.

---

## 1. What this module does

The Super Admin Dashboard is the **first page the Super Admin sees after login**. It shows the health of the **whole VetCare platform**, meaning every clinic, account, and computer, and highlights what needs attention.

- **Platform numbers:** clinics, users, and devices.
- **Attention needed:** pending sign-ups, clinics without an admin, and computers that aren't syncing.
- **Trends:** clinic growth and sync activity.
- **Platform usage:** how much VetCare is being used, shown as **counts only**.

**Privacy rule:** the Super Admin sees **counts and statuses only**. The dashboard never shows a clinic's owners, pets, medical records, bills, or **money amounts**.

| Role | Access |
|---|---|
| Super Admin | Full view |
| All other roles | No access (they have their own dashboards) |

---

## 2. Parts of the dashboard

### A. Header
- Greeting: "Good morning, Super Admin", today's date, and "VetCare Platform".
- **Server status pill:** "Server online · Updated 11:38 AM" (mint), or "Can't reach server" (light red).
- **Refresh** button.

### B. Stat cards
| Card | What it shows | Source |
|---|---|---|
| Clinics | Total, with "3 active · 2 pending · 1 inactive" | Clinic |
| Pending approvals | Clinic sign-ups waiting for review | Clinic (Pending) |
| Users | Total accounts, with a split by role | User, Role |
| Devices | Total, with "% healthy" | Device, SyncLog |
| Failed syncs today | Failed syncs across all clinics | SyncLog |
| Conflicts today | Records marked Conflict during today's syncs | SyncLog |

### C. Attention needed (action center)
A list of problems, **most urgent first**, each with a button that opens the right module:
| Item | Button | Opens |
|---|---|---|
| Clinic sign-ups waiting for review | Review | Clinics → Pending approval |
| Active clinics without an active admin | Add admin | Clinic admins (clinic selected) |
| Failing devices (last 3 syncs failed) | View device | Devices & sync logs (device panel) |
| Stale devices (no sync in 24h+) | View device | Devices & sync logs |
| Clinics with no sync from any device in 3+ days | View clinic | Clinic details |
If everything is fine: "All clear — nothing needs your attention."

### D. Charts
- **Clinic growth (last 6 months):** bars for new clinics each month, with a line for total active clinics.
- **Sync activity (last 7 days):** stacked bars for successful vs. failed syncs per day.
- **Users by role:** a donut chart (Administrator, Veterinarian, Cashier/Staff).

### E. Lists
- **Recent sign-ups:** the last 5 clinic registrations, with status and a Review button for pending ones.
- **Clinic sync health:** the clinics with the most problems first (devices, healthy, stale, failing, last sync).
- **Recent setting changes:** the last 5 changes in System Settings (setting, old → new, who, when).

### F. Platform usage this month (counts only)
- Appointments booked, New pets registered, Medical records written, and Bills created, across all clinics.
- Each with a small comparison to last month. **No peso amounts.**

### G. Quick actions
- Review sign-ups, Add clinic, Add admin, System settings.

---

## 3. Rules (checked in DashboardController)

1. Only the **Super Admin** can open this dashboard.
2. It shows **counts and statuses only**. It never shows clinic records or money amounts.
3. Users of **Rejected** clinics and **archived** records are not counted.
4. The **Stale** and **Failing** rules use the values in System Settings (`Sync.StaleAfterHours`, `Sync.FailingAfterCount`).
5. Numbers are calculated **when the page opens** and refreshed **every 60 seconds**.
6. Every card and list item **opens the related module**, already filtered.
7. The dashboard is **read-only**. All changes happen in the other modules, with their own rules.
8. It works **online only**, because it reads from the central server. If the server can't be reached, it shows the last loaded numbers with a warning.

---

## 4. MVC files

| Layer | File |
|---|---|
| Model | `Models/Repositories/DashboardRepository.cs` (adds the Super Admin summary queries) |
| Model (used) | `ClinicRepository.cs`, `UserRepository.cs`, `DeviceRepository.cs`, `SyncLogRepository.cs`, `SettingRepository.cs` |
| Controller | `Controllers/DashboardController.cs` |
| View | `Views/SuperAdmin/SuperAdminDashboardPage.xaml` |
| View (shared) | `Views/Shared/StatCardView.xaml` |
| Helper | `Helpers/SyncHealthHelper.cs`, `Helpers/GreetingHelper.cs` |

**DashboardController methods (Super Admin)**
- `GetSuperAdminDashboard()`: all cards and lists in one call
- `GetPlatformStats()`: clinics, pending, users, devices, failed syncs, conflicts
- `GetAttentionItems()`
- `GetClinicGrowth(months)`
- `GetSyncActivity(days)`
- `GetUsersByRole()`
- `GetRecentSignups(count)`
- `GetClinicSyncHealth(count)`
- `GetRecentSettingChanges(count)`
- `GetPlatformUsage()`: counts only

---

## 5. UI build prompt

```
Build the Super Admin "Dashboard" page for the VetCare veterinary clinic management system. It's the first page after the Super Admin logs in. It sits to the right of the existing VetCare sidebar, with "Dashboard" active under OVERVIEW (light cyan #B8F0F2 background, bold navy text). The Super Admin's sidebar shows: OVERVIEW → Dashboard; CLINIC & TEAM → Clinics, Clinic admins; OFFLINE / ONLINE SYNC → Devices & sync logs; SYSTEM → System settings. The clinic switcher card shows "All clinics" / "Super Admin workspace". Keep the same clean, light, rounded style.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4, Light red: #FDE2E2
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow, padding 20px
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500); numbers use tabular numbers
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall
- Charts: flat — navy #0A2E4F main, cyan #5DCFD6 second, orange #D4421C for failures, light gridlines #E6EEF2

HEADER
- Left: greeting (30px Nunito 800 navy) "Good morning, Super Admin" (changes by time of day), and below in gray: "Wednesday, Nov 18, 2026 · VetCare Platform".
- Right: server status pill — mint with cloud-check icon "Server online · Updated 11:38 AM" (or light red with cloud-off icon "Can't reach server · Showing last loaded data"), and an outline icon button "Refresh" (refresh-cw).

QUICK ACTIONS (row of buttons under the header)
- Orange "Review sign-ups" with a white count pill "2", outline "+ Add clinic" (building-2), outline "+ Add admin" (user-cog), outline "System settings" (settings).

STAT CARDS (6 in a row, or 3 per row on smaller widths; 44px icon circle, big number 28px Nunito 800 navy, gray label, small gray detail line). Each card is clickable.
- "Clinics" — 6 — "3 active · 2 pending · 1 inactive" (building-2 icon, cyan circle)
- "Pending approvals" — 2 — "Oldest: 2 days ago" (clock icon, amber circle)
- "Users" — 23 — "7 admins · 6 vets · 10 staff" (users icon, pale-blue circle)
- "Devices" — 8 — "50% healthy" (monitor icon, mint circle)
- "Failed syncs today" — 3 (cloud-off icon, light-red circle, red icon)
- "Conflicts today" — 2 (git-merge icon, light-orange circle, orange icon)

MAIN ROW (2 cards, 45% / 55%)
- "Attention needed" card (left):
  - Title row: alert-circle icon + "Attention needed" + count pill "4".
  - Rows (56px, divided by thin lines), each with a colored icon circle, a bold navy title, a gray detail line, and a small button on the right:
    1. Amber clock — "2 clinic sign-ups waiting" — "PetLife Animal Clinic, Mintal Animal Wellness" — orange "Review"
    2. Light-red cloud-off — "Vet Room PC is failing" — "Happy Paws · last 3 syncs failed (Connection timed out)" — outline "View device"
    3. Amber clock — "Consultation Room 2 hasn't synced in 3 days" — "VetCare Clinic – Matina · 27 changes waiting" — outline "View device"
    4. Light-gray pause — "Buhangin Vet Clinic is inactive" — "2 devices blocked · since Nov 6" — outline "View clinic"
  - If nothing: mint check-circle icon, "All clear — nothing needs your attention."
- "Sync activity – last 7 days" card (right): stacked bar chart per day (Success = navy, Failed = orange), legend on top right, tooltip "Nov 17 · 412 success · 5 failed". Under the chart, 3 small stats: "Success rate 98.7%", "Avg. sync time 2.1 s", "Records synced 18,240".

SECOND ROW (3 cards)
- "Clinic growth": bars for new clinics per month (Jun–Nov 2026: 0, 1, 0, 0, 0, 2) in cyan, and a navy line for active clinics (3, 4, 4, 4, 4, 3). Small note in gray: "2 new sign-ups this month are still pending."
- "Users by role": donut chart with the total "23" in the center. Legend: Administrator 7 (navy), Veterinarian 6 (cyan), Cashier/Staff 10 (orange).
- "Platform usage this month" (counts only, no money): 4 small rows with an icon, label, number, and a comparison:
  - Appointments booked — 412 — "+6% vs Oct" (green)
  - New pets registered — 37 — "+12%" (green)
  - Medical records written — 351 — "+4%" (green)
  - Bills created — 338 — "−2%" (orange)
  - Small gray lock note at the bottom: "Counts only. Clinic records and amounts stay private."

THIRD ROW (3 cards)
- "Recent sign-ups": rows with clinic initials tile, clinic name, area in gray, date, status pill (Pending = amber, Active = mint, Rejected = light red), and a small "Review" button for pending ones. Rows: Mintal Animal Wellness – Mintal – Nov 17 – Pending; PetLife Animal Clinic – Toril – Nov 16 – Pending; Bajada Pet Care Center – Bajada – Jul 1 – Active; Buhangin Vet Clinic – Buhangin – Mar 9 – Active (now Inactive, show gray pill "Inactive"); Happy Paws Veterinary Clinic – Lanang – Feb 14 – Active. "View all →" link.
- "Clinic sync health": rows with clinic name, small counts "3 devices · 2 healthy · 1 stale", last sync, and an overall pill (All good = mint, Needs attention = amber, Problem = light red, Clinic inactive = gray). Rows: Happy Paws – Problem; VetCare Clinic – Matina – Needs attention; Bajada Pet Care Center – All good; Buhangin Vet Clinic – Clinic inactive. "View all →".
- "Recent setting changes": rows with the setting label, "old → new" (old in gray strikethrough, new in bold navy), who, and when. Rows: Session timeout 60 → 30 – Super Admin – Nov 10; Consultation fee (Happy Paws) ₱400 → ₱450 – Mark Uy – Nov 3; Keep sync logs for 60 → 90 days – Super Admin – Oct 28. "View history →".

INTERACTIONS
- Every stat card, attention row, and list row opens the related module page, already filtered (e.g., "Failed syncs today" opens Devices & sync logs → Sync log filtered to Failed, today).
- The page refreshes every 60 seconds; "Updated 11:38 AM" changes after each refresh.

RESPONSIVE
- Stat cards wrap to 3 then 2 per row, the main row stacks, the second and third rows become 1–2 cards per row, and charts keep their height but shrink in width.
```
