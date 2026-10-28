namespace PolyclinicQueue.API.Models;

public class MedicalRecord
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Prescriptions { get; set; }
    public string? Recommendations { get; set; }
    public DateTime RecordDate { get; set; }

    // Поля из JOIN
    public string? DoctorName { get; set; }
    public string? Specialization { get; set; }
}
