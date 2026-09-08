// Tanak omotač oko fetch-a ka /api/*. Sve greške se svode na Error sa
// porukom koju server pošalje, da bi interfejs uvek imao šta da prikaže.

async function zahtev(putanja, opcije = {}) {
  const odgovor = await fetch(putanja, {
    headers: { "Content-Type": "application/json" },
    ...opcije,
  });

  const tekst = await odgovor.text();
  let telo = null;
  try {
    telo = tekst ? JSON.parse(tekst) : null;
  } catch {
    telo = { greska: tekst };
  }

  if (!odgovor.ok) {
    const poruka = telo?.greska || telo?.detail || telo?.title || `HTTP ${odgovor.status}`;
    const g = new Error(poruka);
    g.telo = telo;
    g.kvotaIscrpljena = telo?.kvotaIscrpljena === true;
    g.status = odgovor.status;
    throw g;
  }

  return telo;
}

export const api = {
  modeli: () => zahtev("/api/modeli"),
  baze: () => zahtev("/api/baze"),
  sema: (baza) => zahtev(`/api/sema/${encodeURIComponent(baza)}`),
  pregled: (baza, tabela, limit = 50, offset = 0) =>
    zahtev(`/api/sema/${encodeURIComponent(baza)}/${encodeURIComponent(tabela)}/pregled`
           + `?limit=${limit}&offset=${offset}`),

  prevedi: (telo) => zahtev("/api/prevedi", { method: "POST", body: JSON.stringify(telo) }),
  izvrsi: (telo) => zahtev("/api/izvrsi", { method: "POST", body: JSON.stringify(telo) }),

  istorija: (limit = 50) => zahtev(`/api/istorija?limit=${limit}`),
  obrisiIstoriju: () => zahtev("/api/istorija", { method: "DELETE" }),

  benchmark: () => zahtev("/api/benchmark/pregled"),
};
