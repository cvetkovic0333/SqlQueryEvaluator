using System.Net;
using System.Text;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Llm.Providers;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Tests;

public class ProvajderiTests
{
    private sealed class LazniServer(string telo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage zahtev, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(telo, Encoding.UTF8, "application/json")
            });
    }

    private static Task<LlmResponse> Gemini(string telo) =>
        new GeminiProvider(
                new ModelDescriptor { Id = "gemini:test", Model = "test" },
                new HttpClient(new LazniServer(telo)), "kljuc")
            .CompleteAsync(new LlmRequest("s", "u"));

    private static Task<LlmResponse> Groq(string telo) =>
        new OpenAiCompatibleProvider(
                new ModelDescriptor { Id = "groq:test", Model = "test", BaseUrl = "https://groq.test/v1" },
                new HttpClient(new LazniServer(telo)), "kljuc")
            .CompleteAsync(new LlmRequest("s", "u"));

    [Fact]
    public async Task Gemini_prepoznaje_odgovor_presecen_razmisljanjem()
    {
        var o = await Gemini("""
            {"candidates":[{"content":{"parts":[{"text":"**:\n - Is `polozen` boolean?","thoughtSignature":"x"}],"role":"model"},
              "finishReason":"MAX_TOKENS"}],
             "usageMetadata":{"promptTokenCount":124,"candidatesTokenCount":47,"thoughtsTokenCount":1149}}
            """);

        Assert.True(o.Presecen);
        Assert.Equal(47 + 1149, o.IzlazniTokeni);
    }

    [Fact]
    public async Task Gemini_ne_ubacuje_razmisljanje_u_odgovor()
    {
        var o = await Gemini("""
            {"candidates":[{"content":{"parts":[{"text":"razmisljam...","thought":true},{"text":"SELECT 1"}]},
              "finishReason":"STOP"}]}
            """);

        Assert.False(o.Presecen);
        Assert.Equal("SELECT 1", o.Text);
    }

    [Fact]
    public async Task Gemini_bez_delova_vraca_prazan_tekst_umesto_greske()
    {
        var o = await Gemini("""{"candidates":[{"content":{"role":"model"},"finishReason":"MAX_TOKENS"}]}""");

        Assert.True(o.Presecen);
        Assert.Equal("", o.Text);
    }

    [Fact]
    public async Task Groq_prepoznaje_finish_reason_length()
    {
        var o = await Groq("""
            {"choices":[{"message":{"content":"SELECT a FROM"},"finish_reason":"length"}],
             "usage":{"prompt_tokens":10,"completion_tokens":1200}}
            """);

        Assert.True(o.Presecen);
        Assert.Equal(1200, o.IzlazniTokeni);
    }

    [Fact]
    public async Task Groq_zavrsen_odgovor_nije_presecen()
    {
        var o = await Groq("""{"choices":[{"message":{"content":"SELECT 1"},"finish_reason":"stop"}]}""");

        Assert.False(o.Presecen);
    }

    [Fact]
    public void Sanitizer_odbija_presecen_odgovor_iako_je_komad_ispravan_sql()
    {
        var r = SqlSanitizer.Proveri("SELECT a FROM t", presecen: true);

        Assert.False(r.Prihvacen);
        Assert.Contains("granicu", r.Razlog);
    }
}
