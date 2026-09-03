using System.Text.Json.Serialization;
using SqlQueryEvaluator.Api.Endpoints;
using SqlQueryEvaluator.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.DodajSqlQueryEvaluator(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Frontend (wwwroot) se servira iz istog procesa kao i API — nema CORS-a
// ni drugog dev servera; jedan `dotnet run` diže i UI i API.
app.MapModelEndpoints();
app.MapSemaEndpoints();
app.MapUpitEndpoints();
app.MapIstorijaEndpoints();
app.MapBenchmarkEndpoints();

app.MapGet("/api/zdravlje", () => Results.Ok(new { status = "ok" }));

app.Run();
