# VetCare – Project Overview

**Project Title:** VetCare: Centralized Veterinary Clinic Management System
**Project Name:** #24 Veterinary Clinic Management System
**Subject:** IT13/L – IT Professional Track 4 (Code 5773, 10:00–12:00 PM)
**Proponent:** Jhon Rey I. Depacaquibo
**Industry:** Veterinary Medicine / Animal Health
**System Type:** Enterprise Resource Planning (ERP) / Customer Relationship Management (CRM)
**Platform:** Multi-tenant desktop application

---

## 1. Description

VetCare is a desktop system that helps veterinary clinics manage their daily work in one place. It covers owner and pet records, appointments, consultations, vaccinations, medicine stock, billing, and reports.

The system is **multi-tenant**: one system serves many clinics. Each clinic only sees its own data, and a Super Admin manages all the clinics.

The system aims to:
- Reduce errors from manual record keeping.
- Keep pet medical and vaccination history from getting lost.
- Help staff keep track of medicine stock.
- Make billing more accurate.
- Give administrators clear reports for better decisions.

---

## 2. Objectives

1. To develop a system that manages pet profiles, owner information, medical records, and vaccination history in one centralized database.
2. To provide an appointment management feature that allows clinic staff to schedule, update, and monitor veterinary consultations.
3. To implement a medicine inventory management feature that tracks available medicines, stock levels, and medicine transactions.
4. To develop a billing and payment management feature that records consultation fees, medicine costs, and payments accurately.
5. To generate reports on clinic appointments, pet medical records, medicine inventory, and financial transactions to support better decision-making.

---

## 3. Target Users

| User | Scope | Responsibilities |
|---|---|---|
| **Super Administrator** | All clinics | Approves and manages clinics, creates clinic admin accounts, and manages system settings. |
| **Administrator** | One clinic | Manages staff accounts, monitors clinic records, and generates reports. |
| **Veterinarian** | One clinic | Handles consultations, diagnoses, treatments, prescriptions, and vaccinations. |
| **Cashier/Staff** | One clinic | Registers owners and pets, manages appointments, dispenses medicine, manages stock, and processes billing. |

> **Pet Owners (Clients)** are not system users. They are customers whose records are managed by clinic staff. They receive printed receipts and medical/vaccination records.

---

## 4. Modules

### Core Modules
| # | Module | Description | Main User |
|---|---|---|---|
| 1 | **Owner Records** | Registers and updates pet owners' contact details. | Cashier/Staff |
| 2 | **Pet Profiles** | Saves each pet's details (species, breed, age, weight) linked to its owner. | Cashier/Staff |
| 3 | **Appointments** | Books, reschedules, checks in, and completes visits. Vets view their own schedule. | Staff, Veterinarian |
| 4 | **Medical Records** | Records consultations: diagnosis, treatment, notes, and the pet's full history. | Veterinarian |
| 5 | **Vaccination** | Records vaccines given and the next due date. | Veterinarian |
| 6 | **Pharmacy** | Vets prescribe medicine and staff dispense it. Tracks stock levels, low-stock alerts, and stock in/out history. | Veterinarian, Staff |
| 7 | **Billing** | Combines consultation, medicine, and vaccine charges into one bill, records payments, and prints receipts. | Cashier |

### Supporting Modules
| # | Module | Description | Main User |
|---|---|---|---|
| 8 | **Reports** | Reports on appointments, medical records, inventory, and income. | Administrator |
| 9 | **User & Clinic Management** | Login, clinic registration, clinic admin accounts, and staff accounts. | Super Admin, Administrator |

---

## 5. System Process Flow

1. **Owner contacts the clinic.** The owner calls, messages, or walks in, and the staff books the appointment and assigns a vet.
   - **New clients** are booked as **guests**: only their name, contact no., pet name, and species are saved on the appointment. No Owner or Pet record is created yet.
   - **Returning clients** are searched up, and their existing pet is picked.
2. **Owner arrives and checks in.** For a guest, the staff registers the owner and pet at this step (or matches an existing owner by contact no.). The appointment is then marked "Checked In."
3. **No-show or cancelled guests** are never added to Owner Records or Pet Profiles. Their guest details are cleared after 30 days, and only the appointment's date, vet, and status are kept for reports.
4. **Consultation.** The vet looks at the pet's history and records the diagnosis, treatment, and any prescription.
5. **Vaccination (if needed).** The vet records the vaccine and its next due date.
6. **Dispense medicine.** The staff dispenses the prescribed medicine, and the stock goes down automatically.
7. **Billing and payment.** The cashier creates the bill, records the payment, and prints the receipt. The appointment is marked "Completed."
8. **Owner receives records.** Printed receipt and medical/vaccination records are given to the owner.
9. **Reports.** The administrator generates reports from the saved data.

---

## 6. Use Case Summary

| Actor | Use Cases |
|---|---|
| Super Admin | Manage clinics (tenants), Manage clinic admin accounts, Manage system settings |
| Administrator | Manage staff accounts, Monitor clinic records, Generate reports |
| Veterinarian | View appointment schedule, View pet medical history, Record consultation, Prescribe medicine, Record vaccination |
| Cashier/Staff | Register pet owners and pets, Manage appointments, Manage medicine inventory, Dispense medicine, Process billing and payment, Print receipts and records |

**Relationships:**
- Record consultation **«include»** View pet medical history
- Prescribe medicine **«extend»** Record consultation
- Record vaccination **«extend»** Record consultation
- Process billing and payment **«include»** Print receipts and records

---

## 7. Database Design (17 Tables)

| Group | Tables |
|---|---|
| System & Multi-tenant | Clinic, Role, User |
| Owners, Pets & Appointments | Owner, Pet, Appointment |
| Medical | MedicalRecord, Prescription, Vaccination |
| Pharmacy | Medicine, InventoryTransaction |
| Billing | Bill, BillItem, Payment |
| Offline/Online Sync | Device, SyncQueue, SyncLog |

**Key relationships:**
- A Clinic has many Users, Owners, Appointments, Medicines, and Bills.
- An Owner has one or more Pets.
- A Pet has many Appointments, Medical Records, and Vaccinations.
- A Medical Record can have many Prescriptions and Vaccinations.
- A Bill has one or more Bill Items and can have many Payments.
- `User.ClinicID` is empty (NULL) for the Super Admin.
- A Clinic has many Devices. Each Device has many SyncQueue entries and SyncLogs.
- An Appointment's `PetID` is empty for **guest bookings** until check-in. The guest's details are kept in four fields on the Appointment: `GuestOwnerName`, `GuestContactNo`, `GuestPetName`, `GuestSpecies`.

**Sync design:**
- All primary and foreign keys use `UNIQUEIDENTIFIER` (GUID), so records made offline on different computers never get the same ID. (Role keeps INT since it is fixed data.)
- Every synced table has 4 sync fields: `CreatedAt`, `UpdatedAt`, `IsDeleted` (soft delete), and `SyncStatus` (Pending / Synced / Conflict).
- **Device:** registers each clinic computer allowed to sync.
- **SyncQueue:** a local list of changes (insert, update, delete) waiting to be sent to the server.
- **SyncLog:** a history of each sync: who ran it, how many records went up and down, and any errors.

---

## 8. Technology Stack

| Part | Technology | Purpose |
|---|---|---|
| Language | C# | Backend logic: authentication, scheduling, records, inventory, billing |
| UI Framework | .NET MAUI (XAML) | Desktop application screens |
| Styling | CSS | Colors, fonts, and layout of the interface |
| Database | SQL Server (managed via SSMS) | Tables, relationships, and queries |
| Data Access | Microsoft.Data.SqlClient | Parameterized SQL queries |

---

## 9. System Architecture

- **Pattern:** Model–View–Controller (MVC). See `VetCare_MVC_Structure.md` for folder layout and coding rules.
  - **Model:** Entity classes and repositories (all SQL).
  - **View:** XAML pages, grouped by user role.
  - **Controller:** Input checks, business rules, role checks, and multi-tenant filtering.
- **Database:** One central SQL Server shared by all clinics. Every clinic record carries a `ClinicID`.
- **Offline/online sync:** Each clinic computer has a local copy of the database. Work is saved locally first, so the clinic keeps running without internet. When online, the app:
  1. **Pushes** pending changes from the SyncQueue to the central server.
  2. **Pulls** changes made by other computers in the same clinic since the last sync.
  3. **Resolves conflicts** with "last update wins" (the record with the newer `UpdatedAt` is kept), and marks unclear cases as Conflict for the admin to review.
  4. Saves the result in the SyncLog.
- **Multi-tenant rule:** After login, the user's `ClinicID` is stored in the session, and every query filters by it. Users can never see another clinic's data.
- **Role-based access:** Each screen and action is limited to the roles allowed to use it.

---

## 10. UI Design Guide

| Item | Value |
|---|---|
| Primary navy (headings, text) | `#0A2E4F` |
| Accent orange (buttons, highlights) | `#D4421C` |
| Background | `#F5FAFD` |
| Light cyan (icons, highlights) | `#B8F0F2` / `#D3EEF8` |
| Dark section | `#002B4E` |
| Heading font | Nunito (Extra Bold) |
| Body font | DM Sans |
| Brand | VetCare – Veterinary Clinic (paw-with-cross logo) |

**Login screen:** Sign in (email + password) and Create account (clinic registration: clinic name, address, contact number, administrator name, email, password). New clinics are reviewed by the Super Admin before activation.

---

## 11. Scope and Limitations

**In scope**
- Multi-clinic management with separate data per clinic.
- Offline use with automatic sync to the central server when online.
- Owner, pet, appointment, medical, vaccination, pharmacy, and billing records.
- Role-based access for Super Admin, Administrator, Veterinarian, and Cashier/Staff.
- Printable receipts and pet records.
- Reports for clinic administrators.

**Limitations**
- Desktop only; there is no mobile app or public web portal.
- Pet owners do not log in. All their transactions go through clinic staff.
- Online payments, SMS/email reminders, and lab equipment integration are not included.
- Works offline, but data from other computers only appears after both computers have synced. Logging in for the first time on a new computer needs internet.

---

## 12. Expected Benefits

- **For clinics:** Organized records, fewer manual errors, and accurate billing.
- **For veterinarians:** Quick access to complete medical and vaccination histories.
- **For staff:** Easier appointment handling and real-time medicine stock tracking.
- **For administrators:** Reports that help monitor clinic performance.
- **For pet owners:** Better care and up-to-date health records for their pets.
