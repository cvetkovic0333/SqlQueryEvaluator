// Zajednicki UI helperi (tabovi, formatiranje). Faza 7 prosiruje.

export function initTabs() {
  const tabs = document.querySelectorAll(".tab");
  const panels = document.querySelectorAll(".panel");

  tabs.forEach((tab) => {
    tab.addEventListener("click", () => {
      tabs.forEach((t) => t.classList.toggle("is-active", t === tab));
      const target = `tab-${tab.dataset.tab}`;
      panels.forEach((p) => p.classList.toggle("is-active", p.id === target));
    });
  });
}
