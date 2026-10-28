namespace PolyclinicQueue.API.Models;

public record LoginDto(string Login, string Password);
public record RegisterDto(string Login, string Password, string FullName, DateTime? BirthDate, string? Gender,
    string? PhoneNumber, string? PolicyNumber, string? Email, string? Address);

// Карточка сотрудника (врача), которую заполняет администратор
public record DoctorRequest(string? Login, string? Password, string FullName, DateTime? BirthDate, string? Gender,
    string Specialization, string? Category, int? ExperienceYears, string? Education, DateTime? HireDate,
    string? CabinetNumber, string? PhoneNumber, string? Email, string? Address, bool IsActive = true);

public record PatientRequest(string FullName, DateTime? BirthDate, string? Gender, string? PhoneNumber,
    string? PolicyNumber, string? Email, string? Address);

// Врач может записать пациента на следующий приём (тогда PatientId обязателен, DoctorId берётся из токена)
public record AppointmentRequest(int? PatientId, int DoctorId, DateTime AppointmentDate);
public record StatusRequest(string Status);
public record QuestionnaireRequest(int AppointmentId, string? Complaints, string? ChronicDiseases, string? Allergies, string? Medications);
public record MedicalRecordRequest(int AppointmentId, string Diagnosis, string? Prescriptions, string? Recommendations);
public record ReviewRequest(int AppointmentId, int Rating, string? Comment);

// История обращений: приём + запись врача + оценка
public record TimelineItem(int AppointmentId, DateTime AppointmentDate, string Status, int QueueNumber,
    string DoctorName, string? Specialization, int? RecordId, string? Diagnosis, string? Prescriptions,
    string? Recommendations, int? Rating);
