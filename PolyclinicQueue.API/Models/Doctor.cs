namespace PolyclinicQueue.API.Models;

public class Doctor
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string? Gender { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public string? Category { get; set; }          // квалификационная категория
    public int? ExperienceYears { get; set; }      // стаж, лет
    public string? Education { get; set; }
    public DateTime? HireDate { get; set; }        // дата приёма на работу
    public string? CabinetNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public double? Rating { get; set; }            // средний рейтинг (из Reviews)
    public int ReviewsCount { get; set; }
}
