# VetCare – MVC Project Structure

**Project:** VetCare: Centralized Veterinary Clinic Management System
**Type:** Multi-tenant ERP/CRM desktop application
**Stack:** C#, .NET MAUI (XAML + CSS), SQL Server (managed in SSMS)
**Pattern:** Model – View – Controller (MVC)

---

## 1. What MVC means in VetCare

| Layer | What it does | Example |
|---|---|---|
| **Model** | Holds the data and talks to the database. | `Pet.cs`, `PetRepository.cs` |
| **View** | Shows the screen and takes user input. Nothing else. | `PetsPage.xaml` |
| **Controller** | The "middle man." Checks input, applies rules, calls the Model, and sends results back to the View. | `PetController.cs` |

**Simple rule:** The View talks only to the Controller. The Controller talks to the Model. The Model talks to the database.

```
User ──► View ──► Controller ──► Model (Repository) ──► SQL Server
                     ▲                  │
                     └──── result ◄─────┘
```

---

## 2. Folder structure

```
VetCare/
├── App.xaml / App.xaml.cs
├── AppShell.xaml / AppShell.xaml.cs        # Navigation (routes per role)
├── MauiProgram.cs                           # Registers controllers & repositories (DI)
│
├── Models/
│   ├── Entities/                            # One class per database table
│   │   ├── Clinic.cs
│   │   ├── Role.cs
│   │   ├── User.cs
│   │   ├── Owner.cs
│   │   ├── Pet.cs
│   │   ├── Appointment.cs
│   │   ├── MedicalRecord.cs
│   │   ├── Vaccination.cs
│   │   ├── Prescription.cs
│   │   ├── Medicine.cs
│   │   ├── InventoryTransaction.cs
│   │   ├── Bill.cs
│   │   ├── BillItem.cs
│   │   └── Payment.cs
│   ├── Repositories/                        # All SQL lives here
│   │   ├── ClinicRepository.cs
│   │   ├── UserRepository.cs
│   │   ├── OwnerRepository.cs
│   │   ├── PetRepository.cs
│   │   ├── AppointmentRepository.cs
│   │   ├── MedicalRecordRepository.cs
│   │   ├── VaccinationRepository.cs
│   │   ├── PrescriptionRepository.cs
│   │   ├── MedicineRepository.cs
│   │   ├── InventoryRepository.cs
│   │   ├── BillingRepository.cs             # Bill + BillItem + Payment
│   │   └── ReportRepository.cs
│   └── Data/
│       └── Database.cs                      # The ONLY place with the connection string
│
├── Controllers/
│   ├── AuthController.cs                    # Login, logout, clinic registration
│   ├── ClinicController.cs                  # Super Admin: manage clinics
│   ├── UserController.cs                    # Admin accounts & staff accounts
│   ├── OwnerController.cs
│   ├── PetController.cs
│   ├── AppointmentController.cs
│   ├── MedicalRecordController.cs
│   ├── VaccinationController.cs
│   ├── PharmacyController.cs                # Medicines, prescriptions, dispensing, stock
│   ├── BillingController.cs                 # Bills, bill items, payments, receipts
│   ├── DashboardController.cs               # Role-based dashboards (read only)
│   └── ReportController.cs
│
├── Views/
│   ├── Auth/
│   │   ├── LoginPage.xaml
│   │   └── RegisterClinicPage.xaml
│   ├── SuperAdmin/
│   │   ├── ClinicsPage.xaml
│   │   ├── ClinicAdminsPage.xaml
│   │   └── SystemSettingsPage.xaml
│   ├── Admin/
│   │   ├── AdminDashboardPage.xaml
│   │   ├── StaffAccountsPage.xaml
│   │   └── ReportsPage.xaml
│   ├── Veterinarian/
│   │   ├── VetDashboardPage.xaml
│   │   ├── VetSchedulePage.xaml
│   │   ├── ConsultationPage.xaml
│   │   └── VaccinationPage.xaml
│   ├── Staff/
│   │   ├── StaffDashboardPage.xaml
│   │   ├── OwnersPage.xaml
│   │   ├── PetsPage.xaml
│   │   ├── AppointmentsPage.xaml
│   │   ├── InventoryPage.xaml
│   │   └── BillingPage.xaml
│   └── Shared/                              # Reusable parts (sidebar, header, dialogs)
│       ├── SidebarView.xaml
│       └── HeaderView.xaml
│
├── Helpers/
│   ├── Session.cs                           # Logged-in user, role, ClinicID
│   ├── Result.cs                            # Standard success/error return
│   ├── PasswordHasher.cs
│   └── Validator.cs                         # Reusable input checks
│
├── Resources/
│   ├── Styles/
│   │   └── app.css                          # All styling (colors, fonts, buttons)
│   ├── Images/                              # Logo, icons
│   └── Fonts/
│
└── Database/
    └── Scripts/
        ├── 01_CreateTables.sql
        └── 02_SeedData.sql                  # Roles + first Super Admin
```

> Each `.xaml` file has a matching `.xaml.cs` (code-behind) file.

---

## 3. Rules for each layer

### Model
- **Entities** are plain classes with properties only. They have no logic and no SQL.
- **Repositories** hold **all** SQL queries. Use parameterized queries only (`@param`). Never join strings together to build SQL.
- Every query on clinic data **must filter by `ClinicID`** (multi-tenant rule).
- Repositories don't know about screens, pop-ups, or roles.

### View
- Shows data and collects input. That's it.
- Code-behind (`.xaml.cs`) only handles UI events. A button click calls a Controller, then shows the result.
- **No SQL, no business rules, no direct Repository calls** inside a View.
- All styling goes in `app.css`. Don't put colors and sizes in every page.

### Controller
- Checks input (required fields, valid email, positive quantities).
- Applies business rules (for example, "can't dispense if stock is too low").
- Checks the user's role before doing an action.
- Adds `Session.ClinicID` to every request, so the View never passes a ClinicID itself.
- Returns a `Result` object. **Never** shows pop-ups or touches UI controls.

---

## 4. Multi-tenant and role rules

1. When someone logs in, `AuthController` saves `UserID`, `RoleName`, and `ClinicID` in `Session`.
2. The **Super Admin** has `ClinicID = null` and can see all clinics. Everyone else only sees their own clinic.
3. Controllers always use `Session.ClinicID`. A user can never pick another clinic.
4. Each Controller action checks the role first:

| Role | Allowed Controllers |
|---|---|
| Super Admin | Clinic, User (clinic admins), Auth |
| Administrator | User (staff), Report, Auth |
| Veterinarian | Appointment (view), MedicalRecord, Vaccination, Pharmacy (prescribe) |
| Cashier/Staff | Owner, Pet, Appointment, Pharmacy (dispense, inventory), Billing |

---

## 5. Sample code

### Helpers/Result.cs
```csharp
namespace VetCare.Helpers;

public class Result
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";

    public static Result Ok(string msg = "Saved successfully.") => new() { Success = true, Message = msg };
    public static Result Fail(string msg) => new() { Success = false, Message = msg };
}
```

### Helpers/Session.cs
```csharp
namespace VetCare.Helpers;

public static class Session
{
    public static int UserID { get; set; }
    public static int? ClinicID { get; set; }      // null = Super Admin
    public static string RoleName { get; set; } = "";
    public static bool IsLoggedIn => UserID > 0;

    public static void Clear()
    {
        UserID = 0; ClinicID = null; RoleName = "";
    }
}
```

### Models/Data/Database.cs
```csharp
using Microsoft.Data.SqlClient;

namespace VetCare.Models.Data;

public static class Database
{
    // Central SQL Server shared by all clinics
    private const string ConnectionString =
        "Server=YOUR_SERVER;Database=VetCareDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;";

    public static SqlConnection GetConnection() => new SqlConnection(ConnectionString);
}
```

### Models/Entities/Pet.cs
```csharp
namespace VetCare.Models.Entities;

public class Pet
{
    public int PetID { get; set; }
    public int OwnerID { get; set; }
    public string PetName { get; set; } = "";
    public string Species { get; set; } = "";
    public string Breed { get; set; } = "";
    public string Sex { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string Color { get; set; } = "";
    public decimal? Weight { get; set; }
}
```

### Models/Repositories/PetRepository.cs
```csharp
using Microsoft.Data.SqlClient;
using VetCare.Models.Data;
using VetCare.Models.Entities;

namespace VetCare.Models.Repositories;

public class PetRepository
{
    public void Add(Pet pet)
    {
        using var conn = Database.GetConnection();
        conn.Open();
        var sql = @"INSERT INTO Pet (OwnerID, PetName, Species, Breed, Sex, BirthDate, Color, Weight)
                    VALUES (@OwnerID, @PetName, @Species, @Breed, @Sex, @BirthDate, @Color, @Weight)";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OwnerID", pet.OwnerID);
        cmd.Parameters.AddWithValue("@PetName", pet.PetName);
        cmd.Parameters.AddWithValue("@Species", pet.Species);
        cmd.Parameters.AddWithValue("@Breed", pet.Breed);
        cmd.Parameters.AddWithValue("@Sex", pet.Sex);
        cmd.Parameters.AddWithValue("@BirthDate", (object?)pet.BirthDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Color", pet.Color);
        cmd.Parameters.AddWithValue("@Weight", (object?)pet.Weight ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // Multi-tenant: only returns pets whose owner belongs to this clinic
    public List<Pet> GetByOwner(int ownerId, int clinicId)
    {
        var pets = new List<Pet>();
        using var conn = Database.GetConnection();
        conn.Open();
        var sql = @"SELECT p.* FROM Pet p
                    INNER JOIN Owner o ON o.OwnerID = p.OwnerID
                    WHERE p.OwnerID = @OwnerID AND o.ClinicID = @ClinicID";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OwnerID", ownerId);
        cmd.Parameters.AddWithValue("@ClinicID", clinicId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            pets.Add(new Pet
            {
                PetID = (int)reader["PetID"],
                OwnerID = (int)reader["OwnerID"],
                PetName = reader["PetName"].ToString()!,
                Species = reader["Species"].ToString()!,
                Breed = reader["Breed"].ToString()!
            });
        }
        return pets;
    }

    public bool OwnerBelongsToClinic(int ownerId, int clinicId)
    {
        using var conn = Database.GetConnection();
        conn.Open();
        using var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM Owner WHERE OwnerID = @OwnerID AND ClinicID = @ClinicID", conn);
        cmd.Parameters.AddWithValue("@OwnerID", ownerId);
        cmd.Parameters.AddWithValue("@ClinicID", clinicId);
        return (int)cmd.ExecuteScalar() > 0;
    }
}
```

### Controllers/PetController.cs
```csharp
using VetCare.Helpers;
using VetCare.Models.Entities;
using VetCare.Models.Repositories;

namespace VetCare.Controllers;

public class PetController
{
    private readonly PetRepository _petRepo;

    public PetController(PetRepository petRepo) => _petRepo = petRepo;

    public Result AddPet(Pet pet)
    {
        // 1. Role check
        if (Session.RoleName != "Cashier/Staff")
            return Result.Fail("You are not allowed to register pets.");

        // 2. Input validation
        if (string.IsNullOrWhiteSpace(pet.PetName)) return Result.Fail("Pet name is required.");
        if (string.IsNullOrWhiteSpace(pet.Species)) return Result.Fail("Species is required.");

        // 3. Multi-tenant check
        if (!_petRepo.OwnerBelongsToClinic(pet.OwnerID, Session.ClinicID!.Value))
            return Result.Fail("Owner not found in your clinic.");

        // 4. Save
        _petRepo.Add(pet);
        return Result.Ok("Pet registered successfully.");
    }

    public List<Pet> GetPetsOfOwner(int ownerId) =>
        _petRepo.GetByOwner(ownerId, Session.ClinicID!.Value);
}
```

### Views/Staff/PetsPage.xaml.cs (code-behind)
```csharp
using VetCare.Controllers;
using VetCare.Models.Entities;

namespace VetCare.Views.Staff;

public partial class PetsPage : ContentPage
{
    private readonly PetController _controller;

    public PetsPage(PetController controller)
    {
        InitializeComponent();
        _controller = controller;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var pet = new Pet
        {
            OwnerID = (int)OwnerPicker.SelectedItem,
            PetName = PetNameEntry.Text,
            Species = SpeciesEntry.Text,
            Breed = BreedEntry.Text
        };

        var result = _controller.AddPet(pet);               // View → Controller only
        await DisplayAlert(result.Success ? "Success" : "Error", result.Message, "OK");
    }
}
```

### MauiProgram.cs (registering the layers)
```csharp
// Models
builder.Services.AddSingleton<PetRepository>();
// Controllers
builder.Services.AddTransient<PetController>();
// Views
builder.Services.AddTransient<PetsPage>();
// ...repeat for each module
```

---

## 6. Example flow: Dispensing medicine

1. **View** (`InventoryPage`): Staff picks a prescription and clicks **Dispense**.
2. **Controller** (`PharmacyController.Dispense(prescriptionId)`):
   - Checks the role is Cashier/Staff.
   - Checks the prescription belongs to `Session.ClinicID` and isn't dispensed yet.
   - Checks there's enough stock.
3. **Model** (`PrescriptionRepository`, `MedicineRepository`, `InventoryRepository`), in **one SQL transaction**:
   - Sets `Prescription.IsDispensed = 1`.
   - Subtracts the quantity from `Medicine.StockQuantity`.
   - Inserts an `InventoryTransaction` (type = "Stock Out").
4. **Controller** returns `Result.Ok(...)`, or `Result.Fail("Not enough stock.")`.
5. **View** shows the message and refreshes the list.

---

## 7. Module-to-file map

| Module | Model (Entity / Repository) | Controller | View(s) |
|---|---|---|---|
| Owner Records | Owner / OwnerRepository | OwnerController | Staff/OwnersPage |
| Pet Profiles | Pet / PetRepository | PetController | Staff/PetsPage |
| Appointments | Appointment / AppointmentRepository | AppointmentController | Staff/AppointmentsPage, Veterinarian/VetSchedulePage |
| Medical Records | MedicalRecord / MedicalRecordRepository | MedicalRecordController | Veterinarian/ConsultationPage |
| Vaccination | Vaccination / VaccinationRepository | VaccinationController | Veterinarian/VaccinationPage |
| Pharmacy | Medicine, Prescription, InventoryTransaction / their Repositories | PharmacyController | Staff/InventoryPage |
| Billing | Bill, BillItem, Payment / BillingRepository | BillingController | Staff/BillingPage |
| Reports | — / ReportRepository | ReportController | Admin/ReportsPage |
| Dashboard | — / DashboardRepository | DashboardController | Admin/AdminDashboardPage, Veterinarian/VetDashboardPage, Staff/StaffDashboardPage |
| Accounts & Clinics | Clinic, User, Role / ClinicRepository, UserRepository | AuthController, ClinicController, UserController | Auth/*, SuperAdmin/*, Admin/StaffAccountsPage |

---

## 8. Naming conventions

| Item | Format | Example |
|---|---|---|
| Entity | Singular, PascalCase | `Appointment` |
| Repository | `<Entity>Repository` | `AppointmentRepository` |
| Controller | `<Module>Controller` | `BillingController` |
| View | `<Name>Page` | `BillingPage.xaml` |
| Methods | Verb + noun | `AddPet()`, `GetPetsOfOwner()` |
| Private fields | `_camelCase` | `_petRepo` |
| SQL parameters | `@ColumnName` | `@ClinicID` |

---

## 9. Quick checklist before committing code

- [ ] No SQL outside `Models/Repositories`.
- [ ] No `DisplayAlert` or UI code inside Controllers.
- [ ] Views only call Controllers, never Repositories.
- [ ] Every clinic-data query filters by `ClinicID`.
- [ ] Every Controller action checks the user's role.
- [ ] All SQL uses parameters (`@param`).
- [ ] Passwords are hashed, never stored as plain text.
- [ ] Multi-step saves (dispense, payment) use a SQL transaction.
- [ ] Styling stays in `Resources/Styles/app.css`.
