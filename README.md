# Text-to-SQL Evaluator

Diplomski rad — prevođenje pitanja na govornom jeziku u SQL upite pomoću modela
veštačke inteligencije, uz evaluaciju kvaliteta generisanih upita i izvršavanje
nad PostgreSQL bazom.

Aplikacija pokriva ceo tok rada:

1. korisnik učita bazu i izabere tabele nad kojima filtrira,
2. otkuca pitanje na **srpskom ili engleskom**,
3. model prevede pitanje u SQL,
4. **LLM-as-a-Judge** oceni generisani upit (1–5) uz obrazloženje,
5. upit ocenjen kao dobar se šalje na izvršenje i rezultat se prikazuje,
6. dashboard prikazuje poređenje svih testiranih modela — zašto je izabran baš taj model.

## Tehnologije

| Sloj | Tehnologija |
|---|---|
| Backend | C# / .NET 9, ASP.NET Core Minimal API |
| Frontend | čist JavaScript (ES moduli) + Chart.js, bez build alata |
| Baza | PostgreSQL 18 |
| Okruženje | VS Code (`dotnet` CLI, C# Dev Kit) |

## Struktura

```
db/                             SQL skripte (šema, seed, meta tabele, read-only rola)
benchmark/testset.json          45 test zadataka (easy/medium/hard) × srpski/engleski
src/SqlQueryEvaluator.Core/     logika: provajderi modela, text-to-SQL, evaluacija
src/SqlQueryEvaluator.Api/      Minimal API + wwwroot (frontend)
src/SqlQueryEvaluator.Benchmark/  offline runner koji testira sve modele
tests/SqlQueryEvaluator.Tests/  xUnit testovi
```

## Testirani modeli

Besplatni API tierovi: **Groq**, **Google Gemini**, **OpenRouter (`:free`)**, **Mistral**.
Novi model se dodaje jednim unosom u `Models` nizu u `appsettings.json` — bez pisanja koda.

## Metrike

- **Execution accuracy (EX)** — izvrši se i generisani i tačan (gold) SQL, pa se porede rezultati
- **Ocena sudije (1–5)** — LLM-as-a-Judge, uz obrazloženje
- **Slaganje sudije sa EX** (+ Cohen's kappa) — koliko je sam sudija pouzdan
- **Valid SQL rate**, prosečna latencija, potrošnja tokena
- Sve razloženo **po težini zadatka** i **po jeziku pitanja**

## Pokretanje

```bash
# 1. baza
psql -U postgres -c "CREATE DATABASE sqleval;"
psql -U postgres -d sqleval -f db/01_schema.sql
psql -U postgres -d sqleval -f db/02_seed.sql
psql -U postgres -d sqleval -f db/03_app_meta.sql
psql -U postgres -d sqleval -f db/04_readonly_role.sql

# 2. API ključevi (nikada u appsettings.json)
dotnet user-secrets set "ApiKeys:Groq" "..." --project src/SqlQueryEvaluator.Api

# 3. aplikacija
dotnet run --project src/SqlQueryEvaluator.Api
```

U VS Code-u: **F5** → „API (web + frontend)".

## Bezbednost izvršavanja

SQL koji je generisao model izvršava se u dva sloja zaštite:

1. baza — zasebna rola `sqleval_ro` sa isključivo `SELECT` pravom nad šemom `shop`
2. aplikacija — sanitizer (samo jedan `SELECT`/`WITH` statement, blacklist ključnih reči,
   `statement_timeout`, read-only transakcija koja se uvek završava `ROLLBACK`-om)
