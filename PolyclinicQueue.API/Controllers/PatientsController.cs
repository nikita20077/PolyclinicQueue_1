using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public PatientsController(DatabaseHelper db) => _db = db;

    private const string Select =
        "SELECT Id, UserId, FullName, BirthDate, Gender, PhoneNumber, PolicyNumber, Email, Address, CreatedAt FROM Patients";

    private static Patient Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        UserId = r.GetInt("UserId"),
        FullName = r.GetStr("FullName"),
        BirthDate = r.GetDateTimeOrNull("BirthDate"),
        Gender = r.GetStringOrNull("Gender"),
        PhoneNumber = r.GetStringOrNull("PhoneNumber"),
        PolicyNumber = r.GetStringOrNull("PolicyNumber"),
        Email = r.GetStringOrNull("Email"),
        Address = r.GetStringOrNull("Address"),
        CreatedAt = r.GetDate("CreatedAt")
    };

    // Все пациенты — только администратору
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll() =>
        Ok(await _db.QueryAsync(Select + " ORDER BY FullName", Map));

    // Картотека врача: только пациенты, которые обращались к врачам ЕГО специальности
    // (стоматолог видит тех, кто лечил зубы в поликлинике, кардиолог — кардиологических и т.д.)
    [HttpGet("my")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> My()
    {
        var spec = await _db.GetDoctorSpecializationAsync(User.ProfileId());
        var list = await _db.QueryAsync(@"
SELECT p.Id, p.FullName, p.BirthDate, p.PhoneNumber, p.PolicyNumber,
  (SELECT COUNT(*) FROM Appointments a JOIN Doctors d ON d.Id = a.DoctorId
    WHERE a.PatientId = p.Id AND a.Status = 'Completed' AND d.Specialization = @spec) AS Visits,
  (SELECT MAX(a.AppointmentDate) FROM Appointments a JOIN Doctors d ON d.Id = a.DoctorId
    WHERE a.PatientId = p.Id AND a.Status = 'Completed' AND d.Specialization = @spec) AS LastVisit,
  (SELECT MIN(a.AppointmentDate) FROM Appointments a JOIN Doctors d ON d.Id = a.DoctorId
    WHERE a.PatientId = p.Id AND a.Status = 'Scheduled' AND a.AppointmentDate >= NOW() AND d.Specialization = @spec) AS NextVisit
FROM Patients p
WHERE EXISTS (SELECT 1 FROM Appointments a JOIN Doctors d ON d.Id = a.DoctorId
              WHERE a.PatientId = p.Id AND a.Status <> 'Cancelled' AND d.Specialization = @spec)
ORDER BY p.FullName",
            r => new
            {
                id = r.GetInt("Id"),
                fullName = r.GetStr("FullName"),
                birthDate = r.GetDateTimeOrNull("BirthDate"),
                phoneNumber = r.GetStringOrNull("PhoneNumber"),
                policyNumber = r.GetStringOrNull("PolicyNumber"),
                visits = r.GetInt("Visits"),
                lastVisit = r.GetDateTimeOrNull("LastVisit"),
                nextVisit = r.GetDateTimeOrNull("NextVisit")
            }, P("@spec", spec));
        return Ok(new { specialization = spec, patients = list });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!User.CanSeePatient(id)) return Forbid();
        if (User.RoleName() == "Doctor" && !await _db.DoctorHasPatientAsync(User.ProfileId(), id)) return Forbid();
        var p = await _db.QuerySingleAsync(Select + " WHERE Id=@id", Map, P("@id", id));
        return p == null ? NotFound(new { message = "Пациент не найден" }) : Ok(p);
    }

    // История обращений (книжка): приёмы + диагнозы + назначения + оценка.
    // Врач видит только обращения к врачам своей специальности; пациент и админ — всё.
    [HttpGet("{id:int}/timeline")]
    public async Task<IActionResult> Timeline(int id)
    {
        if (!User.CanSeePatient(id)) return Forbid();
        var isDoctor = User.RoleName() == "Doctor";
        if (isDoctor && !await _db.DoctorHasPatientAsync(User.ProfileId(), id)) return Forbid();

        var ps = new List<MySqlParameter> { P("@p", id) };
        var specFilter = "";
        if (isDoctor)
        {
            specFilter = " AND d.Specialization = @spec";
            ps.Add(P("@spec", await _db.GetDoctorSpecializationAsync(User.ProfileId())));
        }

        var items = await _db.QueryAsync(@"
SELECT a.Id AS AppointmentId, a.AppointmentDate, a.Status, a.QueueNumber,
       d.FullName AS DoctorName, d.Specialization,
       m.Id AS RecordId, m.Diagnosis, m.Prescriptions, m.Recommendations,
       (SELECT rv.Rating FROM Reviews rv WHERE rv.AppointmentId = a.Id LIMIT 1) AS Rating
FROM Appointments a
JOIN Doctors d ON d.Id = a.DoctorId
LEFT JOIN MedicalRecords m ON m.AppointmentId = a.Id
WHERE a.PatientId = @p" + specFilter + @"
ORDER BY a.AppointmentDate DESC",
            r => new TimelineItem(r.GetInt("AppointmentId"), r.GetDate("AppointmentDate"), r.GetStr("Status"),
                r.GetInt("QueueNumber"), r.GetStr("DoctorName"), r.GetStringOrNull("Specialization"),
                r.GetIntOrNull("RecordId"), r.GetStringOrNull("Diagnosis"), r.GetStringOrNull("Prescriptions"),
                r.GetStringOrNull("Recommendations"), r.GetIntOrNull("Rating")),
            ps.ToArray());
        return Ok(items);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PatientRequest req)
    {
        if (!(User.IsAdmin() || (User.RoleName() == "Patient" && User.ProfileId() == id))) return Forbid();
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "Укажите ФИО" });

        await _db.ExecuteAsync(
            @"UPDATE Patients SET FullName=@n, BirthDate=@b, Gender=@g, PhoneNumber=@ph, PolicyNumber=@pol, Email=@e, Address=@ad WHERE Id=@id",
            P("@n", req.FullName.Trim()), P("@b", req.BirthDate), P("@g", req.Gender), P("@ph", req.PhoneNumber),
            P("@pol", req.PolicyNumber), P("@e", req.Email), P("@ad", req.Address), P("@id", id));
        return Ok(new { message = "Сохранено" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.QuerySingleAsync(Select + " WHERE Id=@id", Map, P("@id", id));
        if (p == null) return NotFound(new { message = "Пациент не найден" });

        await _db.InTransactionAsync(async (conn, tx) =>
        {
            await ExecTxAsync(conn, tx, "DELETE FROM Reviews WHERE PatientId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM MedicalRecords WHERE PatientId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Questionnaires WHERE PatientId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Appointments WHERE PatientId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Patients WHERE Id=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Users WHERE Id=@u", P("@u", p.UserId));
            return 0;
        });
        return Ok(new { message = "Пациент удалён" });
    }
}
