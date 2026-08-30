-- =====================================================================
-- 00_kreiraj_bazu.sql — kreiranje baze "sqleval"
--
-- Pokrenuti povezan na sistemsku bazu "postgres", kao superuser:
--   psql -U postgres -d postgres -f Baza/00_kreiraj_bazu.sql
--
-- Skripta je idempotentna: ako baza već postoji, ne radi ništa i ne puca.
-- PostgreSQL nema "CREATE DATABASE IF NOT EXISTS", pa se koristi psql
-- \gexec — komanda se generiše kao tekst i izvršava samo ako baza fali.
--
-- Napomena: DROP je namerno zakomentarisan da se baza ne bi slučajno
-- obrisala sa svim podacima. Odkomentariši ga samo za potpuno čist start.
-- =====================================================================

-- DROP DATABASE IF EXISTS sqleval;

SELECT 'CREATE DATABASE sqleval'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'sqleval')
\gexec

COMMENT ON DATABASE sqleval IS
  'Text-to-SQL Evaluator — demo baze (prodavnica, fakultet) + radni podaci aplikacije';
