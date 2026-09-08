using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Api.Endpoints;

public sealed record PdfPodaci(
    string Pitanje,
    string Sql,
    string ModelId,
    int? OcenaSudije,
    string? Obrazlozenje,
    QueryResult Rezultat);

/// <summary>
/// Sastavlja PDF dokument sa pitanjem, generisanim SQL-om, ocenom sudije i
/// tabelom rezultata — dakle celim tokom, ne samo podacima. Takav dokument
/// se može priložiti uz rad kao dokaz šta je model odgovorio i na osnovu
/// čega je upit pušten na izvršenje.
/// </summary>
public static class PdfIzvoz
{
    private const string Mastilo = "#1c2431";
    private const string Prigusen = "#5c6779";
    private const string Akcenat = "#2f5d8a";
    private const string Linija = "#d8dee8";
    private const string Traka = "#f2f5f9";

    public static byte[] Napravi(PdfPodaci p)
    {
        return Document.Create(dokument =>
        {
            dokument.Page(strana =>
            {
                strana.Size(PageSizes.A4.Landscape());
                strana.Margin(28);
                strana.DefaultTextStyle(x => x.FontSize(9).FontColor(Mastilo).FontFamily("Lato"));

                strana.Header().Element(e => Zaglavlje(e, p));
                strana.Content().PaddingTop(14).Element(e => Sadrzaj(e, p));

                strana.Footer().PaddingTop(8).Row(red =>
                {
                    red.RelativeItem().Text($"Text-to-SQL Evaluator · {DateTime.Now:dd.MM.yyyy. HH:mm}")
                        .FontSize(7.5f).FontColor(Prigusen);
                    red.ConstantItem(90).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Prigusen));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Zaglavlje(IContainer okvir, PdfPodaci p)
    {
        okvir.BorderBottom(1.4f).BorderColor(Akcenat).PaddingBottom(8).Column(kolona =>
        {
            kolona.Item().Text("Rezultat upita").FontSize(16).SemiBold();
            kolona.Item().PaddingTop(2)
                .Text("Pitanje postavljeno govornim jezikom, prevedeno u SQL i izvršeno nad bazom")
                .FontSize(8).FontColor(Prigusen);
        });
    }

    private static void Sadrzaj(IContainer okvir, PdfPodaci p)
    {
        okvir.Column(kolona =>
        {
            kolona.Spacing(11);

            kolona.Item().Element(e => Polje(e, "PITANJE", p.Pitanje));

            kolona.Item().Column(c =>
            {
                c.Item().Text("GENERISANI SQL").FontSize(7.5f).SemiBold()
                    .FontColor(Prigusen).LetterSpacing(0.08f);
                c.Item().PaddingTop(3).Background(Traka).Border(1).BorderColor(Linija)
                    .Padding(8).Text(p.Sql).FontFamily(Fonts.Consolas).FontSize(8.5f);
            });

            kolona.Item().Row(red =>
            {
                red.Spacing(24);
                red.AutoItem().Element(e => Polje(e, "MODEL", p.ModelId));
                red.AutoItem().Element(e => Polje(e, "OCENA SUDIJE",
                    p.OcenaSudije is > 0 ? $"{p.OcenaSudije} / 5" : "nije ocenjeno"));
                red.AutoItem().Element(e => Polje(e, "BROJ REDOVA",
                    p.Rezultat.BrojRedova.ToString(CultureInfo.InvariantCulture)));
                red.AutoItem().Element(e => Polje(e, "TRAJANJE", $"{p.Rezultat.TrajanjeMs} ms"));
            });

            if (!string.IsNullOrWhiteSpace(p.Obrazlozenje))
                kolona.Item().Element(e => Polje(e, "OBRAZLOŽENJE SUDIJE", p.Obrazlozenje));

            kolona.Item().PaddingTop(4).Element(e => Tabela(e, p.Rezultat));

            if (p.Rezultat.Odsecen)
                kolona.Item().Text($"Prikazano je prvih {p.Rezultat.BrojRedova} redova; rezultat je odsečen.")
                    .FontSize(7.5f).Italic().FontColor(Prigusen);
        });
    }

    private static void Polje(IContainer okvir, string oznaka, string vrednost)
    {
        okvir.Column(c =>
        {
            c.Item().Text(oznaka).FontSize(7.5f).SemiBold().FontColor(Prigusen).LetterSpacing(0.08f);
            c.Item().PaddingTop(2).Text(vrednost).FontSize(9.5f);
        });
    }

    private static void Tabela(IContainer okvir, QueryResult rezultat)
    {
        if (rezultat.Kolone.Count == 0)
        {
            okvir.Text("Upit nije vratio nijednu kolonu.").Italic().FontColor(Prigusen);
            return;
        }

        okvir.Table(tabela =>
        {
            tabela.ColumnsDefinition(def =>
            {
                foreach (var _ in rezultat.Kolone)
                    def.RelativeColumn();
            });

            tabela.Header(zaglavlje =>
            {
                foreach (var kolona in rezultat.Kolone)
                {
                    zaglavlje.Cell().Background(Akcenat).Padding(5)
                        .Text(kolona).FontColor(Colors.White).SemiBold().FontSize(8);
                }
            });

            for (var i = 0; i < rezultat.Redovi.Count; i++)
            {
                foreach (var vrednost in rezultat.Redovi[i])
                {
                    var celija = tabela.Cell()
                        .Background(i % 2 == 1 ? Traka : Colors.White)
                        .BorderBottom(0.5f).BorderColor(Linija)
                        .Padding(4);

                    // NULL se namerno razlikuje od praznog teksta — u
                    // rezultatu upita to su dve različite stvari.
                    if (vrednost is null)
                        celija.Text("NULL").Italic().FontColor(Prigusen).FontSize(8);
                    else
                        celija.Text(Formatiraj(vrednost)).FontSize(8);
                }
            }
        });
    }

    private static string Formatiraj(object v) => v switch
    {
        bool b => b ? "da" : "ne",
        decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };
}
