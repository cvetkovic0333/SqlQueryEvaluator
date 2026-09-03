// Tab "Istorija" — prethodni upiti; klik vraća upit u editor.

import { api } from "./api.js";
import { $, escapeHtml, datumFormat, trajanje, brojFormat, poruka } from "./ui.js";
import { ucitajUEditor } from "./upit.js";

export function initIstorija() {
  $("#dugme-osvezi-istoriju").addEventListener("click", () => osveziIstoriju(true));
  $("#dugme-obrisi-istoriju").addEventListener("click", async () => {
    if (!confirm("Obrisati celu istoriju upita?")) return;
    try {
      await api.obrisiIstoriju();
      poruka("Istorija je obrisana.");
      osveziIstoriju(true);
    } catch (e) {
      poruka(e.message, true);
    }
  });
}

export async function osveziIstoriju(prikaziPoruku = false) {
  const kontejner = $("#istorija-lista");

  try {
    const stavke = await api.istorija(50);

    if (stavke.length === 0) {
      kontejner.innerHTML = `
        <div class="prazno">
          <div class="prazno-ikona">
            <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>
          </div>
          <h2>Istorija je prazna</h2>
          <p>Postavi prvo pitanje u tabu „Upit" i ono će se pojaviti ovde.</p>
        </div>`;
      return;
    }

    kontejner.innerHTML = stavke.map((s) => {
      const ocena = s.ocena ?? 0;
      const bojaOcene = ocena >= 4 ? "zelena" : ocena >= 3 ? "zuta" : ocena > 0 ? "crvena" : "";

      return `
        <div class="istorija-stavka" data-id="${s.upitId}">
          <div class="istorija-vrh">
            <span class="istorija-pitanje">${escapeHtml(s.pitanje)}</span>
            ${ocena ? `<span class="znacka ${bojaOcene}">ocena ${ocena}/5</span>` : ""}
            ${s.izvrsen ? `<span class="znacka zelena">izvršen</span>` : `<span class="znacka">nije izvršen</span>`}
            ${s.greska ? `<span class="znacka crvena">odbijen</span>` : ""}
          </div>
          ${s.sql ? `<div class="istorija-sql">${escapeHtml(s.sql)}</div>` : ""}
          <div class="istorija-meta">
            <span>${escapeHtml(s.baza)}</span>
            <span>${s.jezik === "en" ? "engleski" : "srpski"}</span>
            <span>${escapeHtml(s.modelId)}</span>
            ${s.brojRedova !== null && s.brojRedova !== undefined
              ? `<span>${brojFormat(s.brojRedova)} redova</span>` : ""}
            ${s.trajanjeMs ? `<span>${trajanje(s.trajanjeMs)}</span>` : ""}
            <span>${datumFormat(s.kreirano)}</span>
          </div>
        </div>`;
    }).join("");

    kontejner.querySelectorAll(".istorija-stavka").forEach((el) => {
      el.addEventListener("click", () => {
        const stavka = stavke.find((s) => String(s.upitId) === el.dataset.id);
        if (stavka) ucitajUEditor(stavka);
      });
    });

    if (prikaziPoruku) poruka(`Učitano ${stavke.length} upita.`);
  } catch (e) {
    kontejner.innerHTML = `<div class="prazno-malo">Greška: ${escapeHtml(e.message)}</div>`;
  }
}
