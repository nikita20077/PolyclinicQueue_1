namespace PolyclinicQueue.API.Models;

public class Review
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public int Rating { get; set; }          // 1..5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PatientName { get; set; }
}
