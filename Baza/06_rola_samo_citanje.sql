\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sqleval_citanje') THEN
        CREATE ROLE sqleval_citanje LOGIN PASSWORD 'PROMENI_ME';
    END IF;
END
$$;

ALTER ROLE sqleval_citanje NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;

ALTER ROLE sqleval_citanje SET statement_timeout = '5s';
ALTER ROLE sqleval_citanje SET idle_in_transaction_session_timeout = '10s';
ALTER ROLE sqleval_citanje SET default_transaction_read_only = on;
ALTER ROLE sqleval_citanje SET search_path = prodavnica, fakultet;

GRANT CONNECT ON DATABASE sqleval TO sqleval_citanje;

GRANT USAGE ON SCHEMA prodavnica TO sqleval_citanje;
GRANT USAGE ON SCHEMA fakultet   TO sqleval_citanje;

GRANT SELECT ON ALL TABLES IN SCHEMA prodavnica TO sqleval_citanje;
GRANT SELECT ON ALL TABLES IN SCHEMA fakultet   TO sqleval_citanje;

ALTER DEFAULT PRIVILEGES IN SCHEMA prodavnica GRANT SELECT ON TABLES TO sqleval_citanje;
ALTER DEFAULT PRIVILEGES IN SCHEMA fakultet   GRANT SELECT ON TABLES TO sqleval_citanje;

REVOKE ALL ON SCHEMA aplikacija FROM sqleval_citanje;
REVOKE ALL ON ALL TABLES IN SCHEMA aplikacija FROM sqleval_citanje;

REVOKE ALL ON SCHEMA public FROM sqleval_citanje;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

COMMENT ON ROLE sqleval_citanje IS
  'Samo za izvršavanje SQL-a generisanog od strane modela — isključivo SELECT nad demo šemama';
