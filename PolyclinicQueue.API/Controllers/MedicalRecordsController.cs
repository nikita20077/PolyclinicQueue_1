using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using PolyclinicQueue.API.Services;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/medicalrecords")]
[Authorize]
public class MedicalRecordsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    private readonly PdfGeneratorService _pdf;
    public MedicalRecordsController(DatabaseHelper db, PdfGeneratorService pdf) { _db = db; _pdf = pdf; }

    private static MedicalRecord Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        AppointmentId = r.GetInt("AppointmentId"),
        PatientId = r.GetInt("PatientId"),
        DoctorId = r.GetInt("DoctorId"),
        Diagnosis = r.GetStr("Diagnosis"),
        Prescriptions = r.GetStringOrNull("Prescriptions"),
        Recommendations = r.GetStringOrNull("Recommendations"),
        RecordDate = r.GetDate("RecordDate"),
        DoctorName = r.GetStringOrNull("DoctorName"),
        Specialization = r.GetStringOrNull("Specialization")
    };

    // История болезни пациента (врач видит только записи врачей своей специальности)
    [HttpGet("patient/{patientId:int}")]
    public async Task<IActionResult> ByPatient(int patientId)
    {
        if (!User.CanSeePatient(patientId)) return Forbid();
        var isDoctor = User.RoleName() == "Doctor";
        if (isDoctor && !await _db.DoctorHasPatientAsync(User.ProfileId(), patientId)) return Forbid();

        var ps = new List<MySqlParameter> { P("@p", patientId) };
        var filter = "";
        if (isDoctor) { filter = " AND d.Specialization = @spec"; ps.Add(P("@spec", await _db.GetDoctorSpecializationAsync(User.ProfileId()))); }

        var list = await _db.QueryAsync(
            @"SELECT m.Id, m.AppointmentId, m.PatientId, m.DoctorId, m.Diagnosis, m.Prescriptions, m.Recommendations,
                     m.RecordDate, d.FullName AS DoctorName, d.Specialization
              FROM MedicalRecords m JOIN Doctors d ON d.Id = m.DoctorId
              WHERE m.PatientId=@p" + filter + " ORDER BY m.RecordDate DESC", Map, ps.ToArray());
        return Ok(list);
    }

    // PDF-рецепт по записи медкарты (серверная генерация)
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var rows = await _db.QueryAsync(@"
SELECT m.PatientId, m.DoctorId, m.Diagnosis, m.Prescriptions, m.Recommendations, m.RecordDate,
       p.FullName AS PatientName, p.BirthDate, d.FullName AS DoctorName, d.Specialization
FROM MedicalRecords m
JOIN Patients p ON p.Id = m.PatientId
JOIN Doctors d ON d.Id = m.DoctorId
WHERE m.Id = @id",
            r => new
            {
                PatientId = r.GetInt("PatientId"),
                DoctorId = r.GetInt("DoctorId"),
                Data = new PrescriptionData(r.GetStr("PatientName"), r.GetDateTimeOrNull("BirthDate"), r.GetStr("Diagnosis"),
                    r.GetStringOrNull("Prescriptions"), r.GetStringOrNull("Recommendations"), r.GetStr("DoctorName"),
                    r.GetStringOrNull("Specialization"), r.GetDate("RecordDate"))
            }, P("@id", id));

        if (rows.Count == 0) return NotFound(new { message = "Запись не найдена" });
        var row = rows[0];
        if (!User.CanSeeAppointment(row.PatientId, row.DoctorId)) return Forbid();

        return File(_pdf.Prescription(row.Data), "application/pdf", $"recipe-{id}.pdf");
    }

    // Врач записывает (или обновляет) диагноз, назначения и рекомендации по приёму
    [HttpPost]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Save([FromBody] MedicalRecordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Diagnosis)) return BadRequest(new { message = "Укажите диагноз" });

        var info = await _db.GetAppointmentInfoAsync(req.AppointmentId);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        if (info.Value.DoctorId != User.ProfileId()) return Forbid();

        var existing = await _db.ScalarAsync("SELECT Id FROM MedicalRecords WHERE AppointmentId=@a", P("@a", req.AppointmentId));
        if (existing != null)
        {
            await _db.ExecuteAsync(
                "UPDATE MedicalRecords SET Diagnosis=@d, Prescriptions=@p, Recommendations=@r, RecordDate=NOW() WHERE Id=@id",
                P("@d", req.Diagnosis.Trim()), P("@p", req.Prescriptions), P("@r", req.Recommendations),
                P("@id", Convert.ToInt32(existing)));
            return Ok(new { id = Convert.ToInt32(existing) });
        }

        var id = await _db.InsertAsync(
            @"INSERT INTO MedicalRecords (AppointmentId, PatientId, DoctorId, Diagnosis, Prescriptions, Recommendations, RecordDate)
              VALUES (@a, @p, @d, @di, @pr, @re, NOW())",
            P("@a", req.AppointmentId), P("@p", info.Value.PatientId), P("@d", info.Value.DoctorId),
            P("@di", req.Diagnosis.Trim()), P("@pr", req.Prescriptions), P("@re", req.Recommendations));
        return Ok(new { id });
    }
}
