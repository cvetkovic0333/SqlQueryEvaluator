import { api } from "./api.js";
import { $, procenat, trajanje, brojFormat, escapeHtml, poruka } from "./ui.js";

const BOJE = ["#5b8cff", "#3ecf8e", "#f0b429", "#a78bfa", "#2dd4bf", "#f2555a", "#fb923c", "#38bdf8", "#e879f9"];

let grafikoni = {};
let ucitano = false;

Chart.defaults.color = "#9aa5bd";
Chart.defaults.borderColor = "rgba(38,46,64,.75)";
Chart.defaults.font.family = '"Segoe UI", system-ui, sans-serif';
Chart.defaults.font.size = 12;

function unisti() {
  Object.values(grafikoni).forEach((g) => g?.destroy());
  grafikoni = {};
}

const osnovneOpcije = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { labels: { boxWidth: 12, boxHeight: 12, padding: 14, usePointStyle: true } },
    tooltip: {
      backgroundColor: "#1c2231",
      borderColor: "#35405a",
      borderWidth: 1,
      padding: 11,
      cornerRadius: 8,
      titleColor: "#e8ecf4",
      bodyColor: "#9aa5bd",
    },
  },
};

const osaProcenat = {
  beginAtZero: true,
  max: 100,
  ticks: { callback: (v) => `${v}%` },
  grid: { color: "rgba(38,46,64,.6)" },
};

export async function ucitajDashboard(naSilu = false) {
  if (ucitano && !naSilu) return;

  let podaci;
  try {
    podaci = await api.benchmark();
  } catch (e) {
    poruka(`Ne mogu da učitam rezultate: ${e.message}`, true);
    return;
  }

  ucitano = true;

  if (!podaci.imaPodataka) {
    $("#rezultati-prazno").classList.remove("hidden");
    $("#rezultati-sadrzaj").classList.add("hidden");
    return;
  }

  $("#rezultati-prazno").classList.add("hidden");
  $("#rezultati-sadrzaj").classList.remove("hidden");

  unisti();
  prikaziPobednika(podaci);
  prikaziKpi(podaci);
  grafModeli(podaci);
  grafTezina(podaci);
  grafJezik(podaci);
  grafBrzina(podaci);
  grafSudija(podaci);
  prikaziSlaganje(podaci);
  prikaziTabelu(podaci);
}

function prikaziPobednika(p) {
  const najbolji = p.modeli.find((m) => m.modelId === p.pobednik) || p.modeli[0];
  if (!najbolji) return;

  $("#pobednik-ime").textContent = najbolji.naziv;
  $("#pobednik-metrike").innerHTML = `
    <div><b>${procenat(najbolji.tacnost)}</b>tačnost rezultata</div>
    <div><b>${procenat(najbolji.validSql)}</b>ispravan SQL</div>
    <div><b>${najbolji.ocenaSudije.toFixed(2)}</b>prosečna ocena sudije</div>
    <div><b>${trajanje(najbolji.trajanjeMs)}</b>prosečno po upitu</div>
    <div><b>${brojFormat(najbolji.broj)}</b>testiranih upita</div>`;

  if (p.podesenModel && p.podesenModel !== p.pobednik) {
    const podesen = p.modeli.find((m) => m.modelId === p.podesenModel);
    $("#pobednik-metrike").insertAdjacentHTML("beforeend",
      `<div style="color:var(--zuta)"><b>⚠</b>aplikacija koristi
       ${escapeHtml(podesen ? podesen.naziv : p.podesenModel)}</div>`);
  }
}

function prikaziKpi(p) {
  const prosecnaTacnost = p.modeli.reduce((s, m) => s + m.tacnost, 0) / p.modeli.length;
  const najbrzi = [...p.modeli].sort((a, b) => a.trajanjeMs - b.trajanjeMs)[0];
  const s = p.slaganjeSudije;

  $("#kpi-red").innerHTML = `
    <div class="kpi">
      <div class="kpi-oznaka">Testirano modela</div>
      <div class="kpi-vrednost">${p.modeli.length}</div>
      <div class="kpi-nota">${brojFormat(p.ukupnoPoziva)} poziva ukupno</div>
    </div>
    <div class="kpi">
      <div class="kpi-oznaka">Prosečna tačnost</div>
      <div class="kpi-vrednost">${procenat(prosecnaTacnost)}</div>
      <div class="kpi-nota">preko svih modela</div>
    </div>
    <div class="kpi">
      <div class="kpi-oznaka">Najbrži model</div>
      <div class="kpi-vrednost" style="font-size:17px">${escapeHtml(najbrzi.naziv)}</div>
      <div class="kpi-nota">${trajanje(najbrzi.trajanjeMs)} po upitu</div>
    </div>
    <div class="kpi">
      <div class="kpi-oznaka">Slaganje sudije</div>
      <div class="kpi-vrednost">${procenat(s.procenat)}</div>
      <div class="kpi-nota">κ = ${s.kappa} — ${escapeHtml(s.opisKappe)}</div>
    </div>`;
}

function grafModeli(p) {
  grafikoni.modeli = new Chart($("#graf-modeli"), {
    type: "bar",
    data: {
      labels: p.modeli.map((m) => m.naziv),
      datasets: [
        {
          label: "Tačnost rezultata",
          data: p.modeli.map((m) => m.tacnost),
          backgroundColor: "#5b8cff",
          borderRadius: 6,
        },
        {
          label: "Ispravan SQL",
          data: p.modeli.map((m) => m.validSql),
          backgroundColor: "rgba(91,140,255,.28)",
          borderRadius: 6,
        },
      ],
    },
    options: { ...osnovneOpcije, indexAxis: "y", scales: { x: osaProcenat, y: { grid: { display: false } } } },
  });
}

function grafTezina(p) {
  const nazivi = { lak: "Lak", srednji: "Srednji", tezak: "Težak" };
  if (!p.tezine || p.tezine.length === 0) return;
  grafikoni.tezina = new Chart($("#graf-tezina"), {
    type: "bar",
    data: {
      labels: p.tezine.map((t) => nazivi[t] || t),
      datasets: p.poTezini.map((m, i) => ({
        label: m.naziv,
        data: m.vrednosti,
        backgroundColor: BOJE[i % BOJE.length],
        borderRadius: 5,
      })),
    },
    options: { ...osnovneOpcije, scales: { y: osaProcenat, x: { grid: { display: false } } } },
  });
}

function grafJezik(p) {
  const kartica = $("#graf-jezik").closest(".kartica");
  if (!p.jezici || p.jezici.length < 2) {
    kartica.classList.add("hidden");
    return;
  }
  kartica.classList.remove("hidden");

  const nazivJezika = { sr: "Srpski", en: "Engleski" };
  grafikoni.jezik = new Chart($("#graf-jezik"), {
    type: "bar",
    data: {
      labels: p.poJeziku.map((m) => m.naziv),
      datasets: p.jezici.map((j, i) => ({
        label: nazivJezika[j] || j,
        data: p.poJeziku.map((m) => m.vrednosti[i]),
        backgroundColor: i === 0 ? "#a78bfa" : "#2dd4bf",
        borderRadius: 5,
      })),
    },
    options: { ...osnovneOpcije, scales: { y: osaProcenat, x: { grid: { display: false } } } },
  });
}

function grafBrzina(p) {
  grafikoni.brzina = new Chart($("#graf-brzina"), {
    type: "scatter",
    data: {
      datasets: p.modeli.map((m, i) => ({
        label: m.naziv,
        data: [{ x: m.trajanjeMs, y: m.tacnost }],
        backgroundColor: BOJE[i % BOJE.length],
        pointRadius: 9,
        pointHoverRadius: 12,
      })),
    },
    options: {
      ...osnovneOpcije,
      plugins: {
        ...osnovneOpcije.plugins,
        tooltip: {
          ...osnovneOpcije.plugins.tooltip,
          callbacks: {
            label: (c) => `${c.dataset.label}: ${c.parsed.y.toFixed(1)}% tačnosti, ${Math.round(c.parsed.x)} ms`,
          },
        },
      },
      scales: {
        x: {
          title: { display: true, text: "Prosečno trajanje (ms) — manje je bolje" },
          beginAtZero: true,
          grid: { color: "rgba(38,46,64,.6)" },
        },
        y: { ...osaProcenat, title: { display: true, text: "Tačnost (%)" } },
      },
    },
  });
}

function grafSudija(p) {
  grafikoni.sudija = new Chart($("#graf-sudija"), {
    type: "bar",
    data: {
      labels: p.modeli.map((m) => m.naziv),
      datasets: [
        {
          label: "Objektivna tačnost rezultata",
          data: p.modeli.map((m) => m.tacnost),
          backgroundColor: "#3ecf8e",
          borderRadius: 5,
        },
        {
          label: "Ocena sudije (1–5 skalirano na 100)",
          data: p.modeli.map((m) => (m.ocenaSudije / 5) * 100),
          backgroundColor: "#f0b429",
          borderRadius: 5,
        },
      ],
    },
    options: { ...osnovneOpcije, scales: { y: osaProcenat, x: { grid: { display: false } } } },
  });
}

function prikaziSlaganje(p) {
  const s = p.slaganjeSudije;
  $("#slaganje-kutija").innerHTML = `
    <div class="slaganje-stavka"><b>${procenat(s.procenat)}</b>slaganje sa rezultatom</div>
    <div class="slaganje-stavka"><b>${s.kappa}</b>Cohen's κ — ${escapeHtml(s.opisKappe)}</div>
    <div class="slaganje-stavka"><b>${s.laznoPozitivno}</b>upita sudija pohvalio, a netačni su</div>
    <div class="slaganje-stavka"><b>${s.laznoNegativno}</b>upita sudija odbacio, a tačni su</div>`;
}

function prikaziTabelu(p) {
  const glava = `<thead><tr>
      <th>Model</th>
      <th class="broj">Tačnost</th><th class="broj">Ispravan SQL</th>
      <th class="broj">Ocena sudije</th><th class="broj">Trajanje</th>
      <th class="broj">Tokena</th><th class="broj">Upita</th>
    </tr></thead>`;

  const telo = p.modeli.map((m) => `
    <tr class="${m.modelId === p.pobednik ? "istaknut" : ""}">
      <td>${escapeHtml(m.naziv)}</td>
      <td class="broj">${procenat(m.tacnost)}</td>
      <td class="broj">${procenat(m.validSql)}</td>
      <td class="broj">${m.ocenaSudije.toFixed(2)}</td>
      <td class="broj">${trajanje(m.trajanjeMs)}</td>
      <td class="broj">${brojFormat(Math.round(m.tokeni))}</td>
      <td class="broj">${brojFormat(m.broj)}</td>
    </tr>`).join("");

  $("#tabela-metrike").innerHTML = `${glava}<tbody>${telo}</tbody>`;
}
