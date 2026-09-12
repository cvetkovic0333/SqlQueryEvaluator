\set ON_ERROR_STOP on

TRUNCATE fakultet.prijave_ispita, fakultet.upisi_predmeta, fakultet.ispitni_rokovi,
         fakultet.studenti, fakultet.predmeti, fakultet.profesori,
         fakultet.smerovi, fakultet.katedre
         RESTART IDENTITY CASCADE;

INSERT INTO fakultet.katedre (katedra_id, naziv, skraceni_naziv, godina_osnivanja) VALUES
 (1,  'Katedra za računarstvo i informatiku', 'RI',  1985),
 (2,  'Katedra za elektroniku',               'EL',  1960),
 (3,  'Katedra za telekomunikacije',          'TK',  1968),
 (4,  'Katedra za automatiku',                'AU',  1974),
 (5,  'Katedra za energetiku',                'EN',  1962),
 (6,  'Katedra za matematiku',                'MA',  1960),
 (7,  'Katedra za fiziku',                    'FI',  1961),
 (8,  'Katedra za mehatroniku',               'ME',  2004),
 (9,  'Katedra za biomedicinsku tehniku',     'BM',  2010),
 (10, 'Katedra za softversko inženjerstvo',   'SI',  2015);

INSERT INTO fakultet.smerovi (smer_id, naziv, katedra_id, nivo_studija, trajanje_godina, ukupno_espb) VALUES
 (1,  'Računarstvo i informatika',   1,  'osnovne',   4, 240),
 (2,  'Elektronika',                 2,  'osnovne',   4, 240),
 (3,  'Telekomunikacije',            3,  'osnovne',   4, 240),
 (4,  'Upravljanje sistemima',       4,  'osnovne',   4, 240),
 (5,  'Elektroenergetika',           5,  'osnovne',   4, 240),
 (6,  'Primenjena matematika',       6,  'osnovne',   3, 180),
 (7,  'Medicinska elektronika',      9,  'master',    1,  60),
 (8,  'Mehatronika',                 8,  'master',    1,  60),
 (9,  'Softversko inženjerstvo',     10, 'master',    1,  60),
 (10, 'Elektrotehnika i računarstvo', 1, 'doktorske', 3, 180);

WITH osnova AS (
    SELECT
        i,
        (CASE WHEN i % 2 = 0
              THEN (ARRAY['Dragan','Zoran','Milan','Predrag','Goran','Slobodan',
                          'Branislav','Vladan','Dejan','Nenad','Saša','Bratislav'])[1 + (i % 12)]
              ELSE (ARRAY['Snežana','Vesna','Gordana','Ljiljana','Biljana','Slavica',
                          'Dragana','Mirjana','Jelena','Aleksandra','Tatjana','Suzana'])[1 + (i % 12)]
         END) AS ime,
        (ARRAY['Jovanović','Petrović','Nikolić','Marković','Đorđević','Stojanović',
               'Ilić','Stanković','Pavlović','Milošević','Todorović','Ristić',
               'Lazarević','Kostić','Popović','Mitrović'])[1 + (i % 16)] AS prezime
    FROM generate_series(1, 100) AS i
)
INSERT INTO fakultet.profesori (profesor_id, ime, prezime, zvanje, katedra_id,
                                email, datum_izbora, kabinet)
SELECT
    o.i,
    o.ime,
    o.prezime,
    CASE
        WHEN o.i % 10 < 3 THEN 'asistent'
        WHEN o.i % 10 < 6 THEN 'docent'
        WHEN o.i % 10 < 8 THEN 'vanredni profesor'
        ELSE                   'redovni profesor'
    END,
    1 + ((o.i - 1) % 10),
    translate(lower(o.ime || '.' || o.prezime), 'čćžšđ', 'cczsd')
      || o.i || '@fakultet.rs',
    DATE '2008-10-01' + ((o.i * 53) % 5800),
    'K' || (1 + ((o.i - 1) % 10)) || '-' || lpad((100 + o.i)::text, 3, '0')
FROM osnova o;

WITH osnova AS (
    SELECT
        i,
        1 + ((i - 1) / 10) AS smer_id,
        1 + ((i - 1) % 10) AS redni_broj
    FROM generate_series(1, 100) AS i
)
INSERT INTO fakultet.predmeti (predmet_id, sifra, naziv, espb, semestar,
                               smer_id, profesor_id, obavezan)
SELECT
    o.i,
    k.skraceni_naziv || '-' || (100 + o.redni_broj * 10 + o.smer_id),
    (ARRAY['Osnovi programiranja','Diskretna matematika','Arhitektura računara',
           'Baze podataka','Algoritmi i strukture podataka','Operativni sistemi',
           'Računarske mreže','Veštačka inteligencija','Softversko inženjerstvo',
           'Zaštita informacija'])[o.redni_broj]
      || ' ' || o.smer_id,
    (ARRAY[6, 5, 6, 7, 8, 6, 5, 7, 6, 4])[o.redni_broj],
    1 + ((o.i * 3) % 8),
    o.smer_id,
    1 + ((o.i * 7) % 80),
    (o.redni_broj <= 7)
FROM osnova o
JOIN fakultet.smerovi s ON s.smer_id = o.smer_id
JOIN fakultet.katedre k ON k.katedra_id = s.katedra_id;

WITH osnova AS (
    SELECT
        i,
        1 + ((i - 1) % 10) AS smer_id,
        2019 + ((i * 3) % 7) AS godina_upisa,
        (CASE WHEN i % 2 = 0
              THEN (ARRAY['Marko','Nikola','Stefan','Miloš','Nemanja','Aleksandar',
                          'Luka','Filip','Vladimir','Petar','Dušan','Ivan',
                          'Bojan','Ognjen','Uroš'])[1 + (i % 15)]
              ELSE (ARRAY['Ana','Jovana','Milica','Marija','Katarina','Teodora',
                          'Sara','Ivana','Jelena','Tamara','Nevena','Dunja',
                          'Anđela','Kristina','Danica'])[1 + (i % 15)]
         END) AS ime,
        (ARRAY['Jovanović','Petrović','Nikolić','Marković','Đorđević','Stojanović',
               'Ilić','Stanković','Pavlović','Milošević','Todorović','Ristić',
               'Lazarević','Kostić','Popović','Mitrović','Simić','Radovanović',
               'Živković','Vasić'])[1 + (i % 20)] AS prezime
    FROM generate_series(1, 1500) AS i
)
INSERT INTO fakultet.studenti (student_id, ime, prezime, broj_indeksa, email,
                               smer_id, godina_upisa, grad, status, datum_rodjenja)
SELECT
    o.i,
    o.ime,
    o.prezime,
    o.godina_upisa || '/' || lpad(o.i::text, 4, '0'),
    translate(lower(o.ime || '.' || o.prezime), 'čćžšđ', 'cczsd')
      || o.i || '@student.fakultet.rs',
    o.smer_id,
    o.godina_upisa,
    (ARRAY['Niš','Beograd','Novi Sad','Kragujevac','Leskovac','Vranje',
           'Pirot','Prokuplje','Zaječar','Kruševac','Užice','Čačak'])
        [1 + (o.i % 12)],
    CASE
        WHEN o.i % 20 = 0 THEN 'ispisan'
        WHEN o.i % 17 = 0 THEN 'mirovanje'
        WHEN o.i % 11 = 0 THEN 'diplomirao'
        WHEN o.i % 7  = 0 THEN 'apsolvent'
        ELSE                   'aktivan'
    END,
    DATE '1998-01-01' + ((o.i * 37) % 3000)
FROM osnova o;

INSERT INTO fakultet.ispitni_rokovi (rok_id, naziv, skolska_godina, datum_pocetka, datum_kraja) VALUES
 (1,  'Januarski',    '2023/2024', DATE '2024-01-15', DATE '2024-02-04'),
 (2,  'Februarski',   '2023/2024', DATE '2024-02-05', DATE '2024-02-25'),
 (3,  'Aprilski',     '2023/2024', DATE '2024-04-08', DATE '2024-04-21'),
 (4,  'Junski',       '2023/2024', DATE '2024-06-10', DATE '2024-06-30'),
 (5,  'Septembarski', '2023/2024', DATE '2024-09-02', DATE '2024-09-22'),
 (6,  'Januarski',    '2024/2025', DATE '2025-01-13', DATE '2025-02-02'),
 (7,  'Februarski',   '2024/2025', DATE '2025-02-03', DATE '2025-02-23'),
 (8,  'Aprilski',     '2024/2025', DATE '2025-04-07', DATE '2025-04-20'),
 (9,  'Junski',       '2024/2025', DATE '2025-06-09', DATE '2025-06-29'),
 (10, 'Septembarski', '2024/2025', DATE '2025-09-01', DATE '2025-09-21');

WITH generisano AS (
    SELECT
        i,
        1 + ((i - 1) % 1500) AS student_id,
        (i - 1) / 1500       AS krug
    FROM generate_series(1, 5000) AS i
)
INSERT INTO fakultet.upisi_predmeta (upis_id, student_id, predmet_id,
                                     skolska_godina, datum_upisa)
SELECT
    g.i,
    g.student_id,
    ((1 + ((g.student_id - 1) % 10)) - 1) * 10
        + 1 + (((g.student_id * 7) + (g.krug * 3)) % 10),
    CASE WHEN g.krug < 2 THEN '2023/2024' ELSE '2024/2025' END,
    CASE WHEN g.krug < 2 THEN DATE '2023-10-01' + (g.i % 30)
                         ELSE DATE '2024-10-01' + (g.i % 30) END
FROM generisano g;

WITH osnova AS (
    SELECT
        u.upis_id,
        u.skolska_godina,
        CASE WHEN u.skolska_godina = '2023/2024'
             THEN 1 + (u.upis_id % 5)
             ELSE 6 + (u.upis_id % 5)
        END AS rok_id,
        CASE WHEN u.upis_id % 11 = 0
             THEN NULL
             ELSE least(100, greatest(0,
                      20 + mod(u.upis_id::bigint * 7919, 81)::int
                         + (st.smer_id - 5) * 4))
        END AS bodovi
    FROM fakultet.upisi_predmeta u
    JOIN fakultet.studenti st ON st.student_id = u.student_id
)
INSERT INTO fakultet.prijave_ispita (prijava_id, upis_id, rok_id, datum_prijave,
                                     bodovi, ocena, polozen)
SELECT
    o.upis_id,
    o.upis_id,
    o.rok_id,
    r.datum_pocetka - 7,
    o.bodovi,
    CASE
        WHEN o.bodovi IS NULL THEN NULL
        WHEN o.bodovi < 51    THEN 5
        WHEN o.bodovi < 61    THEN 6
        WHEN o.bodovi < 71    THEN 7
        WHEN o.bodovi < 81    THEN 8
        WHEN o.bodovi < 91    THEN 9
        ELSE                       10
    END,
    COALESCE(o.bodovi >= 51, false)
FROM osnova o
JOIN fakultet.ispitni_rokovi r ON r.rok_id = o.rok_id;

SELECT setval(pg_get_serial_sequence('fakultet.katedre',        'katedra_id'),  (SELECT max(katedra_id)  FROM fakultet.katedre));
SELECT setval(pg_get_serial_sequence('fakultet.smerovi',        'smer_id'),     (SELECT max(smer_id)     FROM fakultet.smerovi));
SELECT setval(pg_get_serial_sequence('fakultet.profesori',      'profesor_id'), (SELECT max(profesor_id) FROM fakultet.profesori));
SELECT setval(pg_get_serial_sequence('fakultet.predmeti',       'predmet_id'),  (SELECT max(predmet_id)  FROM fakultet.predmeti));
SELECT setval(pg_get_serial_sequence('fakultet.studenti',       'student_id'),  (SELECT max(student_id)  FROM fakultet.studenti));
SELECT setval(pg_get_serial_sequence('fakultet.upisi_predmeta', 'upis_id'),     (SELECT max(upis_id)     FROM fakultet.upisi_predmeta));
SELECT setval(pg_get_serial_sequence('fakultet.ispitni_rokovi', 'rok_id'),      (SELECT max(rok_id)      FROM fakultet.ispitni_rokovi));
SELECT setval(pg_get_serial_sequence('fakultet.prijave_ispita', 'prijava_id'),  (SELECT max(prijava_id)  FROM fakultet.prijave_ispita));

ANALYZE fakultet.katedre;
ANALYZE fakultet.smerovi;
ANALYZE fakultet.profesori;
ANALYZE fakultet.predmeti;
ANALYZE fakultet.studenti;
ANALYZE fakultet.upisi_predmeta;
ANALYZE fakultet.ispitni_rokovi;
ANALYZE fakultet.prijave_ispita;
