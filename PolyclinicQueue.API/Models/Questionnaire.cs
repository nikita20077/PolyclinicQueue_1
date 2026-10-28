namespace PolyclinicQueue.API.Models;

public class Questionnaire
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public int AppointmentId { get; set; }
    public string? Complaints { get; set; }
    public string? ChronicDiseases { get; set; }
    public string? Allergies { get; set; }
    public string? Medications { get; set; }
    public DateTime FilledAt { get; set; }
}
