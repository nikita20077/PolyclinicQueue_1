namespace PolyclinicQueue.API.Models;

public class Appointment
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public int DoctorId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = "Scheduled";  // Scheduled / InProgress / Completed / Cancelled
    public int QueueNumber { get; set; }
    public DateTime CreatedAt { get; set; }

    // Поля из JOIN (в таблице отсутствуют, заполняются в запросах)
    public string? PatientName { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialization { get; set; }
    public string? CabinetNumber { get; set; }
    public int PeopleAhead { get; set; }   // сколько человек впереди в очереди
}
