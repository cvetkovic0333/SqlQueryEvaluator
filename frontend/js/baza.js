// Tab "Baza i upit" — leva strana lista tabela, desna prikaz podataka.
//
// Čekiranje tabele ODMAH učitava njen sadržaj; ne postoji korak "pa sad
// klikni negde drugde". Više čekiranih tabela znači više prikazanih.

import { api } from "./api.js";
import { $, escapeHtml, brojFormat, nacrtajTabelu, poruka, trajanje } from "./ui.js";
import { daj, postavi } from "./stanje.js";

const KORAK = 50;

// Koliko je redova do sada učitano za svaku prikazanu tabelu.
const ucitano = new Map();

export function initBaza() {
  $("#izbor-baze").addEventListener("change", (e) => {
    postavi({ aktivnaBaza: e.target.value, sema: null, izabraneTabele: new Set() });
    ucitano.clear();
    $("#tabela-detalj").innerHTML = "";
    ucitajSemu();
  });
}

export async function ucitajSemu(prikaziPoruku = false) {
  const baza = daj().aktivnaBaza;
  if (!baza) return;

  $("#tabele-lista").innerHTML = `<div class="prazno-malo"><span class="vrtenje"></span> Učitavam…</div>`;

  try {
    const sema = await api.sema(baza);
    postavi({ sema });

    $("#baza-info").textContent =
      `${sema.tabele.length} tabela · ${brojFormat(sema.ukupnoRedova)} redova`;

    nacrtajListuTabela(sema);
    if (prikaziPoruku) poruka(`Učitana baza „${sema.prikaznoIme}"`);
  } catch (e) {
    $("#tabele-lista").innerHTML = `<div class="prazno-malo">Greška: ${escapeHtml(e.message)}</div>`;
    poruka(`Ne mogu da učitam bazu: ${e.message}`, true);
  }
}

function nacrtajListuTabela(sema) {
  const s = daj();

  $("#tabele-lista").innerHTML = sema.tabele.map((t) => `
    <label class="tabela-stavka" for="cek-${escapeHtml(t.naziv)}">
      <input type="checkbox" id="cek-${escapeHtml(t.naziv)}" data-tabela="${escapeHtml(t.naziv)}"
             ${s.izabraneTabele.has(t.naziv) ? "checked" : ""}>
      <span class="tabela-stavka-ime">${escapeHtml(t.naziv)}</span>
      <span class="tabela-stavka-broj">${brojFormat(t.brojRedova)}</span>
    </label>`).join("");

  $("#tabele-lista").querySelectorAll("input[type=checkbox]").forEach((el) => {
    el.addEventListener("change", () => {
      const naziv = el.dataset.tabela;
      const stanje = daj();

      if (el.checked) stanje.izabraneTabele.add(naziv);
      else stanje.izabraneTabele.delete(naziv);

      postavi({});
      osveziInfoIzabranih();
      osveziPrikaz();
    });
  });

  osveziInfoIzabranih();
  osveziPrikaz();
}

export function osveziInfoIzabranih() {
  const s = daj();
  const el = $("#izabrane-tabele-info");
  if (!el) return;

  el.innerHTML = s.izabraneTabele.size === 0
    ? "Nije izabrana nijedna tabela — model dobija celu šemu."
    : `Kontekst upita: ${[...s.izabraneTabele]
        .map((t) => `<span class="znacka plava">${escapeHtml(t)}</span>`).join(" ")}`;
}

/** Prikazuje po jednu karticu za svaku čekiranu tabelu. */
function osveziPrikaz() {
  const s = daj();
  const kontejner = $("#tabela-detalj");
  const izabrane = [...s.izabraneTabele];

  if (izabrane.length === 0) {
    ucitano.clear();
    kontejner.innerHTML =
      `<div class="prazno-malo">Čekiraj tabelu sa leve strane da vidiš njen sadržaj.</div>`;
    return;
  }

  // Kartice koje više nisu čekirane se uklanjaju, a postojeće se ne diraju
  // da se ne izgubi ono što je korisnik već dolistao.
  [...ucitano.keys()].forEach((naziv) => {
    if (!s.izabraneTabele.has(naziv)) {
      ucitano.delete(naziv);
      kontejner.querySelector(`[data-kartica="${naziv}"]`)?.remove();
    }
  });

  kontejner.querySelector(".prazno-malo")?.remove();

  izabrane.forEach((naziv) => {
    if (ucitano.has(naziv)) return;

    const tabela = s.sema?.tabele.find((t) => t.naziv === naziv);
    ucitano.set(naziv, 0);

    kontejner.insertAdjacentHTML("beforeend", `
      <div class="kartica" data-kartica="${escapeHtml(naziv)}">
        <div class="kartica-zaglavlje sa-akcijom">
          <div>
            <h3>${escapeHtml(naziv)}</h3>
            <span class="kartica-opis">
              ${escapeHtml(tabela?.opis || "")}${tabela?.opis ? " · " : ""}${brojFormat(tabela?.brojRedova ?? 0)} redova
            </span>
          </div>
          <span class="kartica-opis" data-brojac="${escapeHtml(naziv)}"></span>
        </div>
        <div class="tabela-okvir">
          <table class="tabela" data-grid="${escapeHtml(naziv)}">
            <tbody><tr><td class="null"><span class="vrtenje"></span> Učitavam…</td></tr></tbody>
          </table>
        </div>
        <div class="ucitaj-jos" data-podnozje="${escapeHtml(naziv)}"></div>
      </div>`);

    ucitajStranu(naziv, 0);
  });
}

/**
 * Dovlači sledećih 50 redova i dodaje ih na postojeće, umesto da ih zameni —
 * korisnik tako listanjem dolazi do kraja tabele bez gubljenja prethodnog.
 */
async function ucitajStranu(naziv, offset) {
  const s = daj();
  const grid = document.querySelector(`[data-grid="${naziv}"]`);
  const podnozje = document.querySelector(`[data-podnozje="${naziv}"]`);
  const brojac = document.querySelector(`[data-brojac="${naziv}"]`);
  if (!grid) return;

  try {
    const p = await api.pregled(s.aktivnaBaza, naziv, KORAK, offset);
    if (!document.querySelector(`[data-grid="${naziv}"]`)) return; // odčekirana u međuvremenu

    if (offset === 0) {
      nacrtajTabelu(grid, p.kolone, p.redovi);
    } else {
      dodajRedove(grid, p.redovi);
    }

    const ukupnoPrikazano = offset + p.redovi.length;
    ucitano.set(naziv, ukupnoPrikazano);

    brojac.textContent =
      `prikazano ${brojFormat(ukupnoPrikazano)} od ${brojFormat(p.ukupnoRedova)} · ${trajanje(p.trajanjeMs)}`;

    podnozje.innerHTML = p.imaJos
      ? `<button class="dugme dugme-tiho" data-jos="${escapeHtml(naziv)}">
           Učitaj sledećih ${Math.min(KORAK, p.ukupnoRedova - ukupnoPrikazano)}
         </button>`
      : `<span class="kartica-opis">Prikazana cela tabela.</span>`;

    const dugme = podnozje.querySelector("button");
    if (dugme) {
      dugme.addEventListener("click", () => {
        dugme.disabled = true;
        dugme.innerHTML = `<span class="vrtenje"></span> Učitavam…`;
        ucitajStranu(naziv, ucitano.get(naziv));
      });
    }
  } catch (e) {
    grid.innerHTML = `<tbody><tr><td class="null">Greška: ${escapeHtml(e.message)}</td></tr></tbody>`;
    podnozje.innerHTML = "";
  }
}

function dodajRedove(grid, redovi) {
  const telo = grid.querySelector("tbody");
  if (!telo) return;

  telo.insertAdjacentHTML("beforeend", redovi.map((red) => `<tr>${red.map((v) => {
    if (v === null || v === undefined) return `<td class="null">NULL</td>`;
    if (typeof v === "number") return `<td class="broj">${escapeHtml(v)}</td>`;
    if (typeof v === "boolean") return `<td>${v ? "da" : "ne"}</td>`;
    return `<td>${escapeHtml(v)}</td>`;
  }).join("")}</tr>`).join(""));
}
