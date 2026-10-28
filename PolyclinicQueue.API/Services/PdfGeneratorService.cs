using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PolyclinicQueue.API.Services;

public record PrescriptionData(string PatientName, DateTime? BirthDate, string Diagnosis, string? Prescriptions,
    string? Recommendations, string DoctorName, string? Specialization, DateTime Date);

/// <summary>Серверная генерация PDF-рецепта (QuestPDF, Community license). Шрифт Arial — есть в Windows.</summary>
public class PdfGeneratorService
{
    private readonly IConfiguration _cfg;

    public PdfGeneratorService(IConfiguration cfg)
    {
        _cfg = cfg;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Prescription(PrescriptionData d)
    {
        var name = _cfg["Clinic:Name"] ?? "Поликлиника";
        var address = _cfg["Clinic:Address"] ?? "";
        var phone = _cfg["Clinic:Phone"] ?? "";
        var drugs = (d.Prescriptions ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(12));

                page.Header().Column(c =>
                {
                    c.Item().Text(name).FontSize(22).Bold().FontColor("#3498DB");
                    c.Item().Text(address).FontColor(Colors.Grey.Darken1);
                    c.Item().Text("Тел.: " + phone).FontColor(Colors.Grey.Darken1);
                    c.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#3498DB");
                });

                page.Content().PaddingVertical(18).Column(c =>
                {
                    c.Spacing(10);
                    c.Item().AlignCenter().Text("РЕЦЕПТ").FontSize(20).Bold();
                    c.Item().Text(t => { t.Span("Пациент: ").SemiBold(); t.Span(d.PatientName); });
                    c.Item().Text(t => { t.Span("Дата рождения: ").SemiBold(); t.Span(d.BirthDate?.ToString("dd.MM.yyyy") ?? "—"); });
                    c.Item().Text(t => { t.Span("Дата приёма: ").SemiBold(); t.Span(d.Date.ToString("dd.MM.yyyy")); });
                    c.Item().Text(t => { t.Span("Диагноз: ").SemiBold(); t.Span(d.Diagnosis); });

                    c.Item().PaddingTop(6).Text("Назначения:").SemiBold();
                    if (drugs.Count == 0) c.Item().Text("—");
                    for (int i = 0; i < drugs.Count; i++)
                        c.Item().Text($"{i + 1}. {drugs[i]}");

                    if (!string.IsNullOrWhiteSpace(d.Recommendations))
                    {
                        c.Item().PaddingTop(6).Text("Рекомендации:").SemiBold();
                        c.Item().Text(d.Recommendations);
                    }

                    c.Item().PaddingTop(40).Row(r =>
                    {
                        r.RelativeItem().Text($"Врач: {d.DoctorName}" + (string.IsNullOrWhiteSpace(d.Specialization) ? "" : $" ({d.Specialization})"));
                        r.RelativeItem().AlignRight().Text("Подпись: ______________");
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Электронный документ · сформирован ").FontSize(9).FontColor(Colors.Grey.Medium);
                    t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }
}
