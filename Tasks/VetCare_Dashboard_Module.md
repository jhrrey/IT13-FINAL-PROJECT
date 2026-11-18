# VetCare – Dashboard ("Dashboard")

**Type:** Home page for each role (summarizes all modules)
**Sidebar location:** OVERVIEW → Dashboard (first item in the sidebar)
**Used by:** Administrator, Veterinarian, Cashier/Staff (each sees their own dashboard)
**Tables:** No new tables. It only **reads** from Appointment, MedicalRecord, Prescription, Vaccination, Medicine, Bill, Payment, User, Device, SyncLog, and SyncQueue.

---

## 1. What this module does

The Dashboard is the **first page after login**. It shows each user what matters most to them **today**, so they know what to do next without opening every module.

- Each role gets a **different dashboard**, based on their job.
- Every number is **calculated live** from the other modules and never saved separately.
- Clicking a card or list item **opens the related module**, already filtered. For example, "Low stock" opens Medicines & stock filtered to low stock.

| Role | Dashboard focus |
|---|---|
| **Administrator** | How the whole clinic is doing: visits, income, stock, staff, and sync |
| **Veterinarian** | Their own patients today: who's waiting, who's next, and unfinished records |
| **Cashier/Staff** | The front desk: check-ins, guests to register, prescriptions to dispense, and bills to collect |
| Super Admin | Has a separate platform dashboard (clinics, devices, sync), not covered here |

---

## 2. Parts shared by all dashboards

### Header
- Greeting with the user's first name and the time of day: "Good morning, Maria".
- Today's date and the clinic name.
- **Sync status pill:**
  - **Online · Synced 2 min ago** (mint)
  - **Online · 5 changes pending** (amber), with a **Sync now** button
  - **Offline · Showing data on this computer** (gray), with the number of changes waiting

### Stat cards
- A row of 4–5 cards, each with an icon, a big number, a label, and sometimes a small comparison (e.g., "+3 vs yesterday").

### Quick actions
- Buttons for the most common tasks of that role.

---

## 3. Administrator dashboard

**Goal:** see the health of the clinic at a glance.

### Stat cards
| Card | Source |
|---|---|
| Appointments today (with Completed / No-show counts) | Appointment |
| Collected today (₱) | Payment |
| Collected this month (₱) | Payment |
| Outstanding balance (₱) | Bill − Payment |
| Stock alerts (low + out + expiring) | Medicine |

### Charts
- **Income – last 30 days:** line chart of daily payments (₱).
- **Appointments this week:** bar chart per day, split by status (Completed, Cancelled, No-show).
- **Visits per vet today:** small bar chart, one bar per vet.

### Lists
- **Stock alerts:** top 5 medicines that are low, out, or expiring, with their status pills. Link: "View all".
- **Recent payments:** last 5 payments with owner, method, amount, and time.
- **Vaccines overdue:** count of pets with overdue vaccines, with a link to the Vaccinations due list.
- **Devices & sync:** each clinic computer, its last sync time, and its status (Synced, Pending, Failed).

### Quick actions
- Add staff, View reports, Medicines & stock, Users & roles.

---

## 4. Veterinarian dashboard

**Goal:** know who's next and finish every record.

### Stat cards
| Card | Source |
|---|---|
| My appointments today | Appointment (VetID = me) |
| Waiting for me (Checked In) | Appointment |
| Completed today | Appointment |
| Unfinished consultations (In Consultation or draft) | Appointment + MedicalRecord |

### Next patient card (large, at the top)
- The first **Checked In** pet assigned to the vet: pet name, species · breed, owner, appointment time, reason, how long they've waited, and the last visit's diagnosis.
- A big **Start consultation** button.
- If no one is waiting, it shows the next scheduled appointment instead.

### Lists
- **Today's schedule:** a timeline of the vet's appointments (time, pet, reason, status). The current or next one is highlighted.
- **Unfinished consultations:** visits still In Consultation or saved as a draft, each with a **Continue** button.
- **My recent records:** the last 5 medical records the vet wrote.
- **Vaccine stock check:** vaccines that are low or out of stock, so the vet knows before a vaccination visit.

### Quick actions
- My schedule, Medical records, Vaccinations.

> Vets **don't see money** (income or bills) on their dashboard.

---

## 5. Cashier/Staff dashboard

**Goal:** keep the front desk moving: check in, dispense, and collect.

### Stat cards
| Card | Source |
|---|---|
| Appointments today | Appointment |
| To check in (with guests to register) | Appointment (Scheduled, today) |
| Prescriptions to dispense | Prescription (IsDispensed = 0) |
| Ready to bill | Appointment (Completed, no bill) |
| Collected today (₱) | Payment |

### Today's queue (main panel)
- A compact list of today's appointments in time order: time, pet, owner, vet, and status.
- Each row has its **next action**: Check in, Register & check in (guests), Create bill, or Receive payment.
- Guest bookings show a **Guest** pill.

### Lists
- **To dispense:** the oldest 5 prescriptions waiting, each with a **Dispense** button.
- **Ready to bill / Unpaid:** completed visits without a bill, and bills with a balance, each with a **Create bill** or **Receive payment** button.
- **Vaccine follow-ups:** the top 5 overdue or due-soon pets, with the owner's contact no. and a **Book** button.
- **Stock alerts:** medicines that are low or out of stock.

### Quick actions
- Book appointment, Walk-in, Add owner, Receive payment.

---

## 6. Rules (checked in DashboardController)

1. The dashboard shown depends on the user's **role**, which comes from `Session.RoleName`.
2. All numbers are **filtered by `Session.ClinicID`**. The vet dashboard is also filtered by **VetID = the logged-in user**.
3. The dashboard is **read-only**. Actions open the real module page or panel, and the module's own rules still apply there.
4. Numbers are **calculated when the page opens**, refreshed **every 60 seconds**, and refreshed right after each sync.
5. **Money is shown only to Administrators and Cashier/Staff.**
6. "Today" uses the computer's local date (Asia/Manila).
7. Lists show **at most 5 items**, each with a "View all" link to the full module.
8. Archived (soft-deleted) records are never counted.

---

## 7. Offline / online sync

- When offline, the dashboard shows the data **saved on this computer**, with a gray banner: "Offline — numbers may not include other computers' changes."
- The sync pill always shows how many **changes are waiting** in the SyncQueue.
- **Sync now** sends pending changes, downloads new data, and refreshes the dashboard.
- The Administrator's **Devices & sync** list warns when a computer **hasn't synced for over 24 hours** or its last sync **failed**.

---

## 8. MVC files

| Layer | File |
|---|---|
| Model | `Models/Repositories/DashboardRepository.cs` (read-only summary queries) |
| Model (used) | Appointment, Medicine, Prescription, Vaccination, Billing, and Sync repositories |
| Controller | `Controllers/DashboardController.cs` |
| View | `Views/Admin/AdminDashboardPage.xaml` |
| View | `Views/Veterinarian/VetDashboardPage.xaml` |
| View | `Views/Staff/StaffDashboardPage.xaml` |
| View | `Views/Shared/StatCardView.xaml`, `SyncStatusView.xaml` |
| Helper | `Helpers/GreetingHelper.cs`, `Helpers/CurrencyHelper.cs` |

**DashboardController methods**
- `GetAdminDashboard()`
- `GetVetDashboard()`
- `GetStaffDashboard()`
- `GetNextPatient()` (vet)
- `GetTodayQueue()` (staff)
- `GetSyncStatus()`
- `SyncNow()`

---

## 9. UI build prompt

```
Build the "Dashboard" page for the VetCare veterinary clinic management system. It is the first page after login and sits to the right of the existing VetCare sidebar. Add a new first section to the sidebar called "OVERVIEW" with the item "Dashboard" (icon: layout-dashboard), and make it active (light cyan #B8F0F2 background, bold navy text). Keep the same clean, light, rounded style.

There are 3 versions of the dashboard, one per role. For preview, add a small segmented switch at the top right of the page: "Administrator | Veterinarian | Cashier/Staff". In the real app, the version is chosen by the logged-in user's role.

DESIGN TOKENS
- Page background: #F5FAFD
- Primary navy: #0A2E4F
- Accent orange (primary buttons): #D4421C, hover #B83715
- Body text gray: #5B6B7B
- Light cyan: #B8F0F2 / #D3EEF8
- Mint: #C4F1E1, Pale blue: #DCEBFB, Light orange: #FDE8E1, Amber: #FFF4E0, Light gray: #EEF1F4, Light red: #FDE2E2
- Borders: 1px #CFE3EC
- Cards: white, 12px radius, 1px border, very soft shadow, padding 20px
- Fonts: headings "Nunito" (800), body "DM Sans" (400/500); money and numbers use tabular numbers
- Icons: Lucide outline, 20px, stroke 1.75
- Buttons: 6px radius, 44px tall
- Charts: simple and flat — navy #0A2E4F as the main color, cyan #5DCFD6 and orange #D4421C as accents, light gridlines #E6EEF2, no 3D, no gradients

SHARED HEADER (all versions)
- Left: greeting (30px Nunito 800 navy): "Good morning, Ana" (changes to afternoon/evening by time), and below it in gray: "Tuesday, Nov 17, 2026 · VetCare Clinic – Matina".
- Right: sync status pill (rounded, 36px tall):
  - Mint: cloud-check icon + "Online · Synced 2 min ago"
  - Amber: refresh-cw icon + "Online · 5 changes pending" + small navy text button "Sync now"
  - Gray: cloud-off icon + "Offline · 5 changes waiting"
- When offline, show a full-width gray banner under the header: "You're offline. Numbers only include data saved on this computer."

STAT CARD STYLE (all versions)
- White card, 44px icon circle on the left, big number (28px Nunito 800 navy), gray label under it, and an optional small comparison line ("+3 vs yesterday" in green, "−2 vs yesterday" in orange).
- The whole card is clickable (hover: border #9FD4E3) and opens the related module.

LIST CARD STYLE (all versions)
- White card with a title row: icon + title (17px bold navy) on the left, "View all →" (orange, 14px) on the right.
- Up to 5 rows, each 56px tall, divided by thin lines. Empty state: small gray text with an icon, e.g. "Nothing to dispense. Nice!"

=== VERSION 1: ADMINISTRATOR ===

STAT CARDS (5 in a row)
- "Appointments today" — 11 — small gray "8 completed · 1 no-show" (calendar-days icon, cyan circle)
- "Collected today" — ₱8,450.00 — "+12% vs yesterday" (wallet icon, mint circle)
- "Collected this month" — ₱186,320.00 (trending-up icon, pale-blue circle)
- "Outstanding balance" — ₱1,970.00 — "4 bills" (circle-dollar-sign icon, light-orange circle, orange icon)
- "Stock alerts" — 5 — "2 low · 1 out · 2 expiring" (package-x icon, amber circle)

CHARTS ROW (2 cards side by side, 60% / 40%)
- "Income – last 30 days": line chart, navy line with light cyan area fill under it, ₱ on the y-axis, dates on the x-axis, tooltip with the exact amount. Top-right of the card: total for the period "₱186,320.00".
- "Appointments this week": stacked bar chart per day (Mon–Sun): Completed = navy, Cancelled = light gray #C9D3DA, No-show = orange. Legend under the chart.

THIRD ROW (3 cards)
- "Visits per vet today": horizontal bars — Dr. Maria Santos 6, Dr. Grace Lim 5 (navy bars, number at the end).
- "Stock alerts" list: medicine name, stock number, and a status pill (Low = amber, Out = light red, Expiring = light orange, Expired = light gray). Rows: Otomax Ear Drops (0, Out), Doxycycline 100 mg (18, Low), 5-in-1 Vaccine (9, Low), Miconazole Cream 2% (Expires Dec 01, Expiring), Multivitamin Syrup (Expired).
- "Recent payments" list: owner name, method pill (Cash / GCash / Maya / Card / Bank), time in gray, amount on the right in bold. Rows: Maria Clara Santos – GCash – 8:52 AM – ₱400.00; Pedro Reyes – Cash – Nov 16 – ₱500.00; Ana Villanueva – Cash – Nov 14 – ₱475.00.

FOURTH ROW (2 cards)
- "Vaccines overdue": a big number "3 pets" in orange with a short line "Owners should be contacted." and an orange outline button "Open due list".
- "Devices & sync": rows with a monitor icon, device name, "Last sync: 2 min ago", and a status pill (Synced = mint, Pending = amber, Failed = light red, "Not synced for 2 days" = light red). Rows: Front Desk PC – Synced; Consultation Room 1 – Pending (3 changes); Consultation Room 2 – Failed (Not synced for 2 days).

QUICK ACTIONS (row of outline buttons with icons under the header): "Add staff" (user-plus), "View reports" (chart-column), "Medicines & stock" (warehouse), "Users & roles" (users).

=== VERSION 2: VETERINARIAN ===
(Header greeting: "Good morning, Dr. Santos")

STAT CARDS (4 in a row)
- "My appointments today" — 6 (calendar-days icon, cyan circle)
- "Waiting for me" — 2 (clock icon, pale-blue circle)
- "Completed today" — 2 (circle-check icon, mint circle)
- "Unfinished consultations" — 1 (file-pen icon, amber circle)

NEXT PATIENT CARD (full width, larger, light cyan #D3EEF8 background, 16px radius, padding 28px)
- Small label (uppercase, orange): "NEXT PATIENT · WAITING 12 MIN"
- Left: 72px mint circle with a dog icon. Next to it: "Bantay" (28px Nunito 800 navy), "Dog · Aspin · Male · 2 yrs 3 mos" (gray), owner "Juan Dela Cruz" with a user icon, and chips: "9:00 AM", "Vaccination".
- Middle: small gray box "Last visit: Nov 14 — Healthy; 5-in-1 Dose 2 given".
- Right: big orange button "Start consultation →" (52px tall).
- If nobody is checked in: show the next scheduled appointment with the text "Not here yet" and no button.

SECOND ROW (2 cards, 55% / 45%)
- "Today's schedule": vertical timeline. Each item: time on the left (bold navy), a dot on the line colored by status, then the pet name + species icon, reason pill, and status pill (Scheduled = pale blue, Checked In = cyan, In Consultation = amber, Completed = mint, Cancelled = gray, No-show = light orange). The current/next item has a light cyan background. Items: 8:00 Kiko – Check-up – No-show; 8:30 Choco – Check-up – Completed; 9:00 Bantay – Vaccination – Checked In; 10:30 Snow – Follow-up – Scheduled; 2:00 PM Whiskers (Guest) – Vaccination – Scheduled; 3:00 PM Mingming – Check-up – Cancelled.
- "Unfinished consultations": rows with pet name, started time, "Draft saved 9:41 AM" in gray, and an orange small button "Continue". Row: Snow – Started Nov 14 – Draft.

THIRD ROW (2 cards)
- "My recent records": rows with date, pet name, diagnosis (one line, cut off with "…"), and a "View" link. Rows: Choco – Mild tartar buildup; Snow – Upper respiratory infection, improving.
- "Vaccine stock check": rows with vaccine name, stock, and status pill: Anti-rabies Vaccine – 18 – In stock (mint); 5-in-1 (DHPPiL) Vaccine – 9 – Low (amber); 4-in-1 (FVRCP) Vaccine – 12 – In stock (mint).

QUICK ACTIONS: "My schedule" (calendar), "Medical records" (clipboard-plus), "Vaccinations" (syringe).
- Do NOT show any money, bills, or income on this version.

=== VERSION 3: CASHIER/STAFF ===
(Header greeting: "Good morning, John")

STAT CARDS (5 in a row)
- "Appointments today" — 11 (calendar-days icon, cyan circle)
- "To check in" — 4 — small gray "2 guests to register" (log-in icon, pale-blue circle)
- "Prescriptions to dispense" — 3 (pill icon, amber circle)
- "Ready to bill" — 1 (receipt icon, light-orange circle, orange icon)
- "Collected today" — ₱8,450.00 (wallet icon, mint circle)

QUICK ACTIONS (under the header, larger buttons): orange "+ Book appointment", navy outline "Walk-in" (footprints icon), outline "Add owner" (user-plus), outline "Receive payment" (wallet).

MAIN ROW (2 cards, 60% / 40%)
- "Today's queue" (left, taller): compact list in time order. Each row: time (bold navy), species icon + pet name (+ gray "Guest" pill for guest bookings), owner name in gray, vet name in gray, status pill, and one action button on the right:
  - Scheduled (registered) → navy outline "Check in"
  - Scheduled (guest) → orange outline "Register & check in"
  - Completed without bill → orange "Create bill"
  - Bill unpaid/partial → navy outline "Receive payment"
  Rows: 9:00 Bantay – Checked In; 9:30 Luna – Checked In; 10:00 Max – Checked In; 10:30 Snow – Scheduled – "Check in"; 11:00 Bruno (Guest) – Pedro Garcia – Scheduled – "Register & check in"; 1:00 PM Rocky – Scheduled – "Check in"; 2:00 PM Whiskers (Guest) – Liza Tan – Scheduled – "Register & check in"; 8:30 Choco – Completed – Paid.
- Right column (stacked cards):
  - "To dispense": rows with pet name, medicine + qty, and a small orange "Dispense" button (disabled gray "No stock" when stock is too low). Rows: Luna – Itraconazole 100 mg × 14; Luna – Miconazole Cream 2% × 1; Max – Otomax Ear Drops × 1 (No stock).
  - "Ready to bill / Unpaid": rows with owner, pet, amount, and a button. Rows: Juan Dela Cruz – Bantay – "Create bill"; Pedro Reyes – Luna – Balance ₱570.00 – "Receive payment"; Pedro Reyes – Max – Balance ₱400.00 – "Receive payment".

BOTTOM ROW (2 cards)
- "Vaccine follow-ups": rows with pet, owner + contact no. (with a copy icon), vaccine, due status pill (Overdue = light orange, Due soon = amber), and a small "Book" button. Rows: Max – Pedro Reyes – Anti-rabies – 18 days overdue; Mingming – Juan Dela Cruz – 4-in-1 (FVRCP) – in 11 days; Bantay – Juan Dela Cruz – 5-in-1 – in 18 days.
- "Stock alerts": same style as the admin version (Otomax – Out; Doxycycline – Low; 5-in-1 Vaccine – Low).

RESPONSIVE
- Stat cards wrap to 2–3 per row on smaller widths, two-column rows stack into one column, the next-patient card stacks its parts vertically, and charts keep their height but shrink in width.
```
