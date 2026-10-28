// Lecture des pièces sur l'appareil : la photo d'un ticket ou d'une facture est lue par Tesseract (WebAssembly) avec le
// modèle français livré dans _content/Lettra.UI.Shared/ocr/. Rien ne quitte l'appareil : ni l'image, ni le texte lu.
// Le texte est rendu à .NET, qui en tire montant, date et TVA (ReceiptTextReader) ; l'utilisateur les confirme.

const MaxSide = 2000;
let engine = null;

function base() {
  return new URL("_content/Lettra.UI.Shared/ocr/", document.baseURI).href;
}

function loadScript(url) {
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = url;
    script.onload = resolve;
    script.onerror = () => reject(new Error("script"));
    document.head.appendChild(script);
  });
}

// WebAssembly SIMD : le seul moteur livré l'exige.
export function supported() {
  try {
    return typeof WebAssembly === "object" && WebAssembly.validate(new Uint8Array([
      0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 96, 0, 1, 123, 3, 2, 1, 0, 10, 10, 1, 8, 0, 65, 0, 253, 15, 253, 98, 11]));
  } catch {
    return false;
  }
}

async function worker() {
  if (!engine) {
    engine = (async () => {
      if (!window.Tesseract) {
        await loadScript(base() + "tesseract.min.js");
      }
      // Le modèle est lu ici et remis au moteur : l'extension .bin est servie partout (serveur, application), sans
      // réglage du type des fichiers statiques.
      const model = await fetch(base() + "fra.traineddata.bin");
      if (!model.ok) {
        throw new Error("model");
      }
      return window.Tesseract.createWorker([{ code: "fra", data: new Uint8Array(await model.arrayBuffer()) }], 1, {
        workerPath: base() + "worker.min.js",
        corePath: base() + "tesseract-core-simd-lstm.wasm.js",
        workerBlobURL: false,
        cacheMethod: "none",
      });
    })();
    engine.catch(() => { engine = null; });
  }
  return engine;
}

// Une image lisible par le navigateur, réduite à 2000 points au plus grand côté (lecture plus rapide, assez fine pour
// un ticket), orientée d'après ses métadonnées. Rend null si le navigateur ne sait pas la décoder (HEIC hors Safari).
async function prepare(bytes, contentType) {
  let bitmap;
  try {
    bitmap = await createImageBitmap(new Blob([bytes], { type: contentType }), { imageOrientation: "from-image" });
  } catch {
    return null;
  }
  const scale = Math.min(1, MaxSide / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return canvas;
}

// Rend "ok" suivi d'un saut de ligne et du texte lu, ou "unsupported", "unreadable", "error".
export async function read(bytes, contentType) {
  if (!supported()) {
    return "unsupported";
  }
  const image = await prepare(bytes, contentType);
  if (!image) {
    return "unreadable";
  }
  try {
    const engineWorker = await worker();
    const result = await engineWorker.recognize(image);
    return "ok\n" + (result.data.text || "");
  } catch {
    return "error";
  }
}

// Libère le moteur (environ 50 Mo de mémoire) quand l'écran qui l'utilisait se ferme.
export async function release() {
  if (engine) {
    const current = engine;
    engine = null;
    try { (await current).terminate(); } catch { /* déjà arrêté */ }
  }
}
