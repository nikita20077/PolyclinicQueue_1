using System.Security.Claims;

namespace PolyclinicQueue.API.Data;

/// <summary>Данные из JWT и проверка прав доступа.</summary>
public static class Access
{
    public static int UserId(this ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>Id в таблице Doctors или Patients (0 для администратора).</summary>
    public static int ProfileId(this ClaimsPrincipal u) =>
        int.TryParse(u.FindFirstValue("profileId"), out var id) ? id : 0;

    public static string RoleName(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.Role) ?? "";
    public static bool IsAdmin(this ClaimsPrincipal u) => u.RoleName() == "Admin";

    // Врачебный доступ к пациенту дополнительно проверяется по специальности (DoctorHasPatientAsync)
    public static bool CanSeePatient(this ClaimsPrincipal u, int patientId) =>
        u.RoleName() is "Admin" or "Doctor" || (u.RoleName() == "Patient" && u.ProfileId() == patientId);

    public static bool CanSeeAppointment(this ClaimsPrincipal u, int patientId, int doctorId) =>
        u.IsAdmin()
        || (u.RoleName() == "Doctor" && u.ProfileId() == doctorId)
        || (u.RoleName() == "Patient" && u.ProfileId() == patientId);

    public static async Task<(int PatientId, int DoctorId, string Status)?> GetAppointmentInfoAsync(this DatabaseHelper db, int id)
    {
        var list = await db.QueryAsync(
            "SELECT PatientId, DoctorId, Status FROM Appointments WHERE Id=@id",
            r => (PatientId: r.GetInt("PatientId"), DoctorId: r.GetInt("DoctorId"), Status: r.GetStr("Status")),
            DatabaseHelper.P("@id", id));
        if (list.Count == 0) return null;
        return list[0];
    }

    /// <summary>Специальность врача (например «Стоматолог»).</summary>
    public static async Task<string> GetDoctorSpecializationAsync(this DatabaseHelper db, int doctorId) =>
        Convert.ToString(await db.ScalarAsync("SELECT Specialization FROM Doctors WHERE Id=@d", DatabaseHelper.P("@d", doctorId))) ?? "";

    /// <summary>
    /// Пациент «принадлежит» врачу, если у него есть неотменённая запись к врачу ТОЙ ЖЕ специальности
    /// (стоматолог видит только тех, кто лечился у стоматологов клиники).
    /// </summary>
    public static async Task<bool> DoctorHasPatientAsync(this DatabaseHelper db, int doctorId, int patientId) =>
        await db.CountAsync(
            @"SELECT COUNT(*) FROM Appointments a JOIN Doctors d ON d.Id = a.DoctorId
              WHERE a.PatientId=@p AND a.Status<>'Cancelled'
                AND d.Specialization = (SELECT Specialization FROM Doctors WHERE Id=@me)",
            DatabaseHelper.P("@p", patientId), DatabaseHelper.P("@me", doctorId)) > 0;
}
