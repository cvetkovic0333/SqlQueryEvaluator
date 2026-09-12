param(
    [string]$PsqlPath = "C:\Program Files\PostgreSQL\18\bin\psql.exe",
    [string]$Korisnik = "postgres",
    [string]$Server   = "localhost",
    [string]$Baza     = "sqleval"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PsqlPath)) {
    Write-Error "Ne mogu da nadjem psql na putanji: $PsqlPath`nPokreni skriptu sa -PsqlPath ""putanja\do\psql.exe"""
}

if (-not $env:PGPASSWORD) {
    $tajna = Read-Host -AsSecureString "Lozinka za korisnika '$Korisnik'"
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($tajna))
}
$env:PGCLIENTENCODING = "UTF8"

$koren = Split-Path -Parent $PSScriptRoot
Set-Location $koren

function Pusti-Skriptu([string]$CiljnaBaza, [string]$Fajl) {
    Write-Host "-> $Fajl" -ForegroundColor Cyan
    & $PsqlPath -U $Korisnik -h $Server -w -d $CiljnaBaza -v ON_ERROR_STOP=1 -q -f $Fajl
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Skripta $Fajl je pukla (izlazni kod $LASTEXITCODE)."
    }
}

Pusti-Skriptu "postgres" "Baza/00_kreiraj_bazu.sql"
Pusti-Skriptu $Baza      "Baza/01_prodavnica_sema.sql"
Pusti-Skriptu $Baza      "Baza/02_prodavnica_podaci.sql"
Pusti-Skriptu $Baza      "Baza/03_fakultet_sema.sql"
Pusti-Skriptu $Baza      "Baza/04_fakultet_podaci.sql"
Pusti-Skriptu $Baza      "Baza/05_aplikacija_meta.sql"
Pusti-Skriptu $Baza      "Baza/06_rola_samo_citanje.sql"

Write-Host ""
Write-Host "Gotovo. Broj redova po tabeli:" -ForegroundColor Green
& $PsqlPath -U $Korisnik -h $Server -w -d $Baza -c @"
SELECT table_schema AS sema, table_name AS tabela,
       (xpath('/row/c/text()',
              query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name),
                           false, true, '')))[1]::text::int AS redova
FROM information_schema.tables
WHERE table_schema IN ('prodavnica','fakultet','aplikacija')
ORDER BY table_schema, table_name;
"@
