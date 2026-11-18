namespace VetCare.Models;

/// <summary>A prescription row of the consultation form (raw text; the controller validates it).</summary>
public sealed record PrescriptionRow(int PrescriptionId, int MedicineId, string Quantity, string Dosage, string Instructions, bool IsDispensed)
{
    public bool IsEmpty => MedicineId == 0 && string.IsNullOrWhiteSpace(Quantity) && string.IsNullOrWhiteSpace(Dosage) && string.IsNullOrWhiteSpace(Instructions);
}

/// <summary>A vaccination row of the consultation form.</summary>
public sealed record VaccinationRow(int MedicineId, string DoseNo, DateTime? NextDue)
{
    public bool IsEmpty => MedicineId == 0 && string.IsNullOrWhiteSpace(DoseNo) && NextDue == null;
}

/// <summary>What the consultation form sends to the controller.</summary>
public sealed class ConsultationFormData
{
    public const string WeightField = "weight";
    public const string TemperatureField = "temperature";
    public const string DiagnosisField = "diagnosis";
    public const string TreatmentField = "treatment";
    public const string FollowUpField = "followUp";

    /// <summary>Error key of prescription row <paramref name="index"/>.</summary>
    public static string PrescriptionField(int index) => "rx" + index;

    /// <summary>Error key of vaccination row <paramref name="index"/>.</summary>
    public static string VaccinationField(int index) => "vx" + index;

    public string Weight { get; set; }
    public string Temperature { get; set; }
    public string Diagnosis { get; set; }
    public string Treatment { get; set; }
    public string Notes { get; set; }
    public IReadOnlyList<PrescriptionRow> Prescriptions { get; set; } = Array.Empty<PrescriptionRow>();
    public IReadOnlyList<VaccinationRow> Vaccinations { get; set; } = Array.Empty<VaccinationRow>();

    /// <summary>"Book a follow-up visit" is ticked.</summary>
    public bool FollowUp { get; set; }

    public DateTime? FollowUpDate { get; set; }

    /// <summary>Book the follow-up with the same vet (otherwise the first vet with a free slot).</summary>
    public bool SameVet { get; set; } = true;
}
