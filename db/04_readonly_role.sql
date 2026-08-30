-- =====================================================================
-- 04_readonly_role.sql — rola sqleval_ro (SELECT-only nad schema shop)
-- Prvi i primarni sloj zastite pri izvrsavanju SQL-a koji je generisao LLM.
-- Sanitizer u aplikaciji je drugi sloj.
-- Pokretanje: psql -U postgres -d sqleval -f db/04_readonly_role.sql
-- =====================================================================

-- TODO(Faza 1): CREATE ROLE / GRANT / statement_timeout
