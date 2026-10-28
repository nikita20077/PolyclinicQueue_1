namespace PolyclinicQueue.API.Data;

/// <summary>
/// Страховка для уже существующей БД: добавляет новые колонки и таблицу Reviews, если их нет.
/// Если вы выполнили полный скрипт Database/PolyclinicQueue_full.sql — здесь ничего не изменится.
/// </summary>
public static class DbInitializer
{
    private static readonly (string Table, string Column, string Def)[] Columns =
    {
        ("Doctors", "BirthDate", "DATE NULL"), ("Doctors", "Gender", "VARCHAR(10) NULL"),
        ("Doctors", "Category", "VARCHAR(50) NULL"), ("Doctors", "ExperienceYears", "INT NULL"),
        ("Doctors", "Education", "VARCHAR(255) NULL"), ("Doctors", "HireDate", "DATE NULL"),
        ("Doctors", "Email", "VARCHAR(100) NULL"), ("Doctors", "Address", "VARCHAR(255) NULL"),
        ("Patients", "Gender", "VARCHAR(10) NULL"), ("Patients", "Address", "VARCHAR(255) NULL")
    };

    public static async Task EnsureAsync(DatabaseHelper db, ILogger logger)
    {
        try
        {
            foreach (var (table, column, def) in Columns)
            {
                var exists = await db.CountAsync(
                    @"SELECT COUNT(*) FROM information_schema.COLUMNS
                      WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER(@t) AND LOWER(COLUMN_NAME) = LOWER(@c)",
                    DatabaseHelper.P("@t", table), DatabaseHelper.P("@c", column));
                if (exists == 0)
                {
                    await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {def}");
                    logger.LogInformation("Добавлена колонка {Table}.{Column}", table, column);
                }
            }

            await db.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS Reviews (
                Id INT AUTO_INCREMENT PRIMARY KEY, AppointmentId INT NOT NULL, PatientId INT NOT NULL, DoctorId INT NOT NULL,
                Rating TINYINT NOT NULL, Comment TEXT NULL, CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY UX_Reviews_Appointment (AppointmentId), KEY IX_Reviews_Doctor (DoctorId)
              ) CHARACTER SET utf8mb4");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось проверить структуру БД. Выполните Database/PolyclinicQueue_full.sql в MySQL Workbench.");
        }
    }
}
