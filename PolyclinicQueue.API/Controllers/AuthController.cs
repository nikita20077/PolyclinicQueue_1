using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using PolyclinicQueue.API.Data;
using PolyclinicQueue.API.Models;
using PolyclinicQueue.API.Services;
using static PolyclinicQueue.API.Data.DatabaseHelper;

namespace PolyclinicQueue.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DatabaseHelper _db;
    private readonly JwtService _jwt;

    public AuthController(DatabaseHelper db, JwtService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    private static User MapUser(MySqlDataReader r) => new()
    {
        Id = r.GetInt("Id"),
        Login = r.GetStr("Login"),
        Password = r.GetStr("Password"),
        Role = r.GetStr("Role"),
        CreatedAt = r.GetDate("CreatedAt")
    };

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Login) || string.IsNullOrEmpty(req.Password))
            return BadRequest(new { message = "Введите логин и пароль" });

        var user = await _db.QuerySingleAsync(
            "SELECT Id, Login, Password, Role, CreatedAt FROM Users WHERE Login=@l AND Password=@p",
            MapUser, P("@l", req.Login.Trim()), P("@p", req.Password));

        if (user == null) return Unauthorized(new { message = "Неверный логин или пароль" });
        return Ok(await BuildAuthResponse(user));
    }

    // Регистрация пациента: логин, пароль и анкета сохраняются в БД (Users + Patients)
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto req)
    {
        var login = req.Login?.Trim() ?? "";
        if (login.Length < 3) return BadRequest(new { message = "Логин — минимум 3 символа" });
        if ((req.Password ?? "").Length < 4) return BadRequest(new { message = "Пароль — минимум 4 символа" });
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "Укажите ФИО" });
        if (req.BirthDate.HasValue && req.BirthDate.Value.Date > DateTime.Today)
            return BadRequest(new { message = "Дата рождения не может быть в будущем" });

        if (await _db.CountAsync("SELECT COUNT(*) FROM Users WHERE Login=@l", P("@l", login)) > 0)
            return Conflict(new { message = "Такой логин уже занят" });

        var policy = string.IsNullOrWhiteSpace(req.PolicyNumber) ? null : req.PolicyNumber.Trim();
        if (policy != null && await _db.CountAsync("SELECT COUNT(*) FROM Patients WHERE PolicyNumber=@p", P("@p", policy)) > 0)
            return Conflict(new { message = "Пациент с таким номером полиса уже зарегистрирован" });

        var userId = await _db.InTransactionAsync(async (conn, tx) =>
        {
            var uid = await ExecTxAsync(conn, tx,
                "INSERT INTO Users (Login, Password, Role, CreatedAt) VALUES (@l, @p, 'Patient', NOW())",
                P("@l", login), P("@p", req.Password));
            await ExecTxAsync(conn, tx,
                @"INSERT INTO Patients (UserId, FullName, BirthDate, Gender, PhoneNumber, PolicyNumber, Email, Address, CreatedAt)
                  VALUES (@u, @n, @b, @g, @ph, @pol, @e, @ad, NOW())",
                P("@u", uid), P("@n", req.FullName.Trim()), P("@b", req.BirthDate), P("@g", Clean(req.Gender)),
                P("@ph", Clean(req.PhoneNumber)), P("@pol", policy), P("@e", Clean(req.Email)), P("@ad", Clean(req.Address)));
            return uid;
        });

        return Ok(await BuildAuthResponse(new User { Id = userId, Login = login, Role = "Patient" }));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<object> BuildAuthResponse(User user)
    {
        var role = NormalizeRole(user.Role);
        int profileId = 0;
        string fullName = user.Login;

        if (role == "Doctor" || role == "Patient")
        {
            var table = role == "Doctor" ? "Doctors" : "Patients";
            var rows = await _db.QueryAsync($"SELECT Id, FullName FROM {table} WHERE UserId=@u",
                r => (Id: r.GetInt("Id"), Name: r.GetStr("FullName")), P("@u", user.Id));
            if (rows.Count > 0) { profileId = rows[0].Id; fullName = rows[0].Name; }
        }
        else if (role == "Admin")
        {
            fullName = "Администратор";
        }

        var redirect = role switch
        {
            "Admin" => "/admin.html",
            "Doctor" => "/doctor.html",
            _ => "/patient.html"
        };

        return new { token = _jwt.CreateToken(user, role, profileId), role, userId = user.Id, profileId, fullName, redirect };
    }

    private static string NormalizeRole(string role)
    {
        role = (role ?? "").Trim();
        return role.Length == 0 ? "" : char.ToUpper(role[0]) + role[1..].ToLower();
    }
}
