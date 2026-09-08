// Tab "Baza" — učitavanje šeme, izbor tabela i pregled sadržaja.

import { api } from "./api.js";
import { $, escapeHtml, brojFormat, nacrtajTabelu, poruka, trajanje } from "./ui.js";
import { daj, postavi, prebaciTabelu } from "./stanje.js";

let aktivnaTabela = null;

export function initBaza() {
  $("#dugme-ucitaj-bazu").addEventListener("click", () => ucitajSemu(true));
  $("#izbor-baze").addEventListener("change", (e) => {
    postavi({ aktivnaBaza: e.target.value, sema: null, izabraneTabele: new Set() });
    ocistiPrikaz();
  });
}

function ocistiPrikaz() {
  aktivnaTabela = null;
  $("#tabele-lista").innerHTML = `<div class="prazno-malo">Klikni „Učitaj bazu" da vidiš tabele.</div>`;
  $("#tabela-detalj").innerHTML = `<div class="prazno-malo">Klikni na tabelu sa leve strane da vidiš njen sadržaj.</div>`;
  $("#baza-info").textContent = "";
}

export async function ucitajSemu(prikaziPoruku = false) {
  const s = daj();
  const baza = s.aktivnaBaza;
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
    <div class="tabela-stavka" data-tabela="${escapeHtml(t.naziv)}">
      <input type="checkbox" data-cek="${escapeHtml(t.naziv)}"
             ${s.izabraneTabele.has(t.naziv) ? "checked" : ""}
             title="Uključi tabelu u kontekst upita">
      <span class="tabela-stavka-ime">${escapeHtml(t.naziv)}</span>
      <span class="tabela-stavka-broj">${brojFormat(t.brojRedova)}</span>
    </div>`).join("");

  $("#tabele-lista").querySelectorAll(".tabela-stavka").forEach((el) => {
    el.addEventListener("click", (dogadjaj) => {
      const naziv = el.dataset.tabela;

      // Klik na kvadratić bira tabelu za kontekst upita;
      // klik bilo gde drugde otvara pregled sadržaja.
      if (dogadjaj.target.matches("input[type=checkbox]")) {
        prebaciTabelu(naziv);
        osveziInfoIzabranih();
        return;
      }

      $("#tabele-lista").querySelectorAll(".tabela-stavka")
        .forEach((x) => x.classList.toggle("is-active", x === el));
      prikaziTabelu(naziv);
    });
  });
}

export function osveziInfoIzabranih() {
  const s = daj();
  const el = $("#izabrane-tabele-info");
  if (!el) return;

  if (s.izabraneTabele.size === 0) {
    el.textContent = "Nije izabrana nijedna tabela — model dobija celu šemu.";
  } else {
    el.innerHTML = `Kontekst upita: ${[...s.izabraneTabele]
      .map((t) => `<span class="znacka plava">${escapeHtml(t)}</span>`).join(" ")}`;
  }
}

async function prikaziTabelu(naziv) {
  const s = daj();
  aktivnaTabela = naziv;
  const tabela = s.sema.tabele.find((t) => t.naziv === naziv);
  if (!tabela) return;

  const kolone = tabela.kolone.map((k) => {
    const fk = tabela.straniKljucevi.find((f) => f.kolona === k.naziv);
    const klase = ["kolona-znacka", k.primarniKljuc ? "pk" : "", fk ? "fk" : ""].filter(Boolean).join(" ");
    const naslov = [
      k.opis || "",
      k.primarniKljuc ? "primarni ključ" : "",
      fk ? `strani ključ → ${fk.ciljnaTabela}.${fk.ciljnaKolona}` : "",
      k.mozeBitiNull ? "može biti NULL" : "obavezno polje",
    ].filter(Boolean).join(" · ");

    return `<span class="${klase}" title="${escapeHtml(naslov)}">
      ${escapeHtml(k.naziv)}<span class="tip">${escapeHtml(k.tip)}</span>
    </span>`;
  }).join("");

  $("#tabela-detalj").innerHTML = `
    <div class="kartica">
      <div class="kartica-zaglavlje">
        <h3>${escapeHtml(tabela.naziv)}</h3>
        <span class="kartica-opis">
          ${escapeHtml(tabela.opis || "")}
          ${tabela.opis ? " · " : ""}${brojFormat(tabela.brojRedova)} redova
        </span>
      </div>
      <div class="kolone-lista">${kolone}</div>
      <div class="tabela-okvir">
        <table class="tabela" id="tabela-pregled"></table>
      </div>
      <div class="kartica-opis" id="pregled-meta" style="margin-top:10px"></div>
    </div>`;

  $("#tabela-pregled").innerHTML =
    `<tbody><tr><td class="null"><span class="vrtenje"></span> Učitavam podatke…</td></tr></tbody>`;

  try {
    const pregled = await api.pregled(s.aktivnaBaza, naziv, 50);
    if (aktivnaTabela !== naziv) return; // korisnik je u međuvremenu kliknuo drugu tabelu

    nacrtajTabelu($("#tabela-pregled"), pregled.kolone, pregled.redovi);
    $("#pregled-meta").textContent =
      `Prikazano prvih ${pregled.redovi.length} od ${brojFormat(pregled.ukupnoRedova)} redova · ${trajanje(pregled.trajanjeMs)}`;
  } catch (e) {
    $("#tabela-pregled").innerHTML =
      `<tbody><tr><td class="null">Greška: ${escapeHtml(e.message)}</td></tr></tbody>`;
  }
}
