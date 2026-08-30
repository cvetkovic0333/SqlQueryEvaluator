-- =====================================================================
-- 02_seed.sql — deterministicki test podaci (setseed + generate_series)
-- Determinizam je bitan: rezultati benchmarka moraju biti ponovljivi.
-- Pokretanje: psql -U postgres -d sqleval -f db/02_seed.sql
-- =====================================================================

-- TODO(Faza 1): INSERT ... SELECT generate_series(...)
