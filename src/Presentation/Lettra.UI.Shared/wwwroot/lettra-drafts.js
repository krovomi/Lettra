// Brouillons hors ligne du navigateur (étape 4.5) : IndexedDB « lettra-drafts », un magasin pour les brouillons
// (clé localId, valeur JSON écrite par .NET) et un pour le cache des données de référence. Le retour du réseau
// (événement « online ») prévient .NET, qui envoie les brouillons.

const DB = "lettra-drafts";
const DRAFTS = "drafts";
const CACHE = "cache";

function open() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(DRAFTS)) db.createObjectStore(DRAFTS, { keyPath: "id" });
      if (!db.objectStoreNames.contains(CACHE)) db.createObjectStore(CACHE, { keyPath: "key" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function run(store, mode, action) {
  const db = await open();
  try {
    return await new Promise((resolve, reject) => {
      const tx = db.transaction(store, mode);
      const request = action(tx.objectStore(store));
      tx.oncomplete = () => resolve(request?.result);
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  } finally {
    db.close();
  }
}

export async function listDrafts() {
  const rows = await run(DRAFTS, "readonly", s => s.getAll());
  return (rows ?? []).map(r => r.json);
}

export function putDraft(id, json) {
  return run(DRAFTS, "readwrite", s => s.put({ id, json }));
}

export function deleteDraft(id) {
  return run(DRAFTS, "readwrite", s => s.delete(id));
}

export async function readCache(key) {
  const row = await run(CACHE, "readonly", s => s.get(key));
  return row?.value ?? null;
}

export function writeCache(key, value) {
  return run(CACHE, "readwrite", s => s.put({ key, value }));
}

export function isOnline() {
  return navigator.onLine !== false;
}

// Une seule écoute par page : la dernière référence .NET reçoit le retour du réseau.
let listener = null;

export function watchOnline(dotnet) {
  if (listener) window.removeEventListener("online", listener);
  listener = () => dotnet.invokeMethodAsync("OnOnline");
  window.addEventListener("online", listener);
}

export function unwatchOnline() {
  if (listener) window.removeEventListener("online", listener);
  listener = null;
}
