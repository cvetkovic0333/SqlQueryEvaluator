// Deljeno stanje aplikacije. Bez framework-a, ali i bez razbacanih globalnih
// promenljivih: moduli se pretplate na promenu i sami se osveže.

const stanje = {
  baze: [],
  aktivnaBaza: null,
  sema: null,
  izabraneTabele: new Set(),
  modeli: [],
  aktivniModel: null,
  jezik: "sr",
  poslednjiPrevod: null,
};

const pretplatnici = new Set();

export function daj() {
  return stanje;
}

export function postavi(izmene) {
  Object.assign(stanje, izmene);
  pretplatnici.forEach((f) => f(stanje));
}

export function pretplati(f) {
  pretplatnici.add(f);
  return () => pretplatnici.delete(f);
}

export function prebaciTabelu(naziv) {
  if (stanje.izabraneTabele.has(naziv)) stanje.izabraneTabele.delete(naziv);
  else stanje.izabraneTabele.add(naziv);
  pretplatnici.forEach((f) => f(stanje));
}
