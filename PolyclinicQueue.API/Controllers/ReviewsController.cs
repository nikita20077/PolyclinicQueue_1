using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public ReviewsController(DatabaseHelper db) => _db = db;

    private static Review Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        AppointmentId = r.GetInt("AppointmentId"),
        PatientId = r.GetInt("PatientId"),
        DoctorId = r.GetInt("DoctorId"),
        Rating = r.GetInt("Rating"),
        Comment = r.GetStringOrNull("Comment"),
        CreatedAt = r.GetDate("CreatedAt"),
        PatientName = r.GetStringOrNull("PatientName")
    };

    // Пациент оценивает врача после завершённого приёма
    [HttpPost]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Create([FromBody] ReviewRequest req)
    {
        if (req.Rating < 1 || req.Rating > 5) return BadRequest(new { message = "Оценка — от 1 до 5 звёзд" });

        var info = await _db.GetAppointmentInfoAsync(req.AppointmentId);
        if (info == null) return NotFound(new { message = "Запись не найдена" });
        if (info.Value.PatientId != User.ProfileId()) return Forbid();
        if (info.Value.Status != "Completed") return BadRequest(new { message = "Оценить можно только завершённый приём" });

        if (await _db.CountAsync("SELECT COUNT(*) FROM Reviews WHERE AppointmentId=@a", P("@a", req.AppointmentId)) > 0)
            return Conflict(new { message = "Вы уже оставили отзыв об этом приёме" });

        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        var id = await _db.InsertAsync(
            @"INSERT INTO Reviews (AppointmentId, PatientId, DoctorId, Rating, Comment, CreatedAt)
              VALUES (@a, @p, @d, @r, @c, NOW())",
            P("@a", req.AppointmentId), P("@p", info.Value.PatientId), P("@d", info.Value.DoctorId),
            P("@r", req.Rating), P("@c", comment));
        return Ok(new { id });
    }

    // Отзывы и средний рейтинг врача
    [HttpGet("doctor/{doctorId:int}")]
    public async Task<IActionResult> ByDoctor(int doctorId)
    {
        var list = await _db.QueryAsync(@"
SELECT r.Id, r.AppointmentId, r.PatientId, r.DoctorId, r.Rating, r.Comment, r.CreatedAt, p.FullName AS PatientName
FROM Reviews r JOIN Patients p ON p.Id = r.PatientId
WHERE r.DoctorId=@d ORDER BY r.CreatedAt DESC", Map, P("@d", doctorId));

        // Пациентам имена других пациентов не показываем
        if (User.RoleName() == "Patient") foreach (var r in list) r.PatientName = "Пациент";

        var avg = list.Count == 0 ? 0 : Math.Round(list.Average(x => x.Rating), 2);
        return Ok(new { doctorId, average = avg, count = list.Count, reviews = list });
    }
}
