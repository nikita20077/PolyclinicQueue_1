using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/questionnaires")]
[Authorize]
public class QuestionnairesController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public QuestionnairesController(DatabaseHelper db) => _db = db;

    private const string Select =
        "SELECT Id, PatientId, AppointmentId, Complaints, ChronicDiseases, Allergies, Medications, FilledAt FROM Questionnaires";

    private static Questionnaire Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        PatientId = r.GetInt("PatientId"),
        AppointmentId = r.GetInt("AppointmentId"),
        Complaints = r.GetStringOrNull("Complaints"),
        ChronicDiseases = r.GetStringOrNull("ChronicDiseases"),
        Allergies = r.GetStringOrNull("Allergies"),
        Medications = r.GetStringOrNull("Medications"),
        FilledAt = r.GetDate("FilledAt")
    };

    // Анкета по записи
    [HttpGet("{appointmentId:int}")]
    public async Task<IActionResult> GetByAppointment(int appointmentId)
    {
        var info = await _db.GetAppointmentInfoAsync(appointmentId);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        if (!User.CanSeeAppointment(info.Value.PatientId, info.Value.DoctorId)) return Forbid();

        var q = await _db.QuerySingleAsync(Select + " WHERE AppointmentId=@a", Map, P("@a", appointmentId));
        return q == null ? NotFound(new { message = "Анкета не заполнена" }) : Ok(q);
    }

    [HttpPost]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Create([FromBody] QuestionnaireRequest req)
    {
        var info = await _db.GetAppointmentInfoAsync(req.AppointmentId);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        if (info.Value.PatientId != User.ProfileId()) return Forbid();

        if (await _db.CountAsync("SELECT COUNT(*) FROM Questionnaires WHERE AppointmentId=@a", P("@a", req.AppointmentId)) > 0)
            return Conflict(new { message = "Анкета для этой записи уже заполнена — отредактируйте её" });

        var id = await _db.InsertAsync(
            @"INSERT INTO Questionnaires (PatientId, AppointmentId, Complaints, ChronicDiseases, Allergies, Medications, FilledAt)
              VALUES (@p, @a, @c, @ch, @al, @m, NOW())",
            P("@p", info.Value.PatientId), P("@a", req.AppointmentId), P("@c", req.Complaints),
            P("@ch", req.ChronicDiseases), P("@al", req.Allergies), P("@m", req.Medications));
        return Ok(new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Update(int id, [FromBody] QuestionnaireRequest req)
    {
        var q = await _db.QuerySingleAsync(Select + " WHERE Id=@id", Map, P("@id", id));
        if (q == null) return NotFound(new { message = "Анкета не найдена" });
        if (q.PatientId != User.ProfileId()) return Forbid();

        await _db.ExecuteAsync(
            @"UPDATE Questionnaires SET Complaints=@c, ChronicDiseases=@ch, Allergies=@al, Medications=@m, FilledAt=NOW() WHERE Id=@id",
            P("@c", req.Complaints), P("@ch", req.ChronicDiseases), P("@al", req.Allergies),
            P("@m", req.Medications), P("@id", id));
        return Ok(new { message = "Сохранено" });
    }
}
