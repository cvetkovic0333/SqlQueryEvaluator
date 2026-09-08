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
frontend/                       korisnički interfejs: HTML, CSS, JS moduli, Chart.js
backend/SqlQueryEvaluator.Api/        Minimal API; servira i frontend, isti proces
backend/SqlQueryEvaluator.Core/       logika: provajderi modela, text-to-SQL, evaluacija
backend/SqlQueryEvaluator.Benchmark/  offline runner koji meri sve modele
Baza/                           SQL skripte za obe demo baze + rola za čitanje
benchmark/testset.json          45 test zadataka (lak/srednji/težak)
tests/SqlQueryEvaluator.Tests/  xUnit testovi
```

Front i back su razdvojeni u zasebne foldere, ali se i dalje pokreću jednom
komandom — API servira `frontend/` iz istog procesa, pa nema drugog servera
ni CORS-a.

## Demo baze

Aplikacija radi nad **dve nezavisne baze** — bira se koja se učitava i filtrira.
Identifikatori su na srpskom, bez dijakritika.

| Šema | Domen | Tabela | Redova |
|---|---|---|---|
| `prodavnica` | kategorije, dobavljači, zaposleni, proizvodi, kupci, porudžbine, stavke, isporuke, recenzije | 9 | 13 210 |
| `fakultet` | katedre, smerovi, profesori, predmeti, studenti, upisi, rokovi, prijave ispita | 8 | 11 730 |

Detalji i redosled pokretanja skripti: [Baza/README.md](Baza/README.md).

## Testirani modeli

Besplatni API tierovi: **Groq**, **Google Gemini**, **OpenRouter (`:free`)**, **Mistral** —
devet modela u registru. Tri od četiri provajdera izlažu isti OpenAI-kompatibilan
oblik, pa ih pokriva jedna klasa; Gemini ima svoju. Novi model se dodaje **jednim
unosom u `Models` nizu** u `appsettings.json`, bez pisanja koda:

```json
{ "Id": "groq:gpt-oss-120b", "Kind": "openai",
  "BaseUrl": "https://api.groq.com/openai/v1",
  "Model": "openai/gpt-oss-120b", "ApiKeyRef": "Groq" }
```

> Ponuda besplatnih modela se menja. Ako neki naziv prestane da radi,
> `--dry-run` to odmah pokaže, a ispravka je izmena jednog JSON polja.

## Test set

`benchmark/testset.json` — **45 zadataka**, po 15 na svakom nivou težine, svaki
na srpskom i engleskom, nad obe šeme:

| Težina | Šta pokriva |
|---|---|
| lak (15) | jedna tabela: `WHERE`, `ORDER BY`, `LIMIT`, `COUNT`, `ILIKE`, `BETWEEN`, `IS NULL` |
| srednji (15) | 2–3 `JOIN`, `GROUP BY` + `HAVING`, datumske funkcije, `DISTINCT`, self-join |
| težak (15) | CTE, window funkcije, `RANK`, `LAG`, running total, `NOT EXISTS`, `EXCEPT`, agregacija nad agregacijom |

Svaki gold SQL je izvršen nad bazom i vraća neprazan rezultat — prazan rezultat
je loš test, jer i pogrešan upit koji ne vrati ništa „pogodi".

## Metrike

- **Execution accuracy (EX)** — izvrši se i generisani i gold SQL, pa se porede
  *rezultati*, ne tekst upita. Redovi se porede kao multiskup osim kada gold ima
  `ORDER BY`; imena kolona se ignorišu, brojevi se zaokružuju na 4 decimale.
- **Ocena sudije (1–5)** — LLM-as-a-Judge, uz obrazloženje na srpskom
- **Slaganje sudije sa EX** + **Cohen's kappa** — koliko je sam sudija pouzdan
- **Valid SQL rate**, prosečna latencija, potrošnja tokena
- Sve razloženo **po težini zadatka** i **po jeziku pitanja**

## Pokretanje

**1. Baza** — kreira `sqleval`, obe demo šeme sa podacima, radne tabele i read-only rolu:

```bash
powershell -File Baza/pokreni_sve.ps1
```

**2. Lozinke i API ključevi** — u `user-secrets`, nikada u `appsettings.json` (repo je javan):

```bash
dotnet user-secrets set "ApiKeys:Groq" "gsk_..." --project backend/SqlQueryEvaluator.Api
```

Isto za `ApiKeys:Gemini`, `ApiKeys:OpenRouter`, `ApiKeys:Mistral`, kao i za
`ConnectionStrings:AplikacijaDb` i `ConnectionStrings:UpitDb`.
Alternativa su promenljive okruženja: `GROQ_API_KEY`, `GEMINI_API_KEY`, …

**3. Provera da ključevi rade** — pre nego što se potroši kvota na pun test:

```bash
dotnet run --project backend/SqlQueryEvaluator.Benchmark -- --dry-run
```

**4. Test modela** — probno na 5 zadataka, pa pun run:

```bash
dotnet run --project backend/SqlQueryEvaluator.Benchmark -- --limit 5
```

Pun test je 45 zadataka × 2 jezika × broj modela. Prekinut test se nastavlja sa
`--resume <id>` — već urađeni parovi se preskaču i kvota se ne troši dvaput.

**5. Aplikacija:**

```bash
dotnet run --project backend/SqlQueryEvaluator.Api
```

U VS Code-u: **F5** → „API (web + frontend)". Jedan proces diže i API i frontend.

## Aplikacija

Tri sekcije:

1. **Baza i upit** — sa leve strane tabele sa brojem redova, tipovima kolona i
   ključevima; sa desne polje za pitanje. Namerno na istom ekranu: pitanje se
   kuca dok se vidi šta baza sadrži. Čekiranjem tabela bira se kontekst koji ide
   modelu. Posle prevoda se prikazuje SQL, ocena sudije sa obrazloženjem, pa
   rezultat izvršavanja. Upit ocenjen sa 4 ili 5 ide odmah; ispod toga aplikacija
   traži izričitu potvrdu.
2. **Rezultati modela** — grafikoni iz stvarnih rezultata testa: tačnost po modelu,
   po težini zadatka, brzina naspram tačnosti, i poređenje ocene sudije sa
   objektivnom tačnošću. Pobednik je istaknut — to je obrazloženje zašto je izabran
   baš taj model. Prikazuju se samo jezici koji su stvarno mereni. Dok test nije
   pokrenut, prikazuje se uputstvo, a ne izmišljeni brojevi.
3. **Istorija** — prethodni upiti sa ocenom i statusom; klik vraća upit u editor.

## Bezbednost izvršavanja

SQL koji je generisao model izvršava se u dva sloja zaštite:

1. **baza** — rola `sqleval_citanje` sme samo `SELECT` nad demo šemama, radne
   podatke aplikacije uopšte ne vidi, a upit joj se prekida posle 5 sekundi
2. **aplikacija** — sanitizer: samo jedan `SELECT`/`WITH` statement, blacklist
   ključnih reči, read-only transakcija koja se uvek završava `ROLLBACK`-om

Sanitizer maskira string literale pre provere ključnih reči, pa legitiman upit
`WHERE status = 'otkazana'` prolazi, a `DROP` sakriven iza komentara ne prolazi.

Provereno na živoj bazi — `DELETE`, `DROP TABLE`, `pg_sleep`, pristup šemi
`aplikacija` i sistemskim katalozima odbijaju se, i to na oba nivoa.

## Testovi

```bash
dotnet test
```

64 testa: sanitizer (uključujući pokušaje zaobilaženja), poređenje rezultata,
Cohen's kappa, parsiranje odgovora sudije, širenje konteksta preko stranih ključeva
i ispravnost samog test seta.
