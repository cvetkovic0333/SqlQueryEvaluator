import { api } from "./api.js";
import { $, initTabovi, escapeHtml, poruka } from "./ui.js";
import { postavi, daj } from "./stanje.js";
import { ucitajDashboard } from "./dashboard.js";
import { initBaza, ucitajSemu, osveziInfoIzabranih } from "./baza.js";
import { initUpit } from "./upit.js";
import { initIstorija, osveziIstoriju } from "./istorija.js";

async function start() {
  initTabovi(priPromeniTaba);
  initBaza();
  initUpit();
  initIstorija();

  await ucitajBaze();
  await ucitajModele();

  osveziInfoIzabranih();

  ucitajSemu();
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
  } catch (e) {
    poruka(`Ne mogu da učitam listu baza: ${e.message}`, true);
  }
}

async function ucitajModele() {
  try {
    const podaci = await api.modeli();

    const rang = await rangModela(podaci);
    const pobednik = rang[0] ?? podaci.podrazumevaniModel;
    postavi({ modeli: podaci.modeli, rangLista: rang, aktivniModel: pobednik });

    $("#upit-model").innerHTML = podaci.modeli.map((m) => `
      <option value="${escapeHtml(m.id)}" ${m.imaKljuc ? "" : "disabled"}
              ${m.id === pobednik ? "selected" : ""}>
        ${escapeHtml(m.naziv)}${m.imaKljuc ? "" : " — nema API ključ"}${m.id === pobednik ? " ★" : ""}
      </option>`).join("");

    if (!podaci.spremno) upozoriNaKljuceve();
  } catch (e) {
    poruka(`Ne mogu da učitam listu modela: ${e.message}`, true);
  }
}

function upozoriNaKljuceve() {
  const cilj = $("#tab-baza .baza-desno");
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

async function rangModela(podaci) {
  const upotrebljivi = podaci.modeli.filter((m) => m.imaKljuc).map((m) => m.id);

  try {
    const b = await api.benchmark();
    if (b.imaPodataka && Array.isArray(b.modeli)) {
      const izmereni = b.modeli.map((m) => m.modelId).filter((id) => upotrebljivi.includes(id));
      const ostali = upotrebljivi.filter((id) => !izmereni.includes(id));
      if (izmereni.length > 0) return [...izmereni, ...ostali];
    }
  } catch {
  }

  const podrazumevani = podaci.podrazumevaniModel;
  return upotrebljivi.includes(podrazumevani)
    ? [podrazumevani, ...upotrebljivi.filter((id) => id !== podrazumevani)]
    : upotrebljivi;
}

function priPromeniTaba(tab) {
  const s = daj();
  if (tab === "rezultati") ucitajDashboard();
  if (tab === "istorija") osveziIstoriju();
  if (tab === "baza" && !s.sema) ucitajSemu();
}

start();
