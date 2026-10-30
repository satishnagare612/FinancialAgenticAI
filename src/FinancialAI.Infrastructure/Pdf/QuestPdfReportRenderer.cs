using FinancialAI.Application.Ports;
using FinancialAI.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinancialAI.Infrastructure.Pdf;

public sealed class QuestPdfReportRenderer : IReportRenderer
{
    static QuestPdfReportRenderer()
    {
        // QuestPDF requires Settings.License to be assigned before any
        // Document.Create call. Setting it in the static constructor
        // guarantees it runs before the first Render — whether that call
        // comes from PersistAndRenderExecutor, the /api/reports/{id}/pdf
        // endpoint, or a test.
        //
        // Community license: free for individuals and companies below
        // QuestPDF's revenue threshold. Verify eligibility before shipping.
        // If the project no longer qualifies, replace with the paid
        // LicenseType.Professional and supply the purchased license key.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(Report report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header()
                    .Text($"Financial AI Projection Report — {report.Id}")
                    .SemiBold().FontSize(18);
                page.Content().Column(col =>
                {
                    col.Item().Text($"Generated Date: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
                    col.Item().Text("Assumptions").Bold();
                    col.Item().Text(string.Join("\n", report.Assumptions));
                    col.Item().Text("Narrative").Bold();
                    col.Item().Text(report.Narrative ?? "(No narrative provided)");
                    col.Item().Text("Key Risks").Bold();
                    col.Item().Text(string.Join("\n", report.KeyRisks.Select(r => $"• {r}")));
                    col.Item().Text("Cited Sources").Bold();
                    col.Item().Text(string.Join("\n", report.CitedSources));
                });
                page.Footer()
                    .Text("Disclaimer: This document is an automated projection report and does not constitute formal legal or financial advice.")
                    .FontSize(8).Italic();
            });
        }).GeneratePdf();
    }
}