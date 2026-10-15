// Services navigateur de l'UI partagée (WASM et MAUI Hybrid) : téléchargement de fichiers, préférences locales.

export function downloadFile(fileName, mimeType, bytes) {
  // bytes arrive en Uint8Array (interop .NET byte[]).
  const blob = new Blob([bytes], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.rel = "noopener";
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export function readPreference(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

export function writePreference(key, value) {
  try { localStorage.setItem(key, value); } catch { /* stockage indisponible : la préférence vaut pour la session */ }
}

// Densité par défaut selon la plateforme : Confort au doigt (tablette, mobile), Expert à la souris et au clavier.
export function prefersComfort() {
  return window.matchMedia ? window.matchMedia("(pointer: coarse)").matches : false;
}
