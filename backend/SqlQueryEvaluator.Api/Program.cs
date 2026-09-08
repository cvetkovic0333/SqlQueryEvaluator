using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using SqlQueryEvaluator.Api.Endpoints;
using SqlQueryEvaluator.Core;

var builder = WebApplication.CreateBuilder(args);

// Tajne se traže IZRIČITO, iako ih ASP.NET u Development režimu obično doda
// sam. To automatsko dodavanje zavisi od atributa koji SDK generiše pri
// build-u, pa ume da izostane — aplikacija se tada podigne bez lozinke za
// bazu i pukne tek na prvom upitu, sa porukom koja ne kaže pravi uzrok.
builder.Configuration
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();

builder.Services.DodajSqlQueryEvaluator(builder.Configuration);

// Ako lozinka nedostaje, bolje je pući odmah i jasno nego na prvom kliku
// korisnika uz stotinu linija Npgsql stack trace-a.
ProveriKonfiguraciju(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});

var app = builder.Build();

// Frontend živi u zasebnom folderu /frontend, van backend projekta.
// Servira se i dalje iz istog procesa — jedan `dotnet run` diže i API i
// korisnički interfejs, bez drugog servera i bez CORS-a.
var frontend = NadjiFrontend(builder.Environment.ContentRootPath);
var fajlovi = new PhysicalFileProvider(frontend);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fajlovi });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fajlovi,

    // U razvoju se JS i CSS menjaju stalno, a browser ih kešira i servira
    // staru verziju — izmena izgleda kao da nije primenjena dok se ne uradi
    // Ctrl+F5. Zato se u Development režimu keširanje isključuje.
    OnPrepareResponse = kontekst =>
    {
        if (kontekst.Context.RequestServices
                .GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            kontekst.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }
    }
});

// Frontend (wwwroot) se servira iz istog procesa kao i API — nema CORS-a
// ni drugog dev servera; jedan `dotnet run` diže i UI i API.
app.MapModelEndpoints();
app.MapSemaEndpoints();
app.MapUpitEndpoints();
app.MapIstorijaEndpoints();
app.MapBenchmarkEndpoints();

app.MapGet("/api/zdravlje", () => Results.Ok(new { status = "ok" }));

app.Run();

static void ProveriKonfiguraciju(IConfiguration konfiguracija)
{
    string[] obavezni = ["AplikacijaDb", "UpitDb"];
    var nedostaju = obavezni
        .Where(n =>
        {
            var cs = konfiguracija.GetConnectionString(n);
            return string.IsNullOrWhiteSpace(cs)
                   || cs.Contains("Password=;", StringComparison.OrdinalIgnoreCase)
                   || cs.TrimEnd().EndsWith("Password=", StringComparison.OrdinalIgnoreCase);
        })
        .ToList();

    if (nedostaju.Count == 0)
        return;

    Console.Error.WriteLine();
    Console.Error.WriteLine("╭─ KONFIGURACIJA NIJE POTPUNA ─────────────────────────────────╮");
    Console.Error.WriteLine($"  Nedostaje lozinka u connection stringu: {string.Join(", ", nedostaju)}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Postavi ih (svaku komandu zasebno, sačekaj potvrdu):");
    Console.Error.WriteLine();
    Console.Error.WriteLine("    dotnet user-secrets set \"ConnectionStrings:AplikacijaDb\" \\");
    Console.Error.WriteLine("      \"Host=localhost;Port=5432;Database=sqleval;Username=postgres;Password=LOZINKA\" \\");
    Console.Error.WriteLine("      --project src/SqlQueryEvaluator.Api");
    Console.Error.WriteLine();
    Console.Error.WriteLine("    dotnet user-secrets set \"ConnectionStrings:UpitDb\" \\");
    Console.Error.WriteLine("      \"Host=localhost;Port=5432;Database=sqleval;Username=sqleval_citanje;Password=LOZINKA\" \\");
    Console.Error.WriteLine("      --project src/SqlQueryEvaluator.Api");
    Console.Error.WriteLine("╰──────────────────────────────────────────────────────────────╯");
    Console.Error.WriteLine();

    Environment.Exit(1);
}

/// <summary>
/// Traži folder /frontend polazeći od projekta naviše. Radi i kada se
/// aplikacija pokrene iz korena projekta i kada se pokrene iz bin foldera.
/// </summary>
static string NadjiFrontend(string pocetak)
{
    var direktorijum = new DirectoryInfo(pocetak);
    while (direktorijum is not null)
    {
        var kandidat = Path.Combine(direktorijum.FullName, "frontend");
        if (File.Exists(Path.Combine(kandidat, "index.html")))
            return kandidat;
        direktorijum = direktorijum.Parent;
    }

    throw new DirectoryNotFoundException(
        "Nije pronađen folder 'frontend' sa index.html. Očekuje se u korenu projekta.");
}

public partial class Program;
