using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/doctors")]
[Authorize]
public class DoctorsController : ControllerBase
{
    private readonly DatabaseHelper _db;
    public DoctorsController(DatabaseHelper db) => _db = db;

    private const string Select = @"
SELECT Id, UserId, FullName, BirthDate, Gender, Specialization, Category, ExperienceYears, Education, HireDate,
       CabinetNumber, PhoneNumber, Email, Address, IsActive, CreatedAt,
       (SELECT AVG(rv.Rating) FROM Reviews rv WHERE rv.DoctorId = Doctors.Id) AS Rating,
       (SELECT COUNT(*) FROM Reviews rv WHERE rv.DoctorId = Doctors.Id) AS ReviewsCount
FROM Doctors";

    private static Doctor Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        UserId = r.GetInt("UserId"),
        FullName = r.GetStr("FullName"),
        BirthDate = r.GetDateTimeOrNull("BirthDate"),
        Gender = r.GetStringOrNull("Gender"),
        Specialization = r.GetStr("Specialization"),
        Category = r.GetStringOrNull("Category"),
        ExperienceYears = r.GetIntOrNull("ExperienceYears"),
        Education = r.GetStringOrNull("Education"),
        HireDate = r.GetDateTimeOrNull("HireDate"),
        CabinetNumber = r.GetStringOrNull("CabinetNumber"),
        PhoneNumber = r.GetStringOrNull("PhoneNumber"),
        Email = r.GetStringOrNull("Email"),
        Address = r.GetStringOrNull("Address"),
        IsActive = r.GetBool("IsActive"),
        CreatedAt = r.GetDate("CreatedAt"),
        Rating = r.GetDoubleOrNull("Rating") is double v ? Math.Round(v, 2) : null,
        ReviewsCount = r.GetInt("ReviewsCount")
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Validate(DoctorRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.FullName)) return "Укажите ФИО сотрудника";
        if (string.IsNullOrWhiteSpace(r.Specialization)) return "Укажите специализацию";
        if (r.BirthDate.HasValue && r.BirthDate.Value.Date > DateTime.Today.AddYears(-18)) return "Сотруднику должно быть не меньше 18 лет";
        if (r.HireDate.HasValue && r.HireDate.Value.Date > DateTime.Today.AddYears(1)) return "Проверьте дату приёма на работу";
        if (r.ExperienceYears is < 0 or > 70) return "Стаж — от 0 до 70 лет";
        if (!string.IsNullOrWhiteSpace(r.Email) && !Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return "Некорректный email";
        return null;
    }

    // Админ видит всех врачей, остальные — только работающих
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var sql = Select + (User.IsAdmin() ? "" : " WHERE IsActive=1") + " ORDER BY FullName";
        return Ok(await _db.QueryAsync(sql, Map));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var d = await _db.QuerySingleAsync(Select + " WHERE Id=@id", Map, P("@id", id));
        return d == null ? NotFound(new { message = "Врач не найден" }) : Ok(d);
    }

    // Средний рейтинг врача
    [HttpGet("{id:int}/rating")]
    public async Task<IActionResult> Rating(int id)
    {
        var rows = await _db.QueryAsync(
            "SELECT COALESCE(AVG(Rating),0) AS Avg1, COUNT(*) AS Cnt FROM Reviews WHERE DoctorId=@d",
            r => (Avg: Convert.ToDouble(r["Avg1"]), Cnt: r.GetInt("Cnt")), P("@d", id));
        return Ok(new { doctorId = id, average = Math.Round(rows[0].Avg, 2), count = rows[0].Cnt });
    }

    // Принять нового сотрудника: создаёт учётную запись (логин/пароль) и карточку врача
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] DoctorRequest req)
    {
        var error = Validate(req);
        if (error != null) return BadRequest(new { message = error });

        var login = req.Login?.Trim() ?? "";
        if (login.Length < 3) return BadRequest(new { message = "Логин — минимум 3 символа" });
        if ((req.Password ?? "").Length < 4) return BadRequest(new { message = "Пароль — минимум 4 символа" });
        if (await _db.CountAsync("SELECT COUNT(*) FROM Users WHERE Login=@l", P("@l", login)) > 0)
            return Conflict(new { message = "Такой логин уже занят" });

        var id = await _db.InTransactionAsync(async (conn, tx) =>
        {
            var uid = await ExecTxAsync(conn, tx,
                "INSERT INTO Users (Login, Password, Role, CreatedAt) VALUES (@l, @p, 'Doctor', NOW())",
                P("@l", login), P("@p", req.Password));
            return await ExecTxAsync(conn, tx,
                @"INSERT INTO Doctors (UserId, FullName, BirthDate, Gender, Specialization, Category, ExperienceYears, Education,
                                       HireDate, CabinetNumber, PhoneNumber, Email, Address, IsActive, CreatedAt)
                  VALUES (@u, @n, @b, @g, @s, @cat, @exp, @edu, @hire, @c, @ph, @em, @ad, @a, NOW())",
                P("@u", uid), P("@n", req.FullName.Trim()), P("@b", req.BirthDate), P("@g", Clean(req.Gender)),
                P("@s", req.Specialization.Trim()), P("@cat", Clean(req.Category)), P("@exp", req.ExperienceYears),
                P("@edu", Clean(req.Education)), P("@hire", req.HireDate), P("@c", Clean(req.CabinetNumber)),
                P("@ph", Clean(req.PhoneNumber)), P("@em", Clean(req.Email)), P("@ad", Clean(req.Address)), P("@a", req.IsActive));
        });
        return Ok(new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] DoctorRequest req)
    {
        var error = Validate(req);
        if (error != null) return BadRequest(new { message = error });
        if (await _db.CountAsync("SELECT COUNT(*) FROM Doctors WHERE Id=@id", P("@id", id)) == 0)
            return NotFound(new { message = "Врач не найден" });

        await _db.ExecuteAsync(
            @"UPDATE Doctors SET FullName=@n, BirthDate=@b, Gender=@g, Specialization=@s, Category=@cat, ExperienceYears=@exp,
                                 Education=@edu, HireDate=@hire, CabinetNumber=@c, PhoneNumber=@ph, Email=@em, Address=@ad, IsActive=@a
              WHERE Id=@id",
            P("@n", req.FullName.Trim()), P("@b", req.BirthDate), P("@g", Clean(req.Gender)),
            P("@s", req.Specialization.Trim()), P("@cat", Clean(req.Category)), P("@exp", req.ExperienceYears),
            P("@edu", Clean(req.Education)), P("@hire", req.HireDate), P("@c", Clean(req.CabinetNumber)),
            P("@ph", Clean(req.PhoneNumber)), P("@em", Clean(req.Email)), P("@ad", Clean(req.Address)),
            P("@a", req.IsActive), P("@id", id));

        if (!string.IsNullOrEmpty(req.Password))
            await _db.ExecuteAsync(
                "UPDATE Users u JOIN Doctors d ON d.UserId = u.Id SET u.Password=@p WHERE d.Id=@id",
                P("@p", req.Password), P("@id", id));

        return Ok(new { message = "Сохранено" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _db.QuerySingleAsync(Select + " WHERE Id=@id", Map, P("@id", id));
        if (doc == null) return NotFound(new { message = "Врач не найден" });

        // Если у врача есть записи/медкарты — удалять нельзя (история), деактивируем
        var used = await _db.CountAsync("SELECT COUNT(*) FROM Appointments WHERE DoctorId=@id", P("@id", id))
                 + await _db.CountAsync("SELECT COUNT(*) FROM MedicalRecords WHERE DoctorId=@id", P("@id", id));
        if (used > 0)
        {
            await _db.ExecuteAsync("UPDATE Doctors SET IsActive=0 WHERE Id=@id", P("@id", id));
            return Ok(new { message = "У врача есть история приёмов — он деактивирован, а не удалён", deactivated = true });
        }

        await _db.InTransactionAsync(async (conn, tx) =>
        {
            await ExecTxAsync(conn, tx, "DELETE FROM Doctors WHERE Id=@id", P("@id", id));
            await ExecTxAsync(conn, tx, "DELETE FROM Users WHERE Id=@u", P("@u", doc.UserId));
            return 0;
        });
        return Ok(new { message = "Врач удалён" });
    }
}
