var builder = WebApplication.CreateBuilder(args);

// Faza 2-6: registracija ModelRegistry-ja, provajdera, SchemaIntrospector-a,
// SqlSanitizer-a, QueryExecutor-a, LlmJudge-a i repozitorijuma.

var app = builder.Build();

// Frontend (wwwroot) se servira iz istog procesa kao i API - nema CORS-a
// ni drugog dev servera; jedan `dotnet run` dize i UI i API.
app.UseDefaultFiles();
app.UseStaticFiles();

// Faza 6: app.MapSchemaEndpoints(); MapTranslateEndpoints(); MapExecuteEndpoints();
//         MapHistoryEndpoints(); MapBenchmarkEndpoints();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", phase = "struktura projekta" }));

app.Run();
