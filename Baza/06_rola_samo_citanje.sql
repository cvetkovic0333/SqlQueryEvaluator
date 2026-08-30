-- =====================================================================
-- 06_rola_samo_citanje.sql — rola "sqleval_citanje"
--
-- PRVI I NAJVAŽNIJI SLOJ ZAŠTITE.
--
-- SQL koji je generisao jezički model izvršava se isključivo pod ovom
-- rolom. Rola sme samo da čita demo šeme (prodavnica, fakultet) i nema
-- nikakav pristup šemi "aplikacija". Čak i kada bi sanitizer u aplikaciji
-- propustio nešto opasno, baza to odbija.
--
-- VAŽNO: lozinka ispod je placeholder. Promeni je pre pokretanja i
-- stvarnu vrednost stavi u user-secrets, nikad u ovaj fajl:
--   dotnet user-secrets set "ConnectionStrings:UpitDb" "..." \
--     --project src/SqlQueryEvaluator.Api
--
-- Pokretanje: psql -U postgres -d sqleval -f Baza/06_rola_samo_citanje.sql
-- =====================================================================
\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sqleval_citanje') THEN
        CREATE ROLE sqleval_citanje LOGIN PASSWORD 'PROMENI_ME';
    END IF;
END
$$;

-- Bez prava na kreiranje baza i rola.
ALTER ROLE sqleval_citanje NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;

-- Svaki upit se sam prekida posle 5 sekundi, čak i ako aplikacija zaboravi
-- da postavi timeout. Transakcije su podrazumevano read-only.
ALTER ROLE sqleval_citanje SET statement_timeout = '5s';
ALTER ROLE sqleval_citanje SET idle_in_transaction_session_timeout = '10s';
ALTER ROLE sqleval_citanje SET default_transaction_read_only = on;
ALTER ROLE sqleval_citanje SET search_path = prodavnica, fakultet;

-- ------------------------------------------------ šta rola SME da radi
GRANT CONNECT ON DATABASE sqleval TO sqleval_citanje;

GRANT USAGE ON SCHEMA prodavnica TO sqleval_citanje;
GRANT USAGE ON SCHEMA fakultet   TO sqleval_citanje;

GRANT SELECT ON ALL TABLES IN SCHEMA prodavnica TO sqleval_citanje;
GRANT SELECT ON ALL TABLES IN SCHEMA fakultet   TO sqleval_citanje;

-- Isto važi i za tabele koje se naknadno dodaju u ove šeme.
ALTER DEFAULT PRIVILEGES IN SCHEMA prodavnica GRANT SELECT ON TABLES TO sqleval_citanje;
ALTER DEFAULT PRIVILEGES IN SCHEMA fakultet   GRANT SELECT ON TABLES TO sqleval_citanje;

-- --------------------------------------------- šta rola NE SME da radi
-- Radni podaci aplikacije (istorija, rezultati benchmarka) su van domašaja.
REVOKE ALL ON SCHEMA aplikacija FROM sqleval_citanje;
REVOKE ALL ON ALL TABLES IN SCHEMA aplikacija FROM sqleval_citanje;

-- U PostgreSQL-u svaka rola podrazumevano ima prava nad šemom "public".
REVOKE ALL ON SCHEMA public FROM sqleval_citanje;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

COMMENT ON ROLE sqleval_citanje IS
  'Samo za izvršavanje SQL-a generisanog od strane modela — isključivo SELECT nad demo šemama';
