using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ReqLens.DataGen;

/// <summary>
/// Renders one requisition. Three layouts on purpose: a real lab receives forms from many clinics
/// and none of them agree on where anything goes, which is the whole reason extraction is not
/// a regex.
/// </summary>
public sealed class RequisitionDocument(Requisition r) : IDocument
{
    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(34);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Colors.Black));
            page.Content().Element(Body);
            page.Footer().PaddingTop(8).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Colors.Grey.Darken1));
                t.Span("SYNTHETIC TEST DOCUMENT - NOT A REAL REQUISITION - NO PROTECTED HEALTH INFORMATION");
            });
        });

    private void Body(IContainer c)
    {
        switch (r.Layout)
        {
            case Layout.Compact:   Compact(c);   break;
            case Layout.TwoColumn: TwoColumn(c); break;
            default:               Gridded(c);   break;
        }
    }

    // ---- shared bits -------------------------------------------------------

    private void Masthead(IContainer c, bool rule) =>
        c.Column(col =>
        {
            col.Item().Text(r.Tenant.Name).FontSize(14).SemiBold();
            col.Item().Text("GENETIC TESTING REQUISITION").FontSize(10).Light().LetterSpacing(0.08f);
            if (rule) col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Black);
        });

    private static void Field(IContainer c, string label, string? value) =>
        c.Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(6.5f).FontColor(Colors.Grey.Darken2).LetterSpacing(0.06f);
            col.Item().Text(string.IsNullOrWhiteSpace(value) ? " " : value).FontSize(10);
        });

    private void ConsentLine(IContainer c) =>
        c.Text(t =>
        {
            t.Span(r.ConsentObtained ? "[X]" : "[  ]").FontSize(11).SemiBold();
            t.Span("  Patient consent for genetic testing obtained and on file").FontSize(9);
        });

    private void SignatureBlock(IContainer c) =>
        c.Column(col =>
        {
            col.Item().PaddingTop(10).LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).Text("Ordering provider signature").FontSize(6.5f).FontColor(Colors.Grey.Darken2);
            if (r.Defects.HasFlag(Defect.HandwrittenNote))
                col.Item().PaddingTop(6).Text("Note: pls run reflex if inconclusive - see chart")
                          .FontSize(9).Italic().FontColor(Colors.Blue.Darken2);
        });

    // ---- layout one: single column, dense ----------------------------------

    private void Compact(IContainer c) =>
        c.Column(col =>
        {
            col.Spacing(7);
            col.Item().Element(x => Masthead(x, rule: true));
            col.Item().Row(row =>
            {
                row.RelativeItem(2).Element(x => Field(x, "Ordering provider", r.Provider.Display));
                row.RelativeItem(1).Element(x => Field(x, "NPI", r.PrintedNpi));
            });
            col.Item().Row(row =>
            {
                row.RelativeItem(2).Element(x => Field(x, "Patient name", $"{r.Patient.LastName}, {r.Patient.FirstName}"));
                row.RelativeItem(1).Element(x => Field(x, "DOB", r.Patient.Dob));
                row.RelativeItem(1).Element(x => Field(x, "Sex", r.Patient.Sex));
            });
            col.Item().Row(row =>
            {
                row.RelativeItem(1).Element(x => Field(x, "MRN", r.Patient.Mrn));
                row.RelativeItem(1).Element(x => Field(x, "Collection date", r.CollectionDate));
                row.RelativeItem(2).Element(x => Field(x, "Specimen", r.SpecimenType));
            });
            col.Item().Element(x => Field(x, "Test ordered", r.PrintedPanel));
            col.Item().Element(x => Field(x, "Diagnosis (ICD-10)",
                r.Diagnosis is null ? "" : $"{r.Diagnosis.Code}  {r.Diagnosis.Description}"));
            col.Item().PaddingTop(4).Element(ConsentLine);
            col.Item().Element(SignatureBlock);
        });

    // ---- layout two: patient left, order right -----------------------------

    private void TwoColumn(IContainer c) =>
        c.Column(outer =>
        {
            outer.Spacing(10);
            outer.Item().Element(x => Masthead(x, rule: false));
            outer.Item().PaddingTop(2).LineHorizontal(2).LineColor(Colors.Grey.Darken1);
            outer.Item().Row(row =>
            {
                row.RelativeItem().PaddingRight(14).Column(left =>
                {
                    left.Spacing(7);
                    left.Item().Text("PATIENT").FontSize(8).SemiBold().FontColor(Colors.Grey.Darken3);
                    left.Item().Element(x => Field(x, "Last name", r.Patient.LastName));
                    left.Item().Element(x => Field(x, "First name", r.Patient.FirstName));
                    left.Item().Element(x => Field(x, "Date of birth", r.Patient.Dob));
                    left.Item().Element(x => Field(x, "Sex", r.Patient.Sex));
                    left.Item().Element(x => Field(x, "Medical record no.", r.Patient.Mrn));
                });
                row.ConstantItem(0.75f).LineVertical(0.75f).LineColor(Colors.Grey.Medium);
                row.RelativeItem().PaddingLeft(14).Column(right =>
                {
                    right.Spacing(7);
                    right.Item().Text("ORDER").FontSize(8).SemiBold().FontColor(Colors.Grey.Darken3);
                    right.Item().Element(x => Field(x, "Provider", r.Provider.Display));
                    right.Item().Element(x => Field(x, "NPI", r.PrintedNpi));
                    right.Item().Element(x => Field(x, "Panel", r.PrintedPanel));
                    right.Item().Element(x => Field(x, "Specimen", r.SpecimenType));
                    right.Item().Element(x => Field(x, "Collected", r.CollectionDate));
                    right.Item().Element(x => Field(x, "ICD-10",
                        r.Diagnosis is null ? "" : r.Diagnosis.Code));
                });
            });
            outer.Item().Element(ConsentLine);
            outer.Item().Element(SignatureBlock);
        });

    // ---- layout three: boxed grid, the "official form" look ----------------

    private void Gridded(IContainer c) =>
        c.Column(col =>
        {
            col.Spacing(0);
            col.Item().Background(Colors.Grey.Lighten3).Padding(8).Element(x => Masthead(x, rule: false));

            col.Item().Border(0.75f).BorderColor(Colors.Grey.Darken1).Table(table =>
            {
                table.ColumnsDefinition(d => { d.RelativeColumn(); d.RelativeColumn(); d.RelativeColumn(); });

                static IContainer Cell(IContainer x) =>
                    x.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(6);

                table.Cell().Element(Cell).Element(x => Field(x, "Patient last", r.Patient.LastName));
                table.Cell().Element(Cell).Element(x => Field(x, "Patient first", r.Patient.FirstName));
                table.Cell().Element(Cell).Element(x => Field(x, "DOB", r.Patient.Dob));

                table.Cell().Element(Cell).Element(x => Field(x, "Sex", r.Patient.Sex));
                table.Cell().Element(Cell).Element(x => Field(x, "MRN", r.Patient.Mrn));
                table.Cell().Element(Cell).Element(x => Field(x, "Collected", r.CollectionDate));

                table.Cell().ColumnSpan(2).Element(Cell).Element(x => Field(x, "Ordering provider", r.Provider.Display));
                table.Cell().Element(Cell).Element(x => Field(x, "NPI", r.PrintedNpi));

                table.Cell().ColumnSpan(2).Element(Cell).Element(x => Field(x, "Test ordered", r.PrintedPanel));
                table.Cell().Element(Cell).Element(x => Field(x, "Specimen", r.SpecimenType));

                table.Cell().ColumnSpan(3).Element(Cell).Element(x => Field(x, "Diagnosis (ICD-10)",
                    r.Diagnosis is null ? "" : $"{r.Diagnosis.Code}  {r.Diagnosis.Description}"));
            });

            col.Item().PaddingTop(8).Element(ConsentLine);
            col.Item().Element(SignatureBlock);
        });
}
