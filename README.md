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
Baza/                           SQL skripte za obe demo baze + rola za čitanje
benchmark/testset.json          45 test zadataka (lak/srednji/težak) × srpski/engleski
src/SqlQueryEvaluator.Core/     logika: provajderi modela, text-to-SQL, evaluacija
src/SqlQueryEvaluator.Api/      Minimal API + wwwroot (frontend)
src/SqlQueryEvaluator.Benchmark/  offline runner koji testira sve modele
tests/SqlQueryEvaluator.Tests/  xUnit testovi
```

## Demo baze

Aplikacija radi nad **dve nezavisne baze** — bira se koja se učitava i filtrira.
Identifikatori su na srpskom, bez dijakritika.

| Šema | Domen | Tabela | Redova |
|---|---|---|---|
| `prodavnica` | kategorije, dobavljači, zaposleni, proizvodi, kupci, porudžbine, stavke, isporuke, recenzije | 9 | 13 700 |
| `fakultet` | katedre, smerovi, profesori, predmeti, studenti, upisi, rokovi, prijave ispita | 8 | 11 720 |

Detalji i redosled pokretanja skripti: [Baza/README.md](Baza/README.md).

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
powershell -File Baza/pokreni_sve.ps1
```

```bash
dotnet user-secrets set "ApiKeys:Groq" "..." --project src/SqlQueryEvaluator.Api
```

```bash
dotnet run --project src/SqlQueryEvaluator.Api
```

Prva komanda kreira bazu `sqleval`, obe demo šeme sa podacima, radne tabele
aplikacije i read-only rolu. Druga upisuje API ključ (nikada u `appsettings.json` —
repo je javan). Treća diže i API i frontend na istom portu.

U VS Code-u: **F5** → „API (web + frontend)".

## Bezbednost izvršavanja

SQL koji je generisao model izvršava se u dva sloja zaštite:

1. **baza** — rola `sqleval_citanje` sme samo `SELECT` nad demo šemama, radne
   podatke aplikacije uopšte ne vidi, a upit joj se prekida posle 5 sekundi
2. **aplikacija** — sanitizer: samo jedan `SELECT`/`WITH` statement, blacklist
   ključnih reči, read-only transakcija koja se uvek završava `ROLLBACK`-om

Provereno na živoj bazi — `DELETE`, `DROP TABLE` i pristup šemi `aplikacija`
odbijaju se već na nivou baze, i pre nego što sanitizer dođe na red.
