using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public AppointmentsController(DatabaseHelper db) => _db = db;

    private static readonly string[] Statuses = { "Scheduled", "InProgress", "Completed", "Cancelled" };

    private const string Select = @"
SELECT a.Id, a.PatientId, a.DoctorId, a.AppointmentDate, a.Status, a.QueueNumber, a.CreatedAt,
       p.FullName AS PatientName, d.FullName AS DoctorName, d.Specialization, d.CabinetNumber,
       (SELECT COUNT(*) FROM Appointments x
         WHERE x.DoctorId = a.DoctorId AND DATE(x.AppointmentDate) = DATE(a.AppointmentDate)
           AND x.QueueNumber < a.QueueNumber AND x.Status IN ('Scheduled','InProgress')) AS PeopleAhead
FROM Appointments a
JOIN Patients p ON p.Id = a.PatientId
JOIN Doctors d ON d.Id = a.DoctorId";

    private static Appointment Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        PatientId = r.GetInt("PatientId"),
        DoctorId = r.GetInt("DoctorId"),
        AppointmentDate = r.GetDate("AppointmentDate"),
        Status = r.GetStr("Status"),
        QueueNumber = r.GetInt("QueueNumber"),
        CreatedAt = r.GetDate("CreatedAt"),
        PatientName = r.GetStringOrNull("PatientName"),
        DoctorName = r.GetStringOrNull("DoctorName"),
        Specialization = r.GetStringOrNull("Specialization"),
        CabinetNumber = r.GetStringOrNull("CabinetNumber"),
        PeopleAhead = r.GetInt("PeopleAhead")
    };

    // Все записи (админ) с необязательными фильтрами ?date=2026-10-07&doctorId=1&status=Scheduled
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? date, [FromQuery] int? doctorId, [FromQuery] string? status)
    {
        var where = new List<string>();
        var ps = new List<MySqlParameter>();
        if (date.HasValue) { where.Add("DATE(a.AppointmentDate) = @date"); ps.Add(P("@date", date.Value.Date)); }
        if (doctorId.HasValue) { where.Add("a.DoctorId = @doc"); ps.Add(P("@doc", doctorId.Value)); }
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("a.Status = @st"); ps.Add(P("@st", status)); }

        var sql = Select + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                         + " ORDER BY a.AppointmentDate DESC, a.QueueNumber";
        return Ok(await _db.QueryAsync(sql, Map, ps.ToArray()));
    }

    // Очередь врача на сегодня
    [HttpGet("today/{doctorId:int}")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<IActionResult> Today(int doctorId)
    {
        if (User.RoleName() == "Doctor" && User.ProfileId() != doctorId) return Forbid();
        var sql = Select + @" WHERE a.DoctorId=@d AND DATE(a.AppointmentDate)=@t AND a.Status<>'Cancelled'
                              ORDER BY a.QueueNumber";
        return Ok(await _db.QueryAsync(sql, Map, P("@d", doctorId), P("@t", DateTime.Today)));
    }

    // Предстоящие записи врача (начиная с завтрашнего дня), в том числе назначенные им самим
    [HttpGet("upcoming/{doctorId:int}")]
    [Authorize(Roles = "Admin,Doctor")]
    public async Task<IActionResult> Upcoming(int doctorId)
    {
        if (User.RoleName() == "Doctor" && User.ProfileId() != doctorId) return Forbid();
        var sql = Select + @" WHERE a.DoctorId=@d AND a.AppointmentDate>=@t AND a.Status='Scheduled'
                              ORDER BY a.AppointmentDate LIMIT 50";
        return Ok(await _db.QueryAsync(sql, Map, P("@d", doctorId), P("@t", DateTime.Today.AddDays(1))));
    }

    // Записи пациента (ожидающиеся и прошедшие)
    [HttpGet("patient/{patientId:int}")]
    public async Task<IActionResult> ByPatient(int patientId)
    {
        if (!User.CanSeePatient(patientId)) return Forbid();
        if (User.RoleName() == "Doctor" && !await _db.DoctorHasPatientAsync(User.ProfileId(), patientId)) return Forbid();
        var sql = Select + " WHERE a.PatientId=@p ORDER BY a.AppointmentDate DESC";
        return Ok(await _db.QueryAsync(sql, Map, P("@p", patientId)));
    }

    // Занятое время врача на дату: ["09:00","10:30"]
    [HttpGet("busy/{doctorId:int}")]
    public async Task<IActionResult> Busy(int doctorId, [FromQuery] DateTime date)
    {
        var list = await _db.QueryAsync(
            "SELECT AppointmentDate FROM Appointments WHERE DoctorId=@d AND DATE(AppointmentDate)=@t AND Status<>'Cancelled'",
            r => r.GetDate("AppointmentDate"), P("@d", doctorId), P("@t", date.Date));
        return Ok(list.Select(x => x.ToString("HH:mm")));
    }

    // Одна запись (для карточки «подробности приёма»)
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var a = await _db.QuerySingleAsync(Select + " WHERE a.Id=@id", Map, P("@id", id));
        if (a == null) return NotFound(new { message = "Запись не найдена" });
        if (!User.CanSeeAppointment(a.PatientId, a.DoctorId)) return Forbid();
        return Ok(a);
    }

    // Живая очередь: сколько впереди, примерное ожидание, какой номер сейчас на приёме
    [HttpGet("{id:int}/queue-status")]
    public async Task<IActionResult> QueueStatus(int id)
    {
        var a = await _db.QuerySingleAsync(Select + " WHERE a.Id=@id", Map, P("@id", id));
        if (a == null) return NotFound(new { message = "Запись не найдена" });
        if (!User.CanSeeAppointment(a.PatientId, a.DoctorId)) return Forbid();

        var current = await _db.ScalarAsync(
            "SELECT MIN(QueueNumber) FROM Appointments WHERE DoctorId=@d AND DATE(AppointmentDate)=@t AND Status='InProgress'",
            P("@d", a.DoctorId), P("@t", a.AppointmentDate.Date));
        var waiting = a.Status == "Scheduled";
        return Ok(new
        {
            appointmentId = a.Id,
            status = a.Status,
            queueNumber = a.QueueNumber,
            peopleAhead = waiting ? a.PeopleAhead : 0,
            estimatedMinutes = waiting ? a.PeopleAhead * 10 : 0,   // ~10 минут на пациента
            currentNumber = current == null ? (int?)null : Convert.ToInt32(current),
            appointmentDate = a.AppointmentDate
        });
    }

    // Создать запись: пациент записывает себя; врач записывает своего пациента на следующий приём; админ — кого угодно
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AppointmentRequest req)
    {
        var role = User.RoleName();
        int patientId, doctorId;
        if (role == "Patient") { patientId = User.ProfileId(); doctorId = req.DoctorId; }
        else if (role == "Doctor") { patientId = req.PatientId ?? 0; doctorId = User.ProfileId(); }
        else if (role == "Admin") { patientId = req.PatientId ?? 0; doctorId = req.DoctorId; }
        else return Forbid();

        if (patientId == 0) return BadRequest(new { message = "Не указан пациент" });
        if (role == "Doctor" && !await _db.DoctorHasPatientAsync(doctorId, patientId)) return Forbid();
        if (await _db.CountAsync("SELECT COUNT(*) FROM Patients WHERE Id=@p", P("@p", patientId)) == 0)
            return NotFound(new { message = "Пациент не найден" });

        var dt = new DateTime(req.AppointmentDate.Year, req.AppointmentDate.Month, req.AppointmentDate.Day,
                              req.AppointmentDate.Hour, req.AppointmentDate.Minute, 0);
        if (dt <= DateTime.Now) return BadRequest(new { message = "Выберите время в будущем" });

        if (await _db.CountAsync("SELECT COUNT(*) FROM Doctors WHERE Id=@d AND IsActive=1", P("@d", doctorId)) == 0)
            return BadRequest(new { message = "Врач не найден или не принимает" });

        if (await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DoctorId=@d AND AppointmentDate=@dt AND Status<>'Cancelled'",
                P("@d", doctorId), P("@dt", dt)) > 0)
            return Conflict(new { message = "Это время уже занято" });

        var max = await _db.ScalarAsync(
            "SELECT COALESCE(MAX(QueueNumber),0) FROM Appointments WHERE DoctorId=@d AND DATE(AppointmentDate)=@day",
            P("@d", doctorId), P("@day", dt.Date));
        var queue = Convert.ToInt32(max) + 1;

        var id = await _db.InsertAsync(
            @"INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber, CreatedAt)
              VALUES (@p, @d, @dt, 'Scheduled', @q, NOW())",
            P("@p", patientId), P("@d", doctorId), P("@dt", dt), P("@q", queue));

        return Ok(new { id, queueNumber = queue });
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusRequest req)
    {
        if (!Statuses.Contains(req.Status)) return BadRequest(new { message = "Недопустимый статус" });
        var info = await _db.GetAppointmentInfoAsync(id);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        var a = info.Value;

        var role = User.RoleName();
        var allowed = role switch
        {
            "Admin" => true,
            "Doctor" => User.ProfileId() == a.DoctorId,
            "Patient" => User.ProfileId() == a.PatientId && req.Status == "Cancelled" && a.Status == "Scheduled",
            _ => false
        };
        if (!allowed) return Forbid();

        await _db.ExecuteAsync("UPDATE Appointments SET Status=@s WHERE Id=@id", P("@s", req.Status), P("@id", id));
        return Ok(new { message = "Статус обновлён" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var info = await _db.GetAppointmentInfoAsync(id);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        var a = info.Value;

        var allowed = User.IsAdmin() ||
            (User.RoleName() == "Patient" && User.ProfileId() == a.PatientId && a.Status == "Scheduled");
        if (!allowed) return Forbid();

        await _db.InTransactionAsync(async (conn, tx) =>
        {
            await ExecTxAsync(conn, tx, "DELETE FROM Reviews WHERE AppointmentId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM MedicalRecords WHERE AppointmentId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Questionnaires WHERE AppointmentId=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Appointments WHERE Id=@id", P("@id", id));
            return 0;
        });
        return Ok(new { message = "Запись удалена" });
    }
}
