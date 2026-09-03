# Baza

SQL skripte za bazu `sqleval`. Sadrži **dve demo baze** koje aplikacija učitava
i nad kojima filtrira podatke, plus radne tabele same aplikacije.

## Šeme

| Šema | Domen | Tabela | Redova |
|---|---|---|---|
| `prodavnica` | Online prodavnica | 9 | 13 210 |
| `fakultet` | Studenti, predmeti, ispiti | 8 | 11 730 |
| `aplikacija` | Istorija upita i rezultati benchmarka | 3 | — |

### `prodavnica`

| Tabela | Redova | Opis |
|---|---|---|
| `kategorije` | 10 | Kategorije proizvoda |
| `dobavljaci` | 100 | Dobavljači |
| `zaposleni` | 100 | Zaposleni; `menadzer_id` je self-FK |
| `proizvodi` | 1 000 | Proizvodi u prodaji |
| `kupci` | 1 500 | Kupci |
| `porudzbine` | 2 000 | Porudžbine |
| `stavke_porudzbine` | 5 000 | Stavke porudžbine |
| `isporuke` | 1 500 | Isporuke |
| `recenzije` | 2 000 | Recenzije proizvoda |

### `fakultet`

| Tabela | Redova | Opis |
|---|---|---|
| `katedre` | 10 | Katedre |
| `smerovi` | 10 | Studijski programi |
| `profesori` | 100 | Nastavno osoblje |
| `predmeti` | 100 | Predmeti, po 10 na svakom smeru |
| `studenti` | 1 500 | Studenti |
| `ispitni_rokovi` | 10 | Ispitni rokovi |
| `upisi_predmeta` | 5 000 | Veza student–predmet (M:N) |
| `prijave_ispita` | 5 000 | Prijave ispita sa ocenama |

## Redosled pokretanja

```bash
psql -U postgres -d postgres -f Baza/00_kreiraj_bazu.sql
psql -U postgres -d sqleval  -f Baza/01_prodavnica_sema.sql
psql -U postgres -d sqleval  -f Baza/02_prodavnica_podaci.sql
psql -U postgres -d sqleval  -f Baza/03_fakultet_sema.sql
psql -U postgres -d sqleval  -f Baza/04_fakultet_podaci.sql
psql -U postgres -d sqleval  -f Baza/05_aplikacija_meta.sql
psql -U postgres -d sqleval  -f Baza/06_rola_samo_citanje.sql
```

Sve odjednom, iz korena projekta:

```bash
powershell -File Baza/pokreni_sve.ps1
```

Skripte su idempotentne — svaka počinje sa `DROP SCHEMA ... CASCADE`
odnosno `TRUNCATE`, pa se mogu pustiti ponovo bez čišćenja.

## Konvencije

- **Identifikatori su na srpskom, bez dijakritika** (`kolicina`, `porudzbine`,
  `menadzer_id`). Dijakritici u imenima kolona prave probleme u C#-u, JSON-u
  i u promptu koji se šalje modelu.
- **Vrednosti i opisi su na srpskom sa dijakriticima** (`isporučena`,
  `pouzećem`). Model ih vidi kroz `COMMENT ON`, što pomaže kod pitanja
  postavljenih na srpskom jeziku.
- **Podaci su potpuno deterministički** — generisani modularnom aritmetikom
  nad rednim brojem reda, bez `random()`. Ovo je uslov da benchmark bude
  ponovljiv: generisani SQL se poredi sa gold SQL-om po *rezultatu*, pa se
  podaci ne smeju menjati između dva pokretanja.
- Podaci nisu ravnomerni namerno: mali broj kupaca kupuje često, neki
  proizvodi su popularniji, smerovi imaju različitu prolaznost (38–78%).
  Bez te neravnomernosti upiti tipa „top 10" i „najbolji smer" vraćaju
  izjednačene rezultate i ne pokazuju ništa.

## Zaštita pri izvršavanju

Rola `sqleval_citanje` (skripta 06) je prvi sloj zaštite — SQL koji generiše
model izvršava se isključivo pod njom:

| Radnja | Ishod |
|---|---|
| `SELECT` nad `prodavnica` / `fakultet` | dozvoljeno |
| `DELETE`, `UPDATE`, `INSERT` | `cannot execute ... in a read-only transaction` |
| `DROP TABLE` | `cannot execute DROP TABLE in a read-only transaction` |
| pristup šemi `aplikacija` | `permission denied for schema aplikacija` |
| upit duži od 5 sekundi | prekida ga `statement_timeout` |

Drugi sloj je sanitizer u aplikaciji (Faza 3), koji odbija sve što nije
jedan jedini `SELECT` ili `WITH`.

> Lozinka role u skripti 06 je placeholder `PROMENI_ME`. Promeni je i
> stvarnu vrednost drži u `dotnet user-secrets`, nikad u repou.
