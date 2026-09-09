using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SqlQueryEvaluator.Core.Evaluation;

namespace SqlQueryEvaluator.Api.Endpoints;

public sealed record RedMetrike(
    string Naziv,
    int Broj,
    double Tacnost,
    double ValidSql,
    double OcenaSudije,
    double TrajanjeMs,
    double Tokena,
    double Lak,
    double Srednji,
    double Tezak);

public sealed record PodaciMetrika(
    int PokretanjeId,
    DateTimeOffset Pocetak,
    string ModelSudije,
    int UkupnoPoziva,
    string? Pobednik,
    IReadOnlyList<RedMetrike> Modeli,
    SlaganjeSudije Slaganje);

/// <summary>
/// PDF sa rezultatima merenja — tabela koja ide u pisani deo rada.
/// Uz brojeve nosi i podatke o samom pokretanju (datum, sudija, broj poziva),
/// jer bez njih tabela nije proverljiva: ponuda besplatnih modela se menja,
/// pa merenje ima smisla samo uz datum i tačan naziv modela.
/// </summary>
public static class PdfMetrike
{
    private const string Mastilo = "#1c2431";
    private const string Prigusen = "#5c6779";
    private const string Akcenat = "#2f5d8a";
    private const string Linija = "#d8dee8";
    private const string Traka = "#f2f5f9";
    private const string Istaknut = "#e8f0f9";

    public static byte[] Napravi(PodaciMetrika p)
    {
        return Document.Create(dokument =>
        {
            dokument.Page(strana =>
            {
                strana.Size(PageSizes.A4.Landscape());
                strana.Margin(30);
                strana.DefaultTextStyle(x => x.FontSize(9).FontColor(Mastilo).FontFamily("Lato"));

                strana.Header().BorderBottom(1.4f).BorderColor(Akcenat).PaddingBottom(8).Column(k =>
                {
                    k.Item().Text("Rezultati testiranja modela").FontSize(16).SemiBold();
                    k.Item().PaddingTop(2)
                        .Text($"Pokretanje #{p.PokretanjeId} · {p.Pocetak:dd.MM.yyyy.} · "
                            + $"sudija: {p.ModelSudije} · {p.UkupnoPoziva} poziva")
                        .FontSize(8).FontColor(Prigusen);
                });

                strana.Content().PaddingTop(14).Column(k =>
                {
                    k.Spacing(16);
                    k.Item().Element(e => TabelaModela(e, p));
                    k.Item().Element(e => Slaganje(e, p.Slaganje));
                    k.Item().Element(Legenda);
                });

                strana.Footer().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Text($"Text-to-SQL Evaluator · izvezeno {DateTime.Now:dd.MM.yyyy. HH:mm}")
                        .FontSize(7.5f).FontColor(Prigusen);
                    r.ConstantItem(80).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Prigusen));
                        t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void TabelaModela(IContainer okvir, PodaciMetrika p)
    {
        okvir.Table(tabela =>
        {
            tabela.ColumnsDefinition(def =>
            {
                def.RelativeColumn(3);                       // model
                foreach (var _ in Enumerable.Range(0, 8))
                    def.RelativeColumn();
            });

            tabela.Header(z =>
            {
                string[] naslovi =
                [
                    "Model", "Tačnost", "Ispravan SQL", "Ocena sudije",
                    "Trajanje", "Tokena", "Lak", "Srednji", "Težak"
                ];

                for (var i = 0; i < naslovi.Length; i++)
                {
                    var c = z.Cell().Background(Akcenat).Padding(5);
                    var t = c.AlignMiddle();
                    if (i > 0) t = t.AlignRight();
                    t.Text(naslovi[i]).FontColor(Colors.White).SemiBold().FontSize(8);
                }
            });

            for (var i = 0; i < p.Modeli.Count; i++)
            {
                var m = p.Modeli[i];
                var pobednik = m.Naziv == p.Pobednik;
                var pozadina = pobednik ? Istaknut : (i % 2 == 1 ? Traka : Colors.White);

                Tekst(m.Naziv + (pobednik ? "  ★" : ""), levo: true, jak: pobednik);
                Tekst(Procenat(m.Tacnost), jak: pobednik);
                Tekst(Procenat(m.ValidSql));
                Tekst(m.OcenaSudije.ToString("0.00", CultureInfo.InvariantCulture));
                Tekst(Trajanje(m.TrajanjeMs));
                Tekst(m.Tokena.ToString("0", CultureInfo.InvariantCulture));
                Tekst(Procenat(m.Lak));
                Tekst(Procenat(m.Srednji));
                Tekst(Procenat(m.Tezak));

                void Tekst(string v, bool levo = false, bool jak = false)
                {
                    var celija = tabela.Cell().Background(pozadina)
                        .BorderBottom(0.5f).BorderColor(Linija).Padding(5);
                    var red = levo ? celija : celija.AlignRight();
                    var stil = red.Text(v).FontSize(8.5f);
                    if (jak) stil.SemiBold();
                }
            }
        });
    }

    private static void Slaganje(IContainer okvir, SlaganjeSudije s)
    {
        okvir.Background(Traka).Border(1).BorderColor(Linija).Padding(12).Column(k =>
        {
            k.Item().Text("Pouzdanost sudije").FontSize(11).SemiBold();
            k.Item().PaddingTop(3)
                .Text("Koliko se ocena sudije poklapa sa objektivnom proverom, "
                    + "gde se rezultat generisanog upita poredi sa rezultatom tačnog.")
                .FontSize(8).FontColor(Prigusen);

            k.Item().PaddingTop(8).Row(r =>
            {
                r.Spacing(28);
                Podatak(r, "SLAGANJE", $"{s.Slaganje:0.0}%");
                Podatak(r, "COHEN'S KAPPA", $"{s.Kappa:0.000}");
                Podatak(r, "TUMAČENJE", MetricsCalculator.OpisKappe(s.Kappa));
                Podatak(r, "POHVALIO NETAČNE", s.LaznoPozitivno.ToString());
                Podatak(r, "ODBACIO TAČNE", s.LaznoNegativno.ToString());
            });
        });

        static void Podatak(RowDescriptor red, string oznaka, string vrednost)
        {
            red.AutoItem().Column(c =>
            {
                c.Item().Text(oznaka).FontSize(7).SemiBold().FontColor(Prigusen).LetterSpacing(0.08f);
                c.Item().PaddingTop(2).Text(vrednost).FontSize(12).SemiBold();
            });
        }
    }

    private static void Legenda(IContainer okvir)
    {
        okvir.Column(k =>
        {
            k.Spacing(2);
            k.Item().Text("Tačnost (execution accuracy) — udeo upita čiji se rezultat poklopio sa rezultatom tačnog upita.")
                .FontSize(7.5f).FontColor(Prigusen);
            k.Item().Text("Ispravan SQL — udeo upita koji su se izvršili bez greške, bez obzira da li je rezultat tačan.")
                .FontSize(7.5f).FontColor(Prigusen);
            k.Item().Text("Lak / Srednji / Težak — tačnost po težini zadatka; po 15 zadataka na svakom nivou.")
                .FontSize(7.5f).FontColor(Prigusen);
        });
    }

    private static string Procenat(double v) => $"{v.ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string Trajanje(double ms) => ms < 1000
        ? $"{ms:0} ms"
        : $"{(ms / 1000).ToString("0.00", CultureInfo.InvariantCulture)} s";
}
