// Zajednički pomoćnici: tabovi, poruke, formatiranje, crtanje tabela.

export const $ = (izbor) => document.querySelector(izbor);
export const $$ = (izbor) => Array.from(document.querySelectorAll(izbor));

export function initTabovi(priPromeni) {
  const tabovi = $$(".tab");
  const paneli = $$(".panel");

  tabovi.forEach((tab) => {
    tab.addEventListener("click", () => {
      const cilj = tab.dataset.tab;
      tabovi.forEach((t) => t.classList.toggle("is-active", t === tab));
      paneli.forEach((p) => p.classList.toggle("is-active", p.id === `tab-${cilj}`));
      if (priPromeni) priPromeni(cilj);
    });
  });
}

let toastTajmer = null;
export function poruka(tekst, jeGreska = false) {
  const el = $("#toast");
  el.textContent = tekst;
  el.classList.toggle("greska", jeGreska);
  el.classList.add("vidljiv");
  clearTimeout(toastTajmer);
  toastTajmer = setTimeout(() => el.classList.remove("vidljiv"), 3600);
}

export function escapeHtml(v) {
  if (v === null || v === undefined) return "";
  return String(v)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

export function brojFormat(n) {
  if (n === null || n === undefined) return "—";
  return new Intl.NumberFormat("sr-RS").format(n);
}

export function procenat(n) {
  if (n === null || n === undefined) return "—";
  return `${Number(n).toFixed(1)}%`;
}

export function trajanje(ms) {
  if (ms === null || ms === undefined) return "—";
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(2)} s`;
}

export function datumFormat(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleString("sr-RS", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

/**
 * Crta tabelu iz {kolone: [], redovi: [[]]}.
 * Brojevi se poravnavaju desno, NULL se vidno razlikuje od praznog teksta —
 * to je bitno kad se proverava rezultat upita.
 */
export function nacrtajTabelu(element, kolone, redovi) {
  if (!kolone || kolone.length === 0) {
    element.innerHTML = `<tbody><tr><td class="null">Upit nije vratio nijednu kolonu.</td></tr></tbody>`;
    return;
  }

  const glava = `<thead><tr>${kolone.map((k) => `<th>${escapeHtml(k)}</th>`).join("")}</tr></thead>`;

  const telo = redovi.length === 0
    ? `<tr><td colspan="${kolone.length}" class="null">Nema rezultata.</td></tr>`
    : redovi.map((red) => `<tr>${red.map((v) => {
        if (v === null || v === undefined) return `<td class="null">NULL</td>`;
        if (typeof v === "number") return `<td class="broj">${escapeHtml(v)}</td>`;
        if (typeof v === "boolean") return `<td>${v ? "da" : "ne"}</td>`;
        return `<td>${escapeHtml(v)}</td>`;
      }).join("")}</tr>`).join("");

  element.innerHTML = `${glava}<tbody>${telo}</tbody>`;
}

export function postaviUcitavanje(dugme, ucitava, tekstNormalno) {
  dugme.disabled = ucitava;
  const raspon = dugme.querySelector(".dugme-tekst") || dugme;
  raspon.innerHTML = ucitava
    ? `<span class="vrtenje"></span> Radim…`
    : escapeHtml(tekstNormalno);
}
