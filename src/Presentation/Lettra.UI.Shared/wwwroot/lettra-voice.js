// Dictée et commandes vocales (vague 5, lane F) : enregistre le micro en WAV PCM 16 bits mono 16 kHz (le format que
// Whisper attend), remonte le niveau sonore pour l'onde affichée, insère le texte dicté dans le champ, et gère la
// touche F8 maintenue (parler tant qu'elle est enfoncée). Aucun audio ne sort d'ici : il est rendu à .NET, qui
// l'envoie au moteur Whisper de la cellule ou du poste.

const TargetRate = 16000;
let session = null;
let pushToTalk = null;

// Contexte sécurisé (https:// ou http://localhost) : ailleurs, le navigateur ne donne jamais le micro à la page.
export function secureContext() {
  return window.isSecureContext !== false;
}

export function supported() {
  return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && (window.AudioContext || window.webkitAudioContext));
}

// Démarre l'enregistrement. Rend "ok", "denied" (micro refusé), "no-device", "unsupported" ou "error".
export async function start(dotnet, maxSeconds) {
  cancel();
  if (!supported()) { return "unsupported"; }
  let stream;
  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } });
  } catch (e) {
    if (e && (e.name === "NotAllowedError" || e.name === "SecurityError")) { return "denied"; }
    if (e && (e.name === "NotFoundError" || e.name === "OverconstrainedError")) { return "no-device"; }
    return "error";
  }

  const Context = window.AudioContext || window.webkitAudioContext;
  const context = new Context();
  const source = context.createMediaStreamSource(stream);
  // ScriptProcessor : déprécié mais présent partout (Safari, WebView Android et iOS) ; un AudioWorklet demanderait un
  // fichier de plus et un contexte sécurisé que les WebView MAUI n'offrent pas toutes.
  const processor = context.createScriptProcessor(4096, 1, 1);
  const chunks = [];
  let lastLevel = 0;
  processor.onaudioprocess = event => {
    const data = event.inputBuffer.getChannelData(0);
    chunks.push(new Float32Array(data));
    const now = performance.now();
    if (now - lastLevel > 90) {
      lastLevel = now;
      let sum = 0;
      for (let i = 0; i < data.length; i++) { sum += data[i] * data[i]; }
      const level = Math.min(1, Math.sqrt(sum / data.length) * 4);
      dotnet.invokeMethodAsync("OnLevel", level).catch(() => {});
    }
  };
  source.connect(processor);
  processor.connect(context.destination);
  const timer = setTimeout(() => dotnet.invokeMethodAsync("OnTimeLimit").catch(() => {}), Math.max(1, maxSeconds || 30) * 1000);
  session = { stream, context, source, processor, chunks, timer };
  return "ok";
}

// Arrête et rend le WAV (Uint8Array, reçu en byte[] côté .NET), ou null s'il n'y avait rien.
export async function stop() {
  const current = session;
  if (!current) { return null; }
  session = null;
  const rate = current.context.sampleRate;
  release(current);
  let length = 0;
  for (const chunk of current.chunks) { length += chunk.length; }
  if (length === 0) { return null; }
  const samples = new Float32Array(length);
  let offset = 0;
  for (const chunk of current.chunks) { samples.set(chunk, offset); offset += chunk.length; }
  return wav(resample(samples, rate, TargetRate), TargetRate);
}

export function cancel() {
  if (session) { release(session); session = null; }
}

function release(current) {
  clearTimeout(current.timer);
  try { current.processor.disconnect(); current.source.disconnect(); } catch { }
  current.stream.getTracks().forEach(track => track.stop());
  current.context.close().catch(() => {});
}

function resample(samples, from, to) {
  if (from === to) { return samples; }
  const ratio = from / to;
  const length = Math.floor(samples.length / ratio);
  const result = new Float32Array(length);
  for (let i = 0; i < length; i++) {
    // Moyenne de la fenêtre : filtre passe-bas grossier, suffisant pour la voix.
    const start = Math.floor(i * ratio);
    const end = Math.min(samples.length, Math.floor((i + 1) * ratio));
    let sum = 0;
    for (let j = start; j < end; j++) { sum += samples[j]; }
    result[i] = end > start ? sum / (end - start) : samples[start] || 0;
  }
  return result;
}

function wav(samples, rate) {
  const buffer = new ArrayBuffer(44 + samples.length * 2);
  const view = new DataView(buffer);
  const text = (offset, value) => { for (let i = 0; i < value.length; i++) { view.setUint8(offset + i, value.charCodeAt(i)); } };
  text(0, "RIFF");
  view.setUint32(4, 36 + samples.length * 2, true);
  text(8, "WAVE");
  text(12, "fmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, rate, true);
  view.setUint32(28, rate * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  text(36, "data");
  view.setUint32(40, samples.length * 2, true);
  for (let i = 0; i < samples.length; i++) {
    const s = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
  }
  return new Uint8Array(buffer);
}

function editable(element) {
  if (!element) { return false; }
  if (element.tagName === "TEXTAREA") { return !element.readOnly && !element.disabled; }
  if (element.tagName === "INPUT") {
    const type = (element.type || "text").toLowerCase();
    return ["text", "search", "email", "tel", "url", ""].includes(type) && !element.readOnly && !element.disabled;
  }
  return element.isContentEditable === true;
}

// Le champ qui a le focus, s'il accepte du texte : son id (créé au besoin) pour y revenir après la transcription.
export function focusedField() {
  const element = document.activeElement;
  if (!editable(element)) { return null; }
  if (!element.id) { element.id = "lt-voice-" + Math.random().toString(36).slice(2, 10); }
  return element.id;
}

// Ajoute le texte dicté à l'endroit du curseur, comme une frappe : l'événement input met à jour Blazor, change
// enregistre (le champ assisté nettoie alors le texte).
export function insertText(id, text) {
  const element = document.getElementById(id);
  if (!editable(element) || !text) { return false; }
  element.focus();
  if (element.isContentEditable) {
    document.execCommand("insertText", false, text);
    return true;
  }
  const value = element.value || "";
  const start = element.selectionStart ?? value.length;
  const end = element.selectionEnd ?? value.length;
  const before = value.slice(0, start);
  const after = value.slice(end);
  const glue = before.length > 0 && !/\s$/.test(before) ? " " : "";
  const inserted = glue + text;
  element.value = before + inserted + after;
  const caret = (before + inserted).length;
  element.setSelectionRange?.(caret, caret);
  element.dispatchEvent(new Event("input", { bubbles: true }));
  element.dispatchEvent(new Event("change", { bubbles: true }));
  return true;
}

// F8 maintenue : appuyer démarre, relâcher arrête. Le navigateur répète keydown tant que la touche est enfoncée :
// seule la première frappe compte.
export function listenPushToTalk(dotnet) {
  unlistenPushToTalk();
  let down = false;
  const keydown = event => {
    if (event.key !== "F8" || event.repeat || down) { return; }
    down = true;
    event.preventDefault();
    dotnet.invokeMethodAsync("OnPushToTalk", true, focusedField()).catch(() => {});
  };
  const keyup = event => {
    if (event.key !== "F8" || !down) { return; }
    down = false;
    event.preventDefault();
    dotnet.invokeMethodAsync("OnPushToTalk", false, null).catch(() => {});
  };
  const blur = () => { if (down) { down = false; dotnet.invokeMethodAsync("OnPushToTalk", false, null).catch(() => {}); } };
  document.addEventListener("keydown", keydown, true);
  document.addEventListener("keyup", keyup, true);
  addEventListener("blur", blur);
  pushToTalk = { keydown, keyup, blur };
}

export function unlistenPushToTalk() {
  if (pushToTalk) {
    document.removeEventListener("keydown", pushToTalk.keydown, true);
    document.removeEventListener("keyup", pushToTalk.keyup, true);
    removeEventListener("blur", pushToTalk.blur);
    pushToTalk = null;
  }
}
