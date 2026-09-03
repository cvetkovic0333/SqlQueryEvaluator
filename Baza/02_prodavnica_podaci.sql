-- =====================================================================
-- 02_prodavnica_podaci.sql — popunjavanje šeme "prodavnica"
--
-- Podaci su 100% DETERMINISTIČKI (modularna aritmetika nad rednim brojem
-- reda, bez random()). To je bitno: rezultati benchmarka moraju da budu
-- ponovljivi, jer se generisani SQL poredi sa gold SQL-om po rezultatu.
--
-- Broj redova po tabeli:
--   kategorije 10 | dobavljaci 100 | zaposleni 100 | proizvodi 1000
--   kupci 1500 | porudzbine 2000 | stavke_porudzbine 5000
--   isporuke 1500 | recenzije 2000
--
-- Pokretanje: psql -U postgres -d sqleval -f Baza/02_prodavnica_podaci.sql
-- =====================================================================
\set ON_ERROR_STOP on

TRUNCATE prodavnica.recenzije, prodavnica.isporuke, prodavnica.stavke_porudzbine,
         prodavnica.porudzbine, prodavnica.proizvodi, prodavnica.kupci,
         prodavnica.zaposleni, prodavnica.dobavljaci, prodavnica.kategorije
         RESTART IDENTITY CASCADE;

-- ------------------------------------------------------ kategorije (10)
INSERT INTO prodavnica.kategorije (kategorija_id, naziv, opis) VALUES
 (1,  'Laptopovi',              'Prenosivi računari svih klasa'),
 (2,  'Mobilni telefoni',       'Pametni telefoni i dodatna oprema'),
 (3,  'Televizori',             'LED, OLED i QLED televizori'),
 (4,  'Bela tehnika',           'Veš mašine, frižideri i šporeti'),
 (5,  'Audio oprema',           'Slušalice, zvučnici i pojačala'),
 (6,  'Računarske komponente',  'Grafičke karte, procesori i memorija'),
 (7,  'Gaming',                 'Konzole, gejmpadovi i gaming oprema'),
 (8,  'Kancelarijska oprema',   'Štampači, skeneri i potrošni materijal'),
 (9,  'Kućni aparati',          'Mali kućni aparati'),
 (10, 'Sportska oprema',        'Bicikli, trenažeri i fitnes oprema');

-- ------------------------------------------------------ dobavljaci (100)
INSERT INTO prodavnica.dobavljaci (dobavljac_id, naziv, grad, drzava, email, telefon, ocena)
SELECT
    i,
    (ARRAY['Delta','Nova','Alfa','Vektor','Merkur','Panonija','Balkan',
           'Zenit','Orion','Kontinental','Sirius','Trijumf'])[1 + (i % 12)]
      || ' ' ||
    (ARRAY['Trade','Grupa','Komerc','Distribucija','Import','Logistik'])[1 + (i % 6)]
      || ' d.o.o.',
    (ARRAY['Beograd','Novi Sad','Niš','Kragujevac','Subotica','Zrenjanin',
           'Pančevo','Čačak'])[1 + (i % 8)],
    (ARRAY['Srbija','Srbija','Srbija','Srbija','Hrvatska','Slovenija',
           'Mađarska','Bosna i Hercegovina'])[1 + (i % 8)],
    'nabavka' || i || '@dobavljac.rs',
    '+381' || (60 + (i % 9)) || lpad(((i * 137) % 10000000)::text, 7, '0'),
    round((3.0 + ((i * 7) % 21) / 10.0)::numeric, 2)
FROM generate_series(1, 100) AS i;

-- ------------------------------------------------------- zaposleni (100)
-- Hijerarhija: 1 = direktor (menadzer_id NULL), 2..6 = rukovodioci sektora,
-- 7..100 = izvršioci raspoređeni po sektorima. Omogućava self-join zadatke.
WITH osnova AS (
    SELECT
        i,
        (CASE WHEN i % 2 = 0
              THEN (ARRAY['Marko','Nikola','Stefan','Miloš','Nemanja','Aleksandar',
                          'Luka','Filip','Vladimir','Petar','Dušan','Ivan'])[1 + (i % 12)]
              ELSE (ARRAY['Ana','Jovana','Milica','Marija','Katarina','Teodora',
                          'Sara','Ivana','Jelena','Tamara','Nevena','Dunja'])[1 + (i % 12)]
         END) AS ime,
        (ARRAY['Jovanović','Petrović','Nikolić','Marković','Đorđević','Stojanović',
               'Ilić','Stanković','Pavlović','Milošević','Todorović','Ristić',
               'Lazarević','Kostić','Popović','Mitrović'])[1 + (i % 16)] AS prezime
    FROM generate_series(1, 100) AS i
)
INSERT INTO prodavnica.zaposleni (zaposleni_id, ime, prezime, email, radno_mesto,
                                  sektor, menadzer_id, datum_zaposlenja, plata)
SELECT
    o.i,
    o.ime,
    o.prezime,
    translate(lower(o.ime || '.' || o.prezime), 'čćžšđ', 'cczsd')
      || o.i || '@prodavnica.rs',
    CASE
        WHEN o.i = 1             THEN 'Direktor'
        WHEN o.i BETWEEN 2 AND 6 THEN 'Rukovodilac sektora'
        WHEN o.i % 4 = 0         THEN 'Viši referent'
        ELSE                          'Referent'
    END,
    CASE
        WHEN o.i = 1 THEN 'Uprava'
        ELSE (ARRAY['Prodaja','Marketing','Logistika','IT','Finansije'])
                 [1 + ((o.i - 2) % 5)]
    END,
    CASE
        WHEN o.i = 1             THEN NULL
        WHEN o.i BETWEEN 2 AND 6 THEN 1
        ELSE 2 + ((o.i - 7) % 5)
    END,
    DATE '2015-03-01' + ((o.i * 47) % 3200),
    CASE
        WHEN o.i = 1             THEN 320000
        WHEN o.i BETWEEN 2 AND 6 THEN 185000 + (o.i * 2500)
        WHEN o.i % 4 = 0         THEN 110000 + ((o.i * 311) % 35000)
        ELSE                          78000 + ((o.i * 211) % 28000)
    END
FROM osnova o;

-- ------------------------------------------------------- proizvodi (1000)
-- Cena polazi od realne bazne cene kategorije i množi se rasipnim faktorom
-- 0.55–1.70. "rasipanje" je permutacija skupa 0..999 (7919 je prost i
-- uzajamno prost sa 1000), pa su vrednosti raspršene, a ipak determinističke.
WITH osnova AS (
    SELECT
        i,
        1 + ((i - 1) % 10)               AS kategorija_id,
        mod(i::bigint * 7919, 1000)::int AS rasipanje
    FROM generate_series(1, 1000) AS i
)
INSERT INTO prodavnica.proizvodi (proizvod_id, naziv, kategorija_id, dobavljac_id, cena,
                                  kolicina_na_stanju, tezina_kg, aktivan,
                                  datum_povlacenja, datum_kreiranja)
SELECT
    o.i,
    (ARRAY['Lenovo','HP','Dell','Asus','Acer','Samsung','LG','Sony',
           'Philips','Bosch','Xiaomi','Canon'])[1 + (o.i % 12)]
      || ' ' ||
    (ARRAY['Laptop','Telefon','Televizor','Veš mašina','Slušalice',
           'Grafička karta','Gejmpad','Štampač','Usisivač','Bicikl'])[o.kategorija_id]
      || ' ' ||
    (ARRAY['X','S','Pro','Max','Lite','Ultra'])[1 + (o.i % 6)]
      || '-' || (1000 + ((o.i * 7) % 8999)),
    o.kategorija_id,
    1 + ((o.i * 13) % 100),
    round(
        (ARRAY[89000, 55000, 72000, 65000, 18000,
               48000, 26000, 22000, 12000, 38000])[o.kategorija_id]
        * (0.55 + o.rasipanje / 1000.0 * 1.15)
    , 2),
    mod(o.rasipanje * 7 + o.i, 250),
    round((0.15 + mod(o.rasipanje * 3, 400) / 10.0)::numeric, 3),
    (o.i % 17 <> 0),
    CASE WHEN o.i % 17 = 0 THEN DATE '2024-02-01' + ((o.i * 3) % 500) ELSE NULL END,
    DATE '2021-01-01' + ((o.i * 11) % 1200)
FROM osnova o;

-- ----------------------------------------------------------- kupci (1500)
WITH osnova AS (
    SELECT
        i,
        (CASE WHEN i % 2 = 0
              THEN (ARRAY['Marko','Nikola','Stefan','Miloš','Nemanja','Aleksandar',
                          'Luka','Filip','Vladimir','Petar','Dušan','Ivan',
                          'Bojan','Zoran','Uroš'])[1 + (i % 15)]
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
INSERT INTO prodavnica.kupci (kupac_id, ime, prezime, email, grad, drzava,
                              segment, datum_registracije, aktivan)
SELECT
    o.i,
    o.ime,
    o.prezime,
    translate(lower(o.ime || '.' || o.prezime), 'čćžšđ', 'cczsd')
      || o.i || '@mail.rs',
    (ARRAY['Beograd','Novi Sad','Niš','Kragujevac','Subotica','Zrenjanin',
           'Pančevo','Čačak','Kraljevo','Leskovac','Novi Pazar','Šabac'])
        [1 + (o.i % 12)],
    (ARRAY['Srbija','Srbija','Srbija','Srbija','Srbija','Srbija','Srbija',
           'Crna Gora','Bosna i Hercegovina','Hrvatska'])[1 + (o.i % 10)],
    CASE
        WHEN o.i % 10 = 0       THEN 'vip'
        WHEN o.i % 10 IN (1, 2) THEN 'pravno lice'
        ELSE                         'maloprodaja'
    END,
    DATE '2020-01-01' + ((o.i * 13) % 1800),
    (o.i % 23 <> 0)
FROM osnova o;

-- ------------------------------------------------------ porudzbine (2000)
-- Porudžbine 1..1500 su otpremljene (imaju red u tabeli isporuke),
-- 1501..2000 su nove / plaćene / otkazane (bez isporuke).
--
-- Kupci nisu ravnomerno raspoređeni: svaka peta porudžbina ide u grupu od
-- prvih 150 kupaca, čime se dobija realan "rep" čestih kupaca. Bez toga
-- upiti tipa "10 kupaca sa najvećom potrošnjom" nemaju šta da pokažu.
INSERT INTO prodavnica.porudzbine (porudzbina_id, kupac_id, zaposleni_id,
                                   datum_porudzbine, status, nacin_placanja,
                                   popust_procenat)
SELECT
    i,
    CASE WHEN i % 5 < 2
         THEN 1 + mod(i::bigint * 7919, 150)::int
         ELSE 1 + mod(i::bigint * 7919, 1500)::int
    END,
    CASE WHEN i % 9 = 0 THEN NULL ELSE 1 + ((i * 3) % 100) END,
    DATE '2023-01-01' + ((i * 11) % 900),
    CASE
        WHEN i <= 1500 THEN
            CASE WHEN i % 25 = 0 THEN 'vraćena'
                 WHEN i % 4  = 0 THEN 'poslata'
                 ELSE                 'isporučena' END
        ELSE
            CASE WHEN i % 7 = 0 THEN 'otkazana'
                 WHEN i % 3 = 0 THEN 'nova'
                 ELSE                'plaćena' END
    END,
    (ARRAY['kartica','kartica','kartica','pouzećem','prenos','paypal'])[1 + (i % 6)],
    CASE
        WHEN i % 31 = 0 THEN 15.00
        WHEN i % 13 = 0 THEN 10.00
        WHEN i % 7  = 0 THEN 5.00
        ELSE                 0
    END
FROM generate_series(1, 2000) AS i;

-- ----------------------------------------------- stavke_porudzbine (5000)
-- Svaka porudžbina dobija 2-3 stavke. proizvod_id se računa tako da se
-- unutar iste porudžbine nikada ne ponovi (poštuje UNIQUE ograničenje).
-- Svaka treća porudžbina bira iz "popularnih" proizvoda, da bi rangiranje
-- najprodavanijih imalo smisla.
--
-- Moduli su PROSTI brojevi (199 i 937), a ne okrugli. Sa modulom 1000
-- kategorija proizvoda ((proizvod_id - 1) % 10) postaje prosta funkcija
-- broja porudžbine, pa svaki kupac kupuje iz svega par kategorija i upiti
-- tipa "kupci koji kupuju iz više kategorija" nemaju šta da vrate.
-- Uz modul 937 proizvodi 938-1000 se nikada ne naruče, što je realno i
-- daje smisla upitu "koji proizvodi nikada nisu naručeni".
WITH generisano AS (
    SELECT
        i,
        1 + ((i - 1) % 2000) AS porudzbina_id,
        (i - 1) / 2000       AS krug
    FROM generate_series(1, 5000) AS i
),
mapirano AS (
    SELECT
        g.i,
        g.porudzbina_id,
        CASE WHEN g.porudzbina_id % 3 = 0
             THEN 1 + (((g.porudzbina_id * 7)  + (g.krug * 67))  % 199)
             ELSE 1 + (((g.porudzbina_id * 17) + (g.krug * 331)) % 937)
        END AS proizvod_id
    FROM generisano g
)
INSERT INTO prodavnica.stavke_porudzbine (stavka_id, porudzbina_id, proizvod_id,
                                          kolicina, cena, popust_procenat)
SELECT
    m.i,
    m.porudzbina_id,
    m.proizvod_id,
    1 + (m.i % 5),
    p.cena,
    CASE WHEN m.i % 11 = 0 THEN 10.00
         WHEN m.i % 23 = 0 THEN 5.00
         ELSE                   0 END
FROM mapirano m
JOIN prodavnica.proizvodi p ON p.proizvod_id = m.proizvod_id;

-- -------------------------------------------------------- isporuke (1500)
INSERT INTO prodavnica.isporuke (isporuka_id, porudzbina_id, kurirska_sluzba,
                                 datum_slanja, datum_isporuke, trosak_dostave,
                                 broj_posiljke)
SELECT
    p.porudzbina_id,
    p.porudzbina_id,
    (ARRAY['Post Express','BEX','D Express','City Express','AKS'])
        [1 + (p.porudzbina_id % 5)],
    p.datum_porudzbine + (1 + (p.porudzbina_id % 5)),
    CASE WHEN p.status = 'poslata' THEN NULL
         ELSE p.datum_porudzbine + (1 + (p.porudzbina_id % 5))
                                 + (1 + (p.porudzbina_id % 7)) END,
    250 + ((p.porudzbina_id % 12) * 50),
    'RS' || lpad(p.porudzbina_id::text, 8, '0')
FROM prodavnica.porudzbine p
WHERE p.porudzbina_id <= 1500;

-- ------------------------------------------------------- recenzije (2000)
-- Recenzije nisu ravnomerne: 60% ide na sto najrecenziranijih proizvoda,
-- ostatak se razliva, a deo proizvoda ostaje bez ijedne recenzije. Sa
-- ravnomernom raspodelom svaki proizvod dobije tačno dve recenzije, pa
-- upit "proizvodi sa najmanje tri recenzije" nema šta da vrati.
INSERT INTO prodavnica.recenzije (recenzija_id, proizvod_id, kupac_id, ocena,
                                  komentar, datum)
SELECT
    i,
    CASE WHEN i % 5 < 3
         THEN 1 + mod(i::bigint * 7919, 100)::int
         ELSE 1 + mod(i::bigint * 7919, 797)::int
    END,
    1 + ((i * 29) % 1500),
    CASE
        WHEN i % 10 < 5 THEN 5
        WHEN i % 10 < 7 THEN 4
        WHEN i % 10 < 8 THEN 3
        WHEN i % 10 < 9 THEN 2
        ELSE                 1
    END,
    CASE WHEN i % 6 = 0 THEN NULL
         ELSE (ARRAY['Odličan proizvod, sve preporuke.',
                     'Vredi svaki dinar.',
                     'Solidno za ovu cenu.',
                     'Očekivao sam više, ali je upotrebljivo.',
                     'Brza isporuka, proizvod kao na slici.',
                     'Nisam zadovoljan kvalitetom.',
                     'Radi besprekorno već mesecima.',
                     'Ambalaža oštećena, proizvod ispravan.'])[1 + (i % 8)]
    END,
    DATE '2023-02-01' + ((i * 7) % 850)
FROM generate_series(1, 2000) AS i;

-- ------------------------------- sinhronizacija IDENTITY sekvenci
SELECT setval(pg_get_serial_sequence('prodavnica.kategorije',        'kategorija_id'), (SELECT max(kategorija_id) FROM prodavnica.kategorije));
SELECT setval(pg_get_serial_sequence('prodavnica.dobavljaci',        'dobavljac_id'),  (SELECT max(dobavljac_id)  FROM prodavnica.dobavljaci));
SELECT setval(pg_get_serial_sequence('prodavnica.zaposleni',         'zaposleni_id'),  (SELECT max(zaposleni_id)  FROM prodavnica.zaposleni));
SELECT setval(pg_get_serial_sequence('prodavnica.proizvodi',         'proizvod_id'),   (SELECT max(proizvod_id)   FROM prodavnica.proizvodi));
SELECT setval(pg_get_serial_sequence('prodavnica.kupci',             'kupac_id'),      (SELECT max(kupac_id)      FROM prodavnica.kupci));
SELECT setval(pg_get_serial_sequence('prodavnica.porudzbine',        'porudzbina_id'), (SELECT max(porudzbina_id) FROM prodavnica.porudzbine));
SELECT setval(pg_get_serial_sequence('prodavnica.stavke_porudzbine', 'stavka_id'),     (SELECT max(stavka_id)     FROM prodavnica.stavke_porudzbine));
SELECT setval(pg_get_serial_sequence('prodavnica.isporuke',          'isporuka_id'),   (SELECT max(isporuka_id)   FROM prodavnica.isporuke));
SELECT setval(pg_get_serial_sequence('prodavnica.recenzije',         'recenzija_id'),  (SELECT max(recenzija_id)  FROM prodavnica.recenzije));

ANALYZE prodavnica.kategorije;
ANALYZE prodavnica.dobavljaci;
ANALYZE prodavnica.zaposleni;
ANALYZE prodavnica.proizvodi;
ANALYZE prodavnica.kupci;
ANALYZE prodavnica.porudzbine;
ANALYZE prodavnica.stavke_porudzbine;
ANALYZE prodavnica.isporuke;
ANALYZE prodavnica.recenzije;
