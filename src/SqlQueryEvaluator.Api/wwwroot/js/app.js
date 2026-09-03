// Ulazna tačka frontenda: učita registar modela i listu baza, pa poveže tabove.

import { api } from "./api.js";
import { $, initTabovi, escapeHtml, poruka } from "./ui.js";
import { postavi, daj } from "./stanje.js";
import { ucitajDashboard } from "./dashboard.js";
import { initBaza, ucitajSemu, osveziInfoIzabranih } from "./baza.js";
import { initUpit, nacrtajPredloge } from "./upit.js";
import { initIstorija, osveziIstoriju } from "./istorija.js";

async function start() {
  initTabovi(priPromeniTaba);
  initBaza();
  initUpit();
  initIstorija();

  await Promise.all([ucitajBaze(), ucitajModele()]);

  osveziInfoIzabranih();
  nacrtajPredloge();
  ucitajDashboard();
}

async function ucitajBaze() {
  try {
    const baze = await api.baze();
    postavi({ baze, aktivnaBaza: baze[0]?.naziv ?? null });

    const opcije = baze
      .map((b) => `<option value="${escapeHtml(b.naziv)}">${escapeHtml(b.prikaznoIme)}</option>`)
      .join("");

    $("#izbor-baze").innerHTML = opcije;
    $("#upit-baza").innerHTML = opcije;
  } catch (e) {
    poruka(`Ne mogu da učitam listu baza: ${e.message}`, true);
  }
}

async function ucitajModele() {
  try {
    const podaci = await api.modeli();
    postavi({ modeli: podaci.modeli, aktivniModel: podaci.podrazumevaniModel });

    const podrazumevani = podaci.modeli.find((m) => m.id === podaci.podrazumevaniModel);
    const sudija = podaci.modeli.find((m) => m.id === podaci.modelSudije);

    $("#pill-model-value").textContent = podrazumevani?.naziv ?? "nije podešen";
    $("#pill-sudija-value").textContent = sudija?.naziv ?? "nije podešen";

    // Model bez ključa se vidi u listi, ali je jasno označen i ne može da se
    // izabere — bolje nego da poziv pukne tek kada korisnik pritisne dugme.
    $("#upit-model").innerHTML = podaci.modeli.map((m) => `
      <option value="${escapeHtml(m.id)}" ${m.imaKljuc ? "" : "disabled"}
              ${m.id === podaci.podrazumevaniModel ? "selected" : ""}>
        ${escapeHtml(m.naziv)}${m.imaKljuc ? "" : " — nema API ključ"}
      </option>`).join("");

    if (!podaci.spremno) {
      $("#pill-model").classList.add("hidden");
      $("#pill-sudija").classList.add("hidden");
      upozoriNaKljuceve();
    }
  } catch (e) {
    poruka(`Ne mogu da učitam listu modela: ${e.message}`, true);
  }
}

/**
 * Nijedan model nema ključ — aplikacija radi, ali ne može da prevodi.
 * Umesto tihe greške pri prvom kliku, upozorenje stoji odmah u tabu "Upit".
 */
function upozoriNaKljuceve() {
  const cilj = $("#tab-upit .upit-raspored");
  cilj.insertAdjacentHTML("afterbegin", `
    <div class="kartica" style="border-color:var(--zuta)">
      <div class="kartica-zaglavlje">
        <h3 style="color:var(--zuta)">Nije podešen nijedan API ključ</h3>
        <span class="kartica-opis">
          Bez ključa aplikacija može da učita i prikaže bazu, ali ne može da prevede pitanje u SQL.
        </span>
      </div>
      <pre class="kod-blok">dotnet user-secrets set "ApiKeys:Groq" "gsk_..." --project src/SqlQueryEvaluator.Api</pre>
      <div class="kartica-opis" style="margin-top:10px">
        Isto važi i za <code>ApiKeys:Gemini</code>, <code>ApiKeys:OpenRouter</code>
        i <code>ApiKeys:Mistral</code>. Posle podešavanja restartuj aplikaciju.
      </div>
    </div>`);
}

function priPromeniTaba(tab) {
  const s = daj();
  if (tab === "rezultati") ucitajDashboard();
  if (tab === "istorija") osveziIstoriju();
  if (tab === "baza" && !s.sema) ucitajSemu();
}

start();
