/*
    VetCare – sample medicines for clinic 1, so vets can prescribe and vaccinate before the
    Medicines & stock module exists. Adds nothing if the clinic already has medicines.

    Run with:  sqlcmd -S .\SQLEXPRESS -E -d db_vetcare -b -i Database\006_sample_medicines.sql
*/
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Medicine WHERE ClinicID = 1)
BEGIN
    INSERT INTO dbo.Medicine (ClinicID, MedicineName, Category, Unit, StockQty, ReorderLevel, UnitPrice) VALUES
    (1, 'Amoxicillin 250 mg',            'Capsule',   'cap',  60, 20, 12.00),
    (1, 'Carprofen 50 mg',                'Tablet',    'tab',  24, 10, 25.00),
    (1, 'Metronidazole 250 mg',           'Tablet',    'tab',   5, 10, 10.00),
    (1, 'Ivermectin 1% injection',        'Injection', 'ml',   30,  5, 40.00),
    (1, 'Otic ear drops',                 'Topical',   'btl',  12,  4, 180.00),
    (1, 'Ketoconazole cream',             'Topical',   'tube',  8,  4, 150.00),
    (1, 'Multivitamin syrup',             'Syrup',     'ml',  500, 100, 1.50),
    (1, 'Anti-rabies vaccine',            'Vaccine',   'dose', 20,  5, 250.00),
    (1, '5-in-1 DHPPi (dog)',             'Vaccine',   'dose', 15,  5, 450.00),
    (1, '4-in-1 FVRCP (cat)',             'Vaccine',   'dose', 10,  5, 500.00);
    PRINT 'Sample medicines added for clinic 1.';
END
ELSE
    PRINT 'Clinic 1 already has medicines; nothing added.';
