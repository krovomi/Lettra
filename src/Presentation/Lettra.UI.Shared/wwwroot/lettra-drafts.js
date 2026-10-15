// Brouillons hors ligne du navigateur (étape 4.5) : une base IndexedDB par utilisateur et par dossier,
// « lettra-drafts.<clé de l'utilisateur>.<dossier> », avec un magasin pour les brouillons (clé localId, valeur JSON
// écrite par .NET) et un pour le cache des données de référence. .NET passe ce périmètre (scope) à chaque appel ; la
// déconnexion efface la base. Le retour du réseau (événement « online ») prévient .NET, qui envoie les brouillons.

const PREFIX = "lettra-drafts";
const DRAFTS = "drafts";
const CACHE = "cache";
// Périmètre de l'onglet, pour recharger l'application sans réseau ; sessionStorage disparaît avec l'onglet.
const REMEMBERED = "lettra-drafts.scope";

function nameOf(scope) {
  return scope ? `${PREFIX}.${scope}` : PREFIX;
}

function open(scope) {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(nameOf(scope), 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(DRAFTS)) db.createObjectStore(DRAFTS, { keyPath: "id" });
      if (!db.objectStoreNames.contains(CACHE)) db.createObjectStore(CACHE, { keyPath: "key" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function run(scope, store, mode, action) {
  const db = await open(scope);
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

export async function listDrafts(scope) {
  const rows = await run(scope, DRAFTS, "readonly", s => s.getAll());
  return (rows ?? []).map(r => r.json);
}

export function putDraft(id, json, scope) {
  return run(scope, DRAFTS, "readwrite", s => s.put({ id, json }));
}

export function deleteDraft(id, scope) {
  return run(scope, DRAFTS, "readwrite", s => s.delete(id));
}

export async function readCache(key, scope) {
  const row = await run(scope, CACHE, "readonly", s => s.get(key));
  return row?.value ?? null;
}

export function writeCache(key, value, scope) {
  return run(scope, CACHE, "readwrite", s => s.put({ key, value }));
}

// Déconnexion : la base de l'utilisateur et du dossier disparaît, avec le périmètre retenu par l'onglet.
export function clearScope(scope) {
  forgetScope();
  return new Promise((resolve, reject) => {
    const request = indexedDB.deleteDatabase(nameOf(scope));
    request.onsuccess = () => resolve();
    request.onblocked = () => resolve();
    request.onerror = () => reject(request.error);
  });
}

export function rememberScope(scope) {
  try { sessionStorage.setItem(REMEMBERED, scope); } catch { /* stockage refusé : pas de reprise hors ligne */ }
}

export function rememberedScope() {
  try { return sessionStorage.getItem(REMEMBERED); } catch { return null; }
}

export function forgetScope() {
  try { sessionStorage.removeItem(REMEMBERED); } catch { /* rien à oublier */ }
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
