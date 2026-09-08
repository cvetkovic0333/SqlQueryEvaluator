// Tab "Upit" — pitanje govornim jezikom → SQL → ocena sudije → izvršavanje.

import { api } from "./api.js";
import { $, $$, escapeHtml, nacrtajTabelu, poruka, trajanje, brojFormat, postaviUcitavanje } from "./ui.js";
import { daj, postavi } from "./stanje.js";
import { osveziIstoriju } from "./istorija.js";

const PREDLOZI = {
  prodavnica: {
    sr: [
      "Prikaži 10 kupaca sa najvećom potrošnjom",
      "Koji proizvodi nikada nisu naručeni?",
      "Ukupna zarada po kategoriji u 2024. godini",
      "Proizvodi sa prosečnom ocenom ispod 3",
      "Koliko porudžbina je otkazano po gradovima?",
    ],
    en: [
      "Show the 10 customers with the highest spending",
      "Which products have never been ordered?",
      "Total revenue per category in 2024",
      "Products with an average rating below 3",
      "How many orders were cancelled per city?",
    ],
  },
  fakultet: {
    sr: [
      "Koji smer ima najbolji prosek ocena?",
      "Studenti koji nisu položili nijedan ispit",
      "Prosečna ocena po predmetu u januarskom roku",
      "Koliko studenata je upisano po godinama?",
      "Profesori sa najviše predmeta",
    ],
    en: [
      "Which study program has the best average grade?",
      "Students who have not passed a single exam",
      "Average grade per course in the January exam period",
      "How many students enrolled per year?",
      "Professors teaching the most courses",
    ],
  },
};

export function initUpit() {
  $("#dugme-prevedi").addEventListener("click", prevedi);
  $("#dugme-izvrsi").addEventListener("click", izvrsi);
  $("#dugme-kopiraj").addEventListener("click", kopiraj);

  $("#upit-model").addEventListener("change", (e) => postavi({ aktivniModel: e.target.value }));

  $$("#prekidac-jezik .prekidac-opcija").forEach((dugme) => {
    dugme.addEventListener("click", () => {
      $$("#prekidac-jezik .prekidac-opcija")
        .forEach((d) => d.classList.toggle("is-active", d === dugme));
      postavi({ jezik: dugme.dataset.jezik });
      nacrtajPredloge();
    });
  });

  // Čim korisnik krene da kuca novo pitanje, prethodni odgovor se sklanja.
  // Inače na ekranu stoji SQL i rezultat starog pitanja, pa deluje kao da
  // se odnose na ono što se upravo kuca.
  $("#upit-tekst").addEventListener("input", ocistiPrethodniOdgovor);

  // Ctrl+Enter šalje upit — brže od traženja dugmeta mišem.
  $("#upit-tekst").addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      prevedi();
    }
  });

  nacrtajPredloge();
}

export function nacrtajPredloge() {
  const s = daj();
  const lista = PREDLOZI[s.aktivnaBaza]?.[s.jezik] || [];
  $("#predlozi").innerHTML = lista
    .map((p) => `<button class="predlog">${escapeHtml(p)}</button>`)
    .join("");

  $$("#predlozi .predlog").forEach((el) => {
    el.addEventListener("click", () => {
      $("#upit-tekst").value = el.textContent;
      $("#upit-tekst").focus();
    });
  });
}

async function prevedi() {
  const s = daj();
  const pitanje = $("#upit-tekst").value.trim();

  if (!pitanje) {
    poruka("Prvo napiši pitanje.", true);
    $("#upit-tekst").focus();
    return;
  }

  const dugme = $("#dugme-prevedi");
  const status = $("#upit-status");
  postaviUcitavanje(dugme, true, "Prevedi u SQL");
  status.className = "upit-status";
  status.textContent = "Model generiše SQL, pa ga sudija ocenjuje…";

  $("#kartica-rezultat").classList.add("hidden");

  // Modeli koji su danas iscrpeli kvotu se preskaču, po redosledu tačnosti
  // iz merenja. Bez ovoga korisnik dobije grešku i mora sam da bira drugi
  // model, iako aplikacija već zna koji je sledeći najbolji.
  const preskoceni = [];

  try {
    for (const modelId of redosledPokusaja()) {
      try {
        const odgovor = await api.prevedi({
          pitanje,
          jezik: s.jezik,
          baza: s.aktivnaBaza,
          tabele: [...s.izabraneTabele],
          modelId,
        });

        if (modelId !== s.aktivniModel) {
          postavi({ aktivniModel: modelId });
          $("#upit-model").value = modelId;
          poruka(`${imeModela(preskoceni[0])} je iscrpeo dnevnu kvotu — prešao sam na ${imeModela(modelId)}.`);
        }

        postavi({ poslednjiPrevod: odgovor });
        prikaziPrevod(odgovor);
        status.textContent = "";
        osveziIstoriju();
        return;
      } catch (e) {
        if (!e.kvotaIscrpljena) throw e;
        preskoceni.push(modelId);
        status.textContent = `${imeModela(modelId)} nema kvote — probam sledeći model…`;
      }
    }

    // Nijedan model nije uspeo.
    status.className = "upit-status greska";
    status.textContent = preskoceni.length > 1
      ? `Svi modeli su iscrpeli dnevnu kvotu (${preskoceni.map(imeModela).join(", ")}). Pokušaj sutra.`
      : "Model je iscrpeo dnevnu kvotu i nema zamene sa podešenim ključem.";
    $("#kartica-sql").classList.add("hidden");
  } catch (e) {
    status.className = "upit-status greska";
    status.textContent = e.message;
    $("#kartica-sql").classList.add("hidden");
  } finally {
    postaviUcitavanje(dugme, false, "Prevedi u SQL");
  }
}

/** Vraća prikaz na početno stanje: bez generisanog SQL-a i bez rezultata. */
function ocistiPrethodniOdgovor() {
  if ($("#kartica-sql").classList.contains("hidden")
      && $("#kartica-rezultat").classList.contains("hidden")) return;

  $("#kartica-sql").classList.add("hidden");
  $("#kartica-rezultat").classList.add("hidden");
  $("#upit-status").textContent = "";
  $("#upit-status").className = "upit-status";
  $("#izvrsi-status").textContent = "";
  $("#izvrsi-status").className = "upit-status";
  postavi({ poslednjiPrevod: null });
}

/** Izabrani model prvi, pa ostali po tačnosti iz merenja. */
function redosledPokusaja() {
  const s = daj();
  const rang = s.rangLista?.length ? s.rangLista : s.modeli.filter((m) => m.imaKljuc).map((m) => m.id);
  const izabrani = s.aktivniModel;
  return izabrani ? [izabrani, ...rang.filter((id) => id !== izabrani)] : rang;
}

function imeModela(id) {
  return daj().modeli.find((m) => m.id === id)?.naziv ?? id;
}

function prikaziPrevod(o) {
  $("#kartica-sql").classList.remove("hidden");

  $("#sql-meta").textContent =
    `${o.modelId} · ${trajanje(o.trajanjeMs)} · ${o.ulazniTokeni + o.izlazniTokeni} tokena`;

  // Sanitizer je odbio upit — prikazuje se sirov odgovor modela i razlog,
  // a dugme za izvršavanje se gasi.
  if (!o.bezbedan) {
    $("#sql-prikaz").textContent = o.sirovOdgovor || "(prazan odgovor)";
    $("#ocena-znacka").className = "ocena-znacka ocena-losa";
    $("#ocena-znacka").innerHTML = `<span>odbijeno</span>`;
    $("#obrazlozenje").className = "obrazlozenje opasnost";
    $("#obrazlozenje").textContent = `Upit je odbijen pre izvršavanja: ${o.razlogOdbijanja}`;
    $("#dugme-izvrsi").disabled = true;
    return;
  }

  $("#sql-prikaz").textContent = o.sql;
  $("#dugme-izvrsi").disabled = false;

  const ocena = o.ocena?.vrednost ?? 0;
  const klasa = ocena >= 4 ? "ocena-dobra" : ocena >= 3 ? "ocena-srednja" : "ocena-losa";

  if (o.ocena) {
    $("#ocena-znacka").className = `ocena-znacka ${klasa}`;
    $("#ocena-znacka").innerHTML =
      `<span class="broj">${ocena}</span><span>/ 5 &nbsp;· sudija</span>`;

    $("#obrazlozenje").className = o.smeOdmahDaSeIzvrsi ? "obrazlozenje" : "obrazlozenje upozorenje";
    $("#obrazlozenje").innerHTML = escapeHtml(o.ocena.obrazlozenje || "Sudija nije dao obrazloženje.")
      + (o.smeOdmahDaSeIzvrsi
        ? ""
        : `<br><br><b>Ocena je ispod praga za automatsko izvršavanje.</b>
           Pregledaj SQL pre nego što ga pustiš.`);
  } else {
    $("#ocena-znacka").className = "ocena-znacka";
    $("#ocena-znacka").innerHTML = "<span>bez ocene</span>";
    $("#obrazlozenje").className = "obrazlozenje";
    $("#obrazlozenje").textContent = "Sudija nije podešen (TextToSql:JudgeModelId).";
  }
}

async function izvrsi() {
  const s = daj();
  const o = s.poslednjiPrevod;
  if (!o?.sql) return;

  const ocena = o.ocena?.vrednost ?? 0;
  if (!o.smeOdmahDaSeIzvrsi && ocena > 0) {
    const potvrda = confirm(
      `Sudija je ovaj upit ocenio sa ${ocena}/5.\n\n` +
      `${o.ocena?.obrazlozenje || ""}\n\nSvejedno izvršiti upit?`);
    if (!potvrda) return;
  }

  const dugme = $("#dugme-izvrsi");
  const status = $("#izvrsi-status");
  postaviUcitavanje(dugme, true, "Izvrši upit");
  status.className = "upit-status";
  status.textContent = "";

  try {
    const r = await api.izvrsi({ sql: o.sql, upitId: o.upitId });

    $("#kartica-rezultat").classList.remove("hidden");
    nacrtajTabelu($("#tabela-rezultat"), r.kolone, r.redovi);
    $("#rezultat-meta").textContent =
      `${brojFormat(r.brojRedova)} ${r.odsecen ? "prikazanih (rezultat je odsečen)" : "redova"} · ${trajanje(r.trajanjeMs)}`;

    status.className = "upit-status ok";
    status.textContent = "Upit je izvršen.";
    osveziIstoriju();
  } catch (e) {
    status.className = "upit-status greska";
    status.textContent = e.message;
    $("#kartica-rezultat").classList.add("hidden");
  } finally {
    postaviUcitavanje(dugme, false, "Izvrši upit");
  }
}

async function kopiraj() {
  const sql = $("#sql-prikaz").textContent;
  try {
    await navigator.clipboard.writeText(sql);
    poruka("SQL je kopiran.");
  } catch {
    poruka("Kopiranje nije uspelo — označi tekst ručno.", true);
  }
}

/** Poziva se iz istorije: vraća stari upit u editor u tabu "Baza i upit". */
export function ucitajUEditor(stavka) {
  $("#upit-tekst").value = stavka.pitanje;
  if (stavka.baza) {
    postavi({ aktivnaBaza: stavka.baza });
    $("#izbor-baze").value = stavka.baza;
  }
  $$("#prekidac-jezik .prekidac-opcija").forEach((d) =>
    d.classList.toggle("is-active", d.dataset.jezik === stavka.jezik));
  postavi({ jezik: stavka.jezik });
  nacrtajPredloge();

  $$(".tab").forEach((t) => t.classList.toggle("is-active", t.dataset.tab === "baza"));
  $$(".panel").forEach((p) => p.classList.toggle("is-active", p.id === "tab-baza"));
  $("#upit-tekst").focus();
}
