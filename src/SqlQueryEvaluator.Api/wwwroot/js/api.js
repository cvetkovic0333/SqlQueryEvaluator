// Tanak omotac oko fetch-a ka /api/*. Faza 6/7.

async function request(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}: ${await res.text()}`);
  }
  return res.json();
}

export const api = {
  schema: () => request("/api/schema"),
  preview: (table, limit = 50) => request(`/api/schema/${encodeURIComponent(table)}/preview?limit=${limit}`),
  translate: (body) => request("/api/translate", { method: "POST", body: JSON.stringify(body) }),
  execute: (body) => request("/api/execute", { method: "POST", body: JSON.stringify(body) }),
  history: (limit = 50) => request(`/api/history?limit=${limit}`),
  benchmarkSummary: () => request("/api/benchmark/summary"),
};
