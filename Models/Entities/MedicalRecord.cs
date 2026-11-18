using VetCare.Helpers;

namespace VetCare.Models.Entities;

/// <summary>
/// A row of the <c>MedicalRecord</c> table: what the vet wrote down during one visit.
/// Exactly one record per appointment; records are never deleted.
/// </summary>
public sealed class MedicalRecord
{
    public int RecordId { get; set; }
    public int AppointmentId { get; set; }
    public int PetId { get; set; }

    /// <summary>The vet who wrote the record.</summary>
    public int VetId { get; set; }

    /// <summary>In kg.</summary>
    public decimal? Weight { get; set; }

    /// <summary>In °C.</summary>
    public decimal? Temperature { get; set; }

    public string Diagnosis { get; set; }
    public string Treatment { get; set; }
    public string Notes { get; set; }
    public DateTime RecordDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>One of <see cref="SyncStatuses"/>.</summary>
    public string SyncStatus { get; set; }

    // From joined rows (not MedicalRecord columns) ----------------------------------------

    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public string PetSex { get; set; }
    public DateTime? PetBirthDate { get; set; }
    public int OwnerId { get; set; }
    public string OwnerFirstName { get; set; }
    public string OwnerLastName { get; set; }
    public string OwnerContactNo { get; set; }
    public string VetFirstName { get; set; }
    public string VetLastName { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan AppointmentTime { get; set; }
    public string Reason { get; set; }
    public string AppointmentStatus { get; set; }
    public int PrescriptionCount { get; set; }
    public int VaccinationCount { get; set; }

    // Convenience ---------------------------------------------------------------------

    public string OwnerName => PersonName.Join(OwnerFirstName, OwnerLastName);
    public string OwnerContactDisplay => Validator.FormatPhMobile(OwnerContactNo);
    public string VetName => "Dr. " + PersonName.Join(VetFirstName, VetLastName);
    public string SpeciesAndBreed => string.IsNullOrWhiteSpace(Breed) ? Species : Species + " · " + Breed;
    public string AgeText => PetBirthDate == null ? null : AgeCalculator.Describe(PetBirthDate.Value, DateTime.Today);
    public string VisitText => AppointmentDate.ToString("MMM d, yyyy") + " · " + TimeSlotHelper.Format(AppointmentTime);
    public string WeightText => Weight == null ? null : Weight.Value.ToString("0.##") + " kg";
    public string TemperatureText => Temperature == null ? null : Temperature.Value.ToString("0.0") + " °C";

    /// <summary>The visit is still going on: the record is a draft.</summary>
    public bool IsDraft => AppointmentStatus == AppointmentStatuses.InConsultation;

    /// <summary>Rule 11: editable only by its vet, on the day it was written.</summary>
    public bool IsEditableBy(int userId, DateTime today) => VetId == userId && RecordDate.Date == today.Date;
}

/// <summary>A medicine prescribed in a record (a row of <c>Prescription</c>).</summary>
public sealed class RecordPrescription
{
    public int PrescriptionId { get; set; }
    public int RecordId { get; set; }
    public int MedicineId { get; set; }
    public string MedicineName { get; set; }
    public string Unit { get; set; }
    public int StockQty { get; set; }
    public int Quantity { get; set; }
    public string Dosage { get; set; }
    public string Instructions { get; set; }
    public bool IsDispensed { get; set; }

    /// <summary>Rule 8: saved, but can't be dispensed until stock is added.</summary>
    public bool ExceedsStock => !IsDispensed && Quantity > StockQty;
}

/// <summary>A vaccine given (a row of <c>Vaccination</c>).</summary>
public sealed class RecordVaccination
{
    public int VaccinationId { get; set; }
    public int PetId { get; set; }
    public int? RecordId { get; set; }
    public int MedicineId { get; set; }
    public string VaccineName { get; set; }
    public int DoseNo { get; set; }
    public DateTime DateGiven { get; set; }
    public DateTime? NextDueDate { get; set; }
    public int GivenBy { get; set; }
    public string GivenByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string SyncStatus { get; set; }

    // From joined rows (lists and the vaccination card) ----------------------------------------
    public string PetName { get; set; }
    public string Species { get; set; }
    public string Breed { get; set; }
    public int OwnerId { get; set; }
    public string OwnerFirstName { get; set; }
    public string OwnerLastName { get; set; }
    public string OwnerContactNo { get; set; }

    public string OwnerName => PersonName.Join(OwnerFirstName, OwnerLastName);
    public string OwnerContactDisplay => Validator.FormatPhMobile(OwnerContactNo);
    public string SpeciesAndBreed => string.IsNullOrWhiteSpace(Breed) ? Species : Species + " · " + Breed;

    /// <summary>"Dose 2", or "Booster" from the 4th dose on.</summary>
    public string DoseText => DoseNo >= 4 ? "Booster" : "Dose " + DoseNo;

    /// <summary>Rule 10: editable only by the vet who gave it, on the day it was recorded.</summary>
    public bool IsEditableBy(int userId, DateTime today) => GivenBy == userId && CreatedAt.Date == today.Date;
}

/// <summary>A correction added after a record was locked (a row of <c>MedicalRecordNote</c>).</summary>
public sealed class RecordNote
{
    public int NoteId { get; set; }
    public int RecordId { get; set; }
    public int VetId { get; set; }
    public string VetName { get; set; }
    public string NoteText { get; set; }
    public DateTime CreatedAt { get; set; }
}
