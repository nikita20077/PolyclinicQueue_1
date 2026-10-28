using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/statistics")]
[Authorize(Roles = "Admin,Doctor")]
public class StatisticsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public StatisticsController(DatabaseHelper db) => _db = db;

    // Общий дашборд администратора
    [HttpGet("dashboard")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Dashboard()
    {
        var today = DateTime.Today;
        var byStatus = await _db.QueryAsync("SELECT Status, COUNT(*) AS C FROM Appointments GROUP BY Status",
            r => (Status: r.GetStr("Status"), C: r.GetInt("C")));
        var ratings = await _db.QueryAsync(@"
SELECT d.Id, d.FullName, d.Specialization, COALESCE(AVG(r.Rating),0) AS AvgRating, COUNT(r.Id) AS Cnt
FROM Doctors d LEFT JOIN Reviews r ON r.DoctorId = d.Id
GROUP BY d.Id, d.FullName, d.Specialization
ORDER BY AvgRating DESC, Cnt DESC",
            r => new
            {
                doctorId = r.GetInt("Id"),
                fullName = r.GetStr("FullName"),
                specialization = r.GetStr("Specialization"),
                average = Math.Round(Convert.ToDouble(r["AvgRating"]), 2),
                count = r.GetInt("Cnt")
            });

        return Ok(new
        {
            totalDoctors = await _db.CountAsync("SELECT COUNT(*) FROM Doctors"),
            activeDoctors = await _db.CountAsync("SELECT COUNT(*) FROM Doctors WHERE IsActive=1"),
            totalPatients = await _db.CountAsync("SELECT COUNT(*) FROM Patients"),
            totalAppointments = await _db.CountAsync("SELECT COUNT(*) FROM Appointments"),
            todayAppointments = await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DATE(AppointmentDate)=@t", P("@t", today)),
            totalReviews = await _db.CountAsync("SELECT COUNT(*) FROM Reviews"),
            byStatus = byStatus.ToDictionary(x => x.Status, x => x.C),
            doctorRatings = ratings
        });
    }

    // Статистика врача
    [HttpGet("doctor/{id:int}")]
    public async Task<IActionResult> Doctor(int id)
    {
        if (User.RoleName() == "Doctor" && User.ProfileId() != id) return Forbid();

        var rating = (await _db.QueryAsync("SELECT COALESCE(AVG(Rating),0) AS A, COUNT(*) AS C FROM Reviews WHERE DoctorId=@d",
            r => (Avg: Convert.ToDouble(r["A"]), Cnt: r.GetInt("C")), P("@d", id)))[0];
        var byStatus = await _db.QueryAsync("SELECT Status, COUNT(*) AS C FROM Appointments WHERE DoctorId=@d GROUP BY Status",
            r => (Status: r.GetStr("Status"), C: r.GetInt("C")), P("@d", id));
        var diagnoses = await _db.QueryAsync(
            "SELECT Diagnosis, COUNT(*) AS C FROM MedicalRecords WHERE DoctorId=@d GROUP BY Diagnosis ORDER BY C DESC LIMIT 5",
            r => new { diagnosis = r.GetStr("Diagnosis"), count = r.GetInt("C") }, P("@d", id));

        return Ok(new
        {
            total = await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DoctorId=@d", P("@d", id)),
            completed = await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DoctorId=@d AND Status='Completed'", P("@d", id)),
            today = await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DoctorId=@d AND DATE(AppointmentDate)=@t AND Status<>'Cancelled'",
                P("@d", id), P("@t", DateTime.Today)),
            averageRating = Math.Round(rating.Avg, 2),
            reviewsCount = rating.Cnt,
            byStatus = byStatus.ToDictionary(x => x.Status, x => x.C),
            byDay = await ByDay(14, id),
            topDiagnoses = diagnoses
        });
    }

    // Приёмы и уникальные пациенты по дням
    [HttpGet("patients-by-day")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PatientsByDay([FromQuery] int days = 14)
    {
        days = Math.Clamp(days, 3, 90);
        return Ok(await ByDay(days, null));
    }

    // Нагрузка по специализациям
    [HttpGet("specializations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Specializations()
    {
        var rows = await _db.QueryAsync(@"
SELECT d.Specialization, COUNT(DISTINCT d.Id) AS Doctors, COUNT(a.Id) AS Appts
FROM Doctors d LEFT JOIN Appointments a ON a.DoctorId = d.Id
GROUP BY d.Specialization
ORDER BY Appts DESC",
            r => new { specialization = r.GetStr("Specialization"), doctors = r.GetInt("Doctors"), appointments = r.GetInt("Appts") });
        return Ok(rows);
    }

    private async Task<List<object>> ByDay(int days, int? doctorId)
    {
        var from = DateTime.Today.AddDays(-(days - 1));
        var sql = "SELECT DATE(AppointmentDate) AS D, COUNT(*) AS Cnt, COUNT(DISTINCT PatientId) AS Pts " +
                  "FROM Appointments WHERE AppointmentDate >= @f AND AppointmentDate < @to AND Status<>'Cancelled'" +
                  (doctorId.HasValue ? " AND DoctorId=@d" : "") + " GROUP BY DATE(AppointmentDate)";
        var ps = new List<MySqlParameter> { P("@f", from), P("@to", DateTime.Today.AddDays(1)) };
        if (doctorId.HasValue) ps.Add(P("@d", doctorId.Value));

        var rows = await _db.QueryAsync(sql, r => (Day: r.GetDate("D").Date, Cnt: r.GetInt("Cnt"), Pts: r.GetInt("Pts")), ps.ToArray());
        var map = rows.ToDictionary(x => x.Day);

        var result = new List<object>();
        for (int i = 0; i < days; i++)
        {
            var d = from.AddDays(i);
            map.TryGetValue(d, out var v);
            result.Add(new { date = d.ToString("yyyy-MM-dd"), appointments = v.Cnt, patients = v.Pts });
        }
        return result;
    }
}
