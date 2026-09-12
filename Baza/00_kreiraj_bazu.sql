SELECT 'CREATE DATABASE sqleval'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'sqleval')
\gexec

COMMENT ON DATABASE sqleval IS
  'Text-to-SQL Evaluator — demo baze (prodavnica, fakultet) + radni podaci aplikacije';
