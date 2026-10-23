# Voix : dictée et commandes vocales

Vague 5, lane D. Ce module transforme ce qui est dit en texte mis en forme, et en commande quand l'utilisateur parle
à Lettra (« ouvrir les factures en retard », « ajoute deux heures d'audit à 90 euros »). Le moteur est **Whisper,
toujours local** : sur le poste (Whisper.net) ou dans la cellule (serveur whisper.cpp). L'audio ne part jamais vers
un service tiers (ADR-0011, ADR-0012). Il n'est ni enregistré ni journalisé, et le texte entendu non plus.

## Ce qui se passe quand on parle

1. L'interface enregistre un WAV PCM 16 bits, mono, 16 kHz (30 s au plus par défaut, 120 s au maximum).
2. `TranscribeSpeechQuery` (permission « lire le dossier ») vérifie le format, écarte le silence sans réveiller le
   moteur, puis appelle `ISpeechToText` avec la langue choisie (jamais de détection automatique) et le vocabulaire
   du métier (« devis, facture, HT, TTC… »).
3. `TranscriptCleaner` retire ce que Whisper invente sur du silence (« Sous-titres réalisés par Amara.org »,
   « [Musique] »).
4. `DictationFormatter` met le texte en forme : ponctuation dite (« virgule », « point à la ligne »), montants
   (« mille deux cents euros » → « 1 200,00 € »), pourcentages, espaces insécables à la française, majuscules.
5. En mode commande, `VoiceCommandParser` reconnaît la commande par une grammaire **déterministe** (pas d'IA) :
   aller à une liste avec un filtre, créer, chercher, ajouter une ligne (quantité, unité UN/ECE, désignation, prix,
   HT ou TTC), appliquer une remise, préparer un devis pour un client, valider, enregistrer, annuler, aide.
   Une commande qui valide ou émet demande toujours confirmation, et chaque action repasse par son propre cas
   d'usage et sa propre permission.

## Activer la voix

Elle est **désactivée par défaut** : sans moteur, `GET /api/v1/voix/statut` répond `Enabled: false` et la
transcription répond 403 `Speech.Disabled`.

| Où | Moteur | Réglage |
|---|---|---|
| Serveur (modes 2, 3, 5) | serveur whisper.cpp de la cellule | `Lettra:Speech:Engine=WhisperServer`, `Lettra:Speech:WhisperServer:Endpoint=http://whisper:8080/` |
| Poste MAUI (modes 1, 4) | Whisper.net | `services.AddOnDeviceSpeech(new OnDeviceSpeechOptions { ModelDirectory = … })` |

Exception : l'application MAUI ouvre la dictée **par défaut**, y compris dans la version de démonstration
(`LettraDemo=true`). Elle se coupe de deux façons (`DeviceSpeech.cs`) :

- sur un appareil, `Lettra:Speech:Engine` dans `lettra.json` (dossier de données de l'application) :
  `{ "Lettra": { "Speech": { "Engine": "None" } } }`. `"OnDevice"` la rouvre ; toute autre valeur la laisse coupée
  (le poste n'a pas d'autre moteur). Lu au démarrage ;
- à la construction, `-p:LettraDictee=false` : coupée par défaut sur ce build, `lettra.json` peut la rouvrir.

Coupée, `AddOnDeviceSpeech` n'est pas appelé : le statut répond `Enabled: false`, le micro n'apparaît pas, le modèle
n'est jamais téléchargé.

Le serveur est l'image construite par `build/voix/whisper-server` : whisper.cpp v1.8.7 compilé sans optimisations
propres à la machine de construction, utilisateur non privilégié, modèle (`WHISPER_MODEL=base`) téléchargé au premier
démarrage dans le volume `/models` et vérifié par empreinte. L'image publique `main` de whisper.cpp s'arrêtait au
chargement du modèle sur les machines de la CI.

Autres réglages : `Lettra:Speech:Languages` (`fr`, `en`), `Lettra:Speech:MaxSeconds` (30). Une adresse de serveur
Whisper publique est **refusée au démarrage** : seuls les noms de service, `*.svc` (et `*.svc.*`), `*.cluster.local`,
`*.internal`, `*.local`, `*.lan`, `*.home.arpa`, `*.localhost` et les adresses privées passent, sauf avec
`Lettra:Speech:WhisperServer:AllowPublicEndpoint=true` (serveur Whisper du cabinet joint par un nom public, jamais un
service tiers).

Sur le poste, le modèle (`base` par défaut, 142 Mio ; `tiny` et `small` possibles) est téléchargé une fois depuis le
dépôt public de whisper.cpp, puis vérifié par l'empreinte SHA-1 publiée **et** par le SHA-256 épinglé dans
`WhisperModelStore.Files` (les deux doivent concorder). Un fichier altéré est supprimé et jamais chargé. whisper.cpp ne
publie que des SHA-1 : le SHA-256 de `ggml-base.bin` (le modèle par défaut et celui de la démonstration) est épinglé et
revérifié à chaque run du workflow « Voix » par `build/voix/model.sh`, qui annote aussi le SHA-256 obtenu ; `tiny` et
`small` n'en ont pas encore et restent vérifiés par le seul SHA-1 (pour les épingler : lancer « Voix » avec ce
modèle, reporter le SHA-256 annoté dans `WhisperModelStore.cs`, `build/voix/model.sh` et
`build/voix/whisper-server/entrypoint.sh`). Le conteneur `whisper` de la démonstration vérifie les mêmes empreintes. Un poste sans réseau reçoit le fichier `ggml-base.bin` dans son dossier `voix` (`AllowDownload = false`).

## Route

- `GET /api/v1/voix/statut` : moteur, langues, durée maximale, et `Ready` : faux tant que le moteur ne peut pas
  transcrire (serveur whisper.cpp qui télécharge ou charge son modèle ; l'API lui demande sa racine, 3 secondes au
  plus, par `ISpeechToText.IsReadyAsync`). Whisper.net sur l'appareil est toujours `Ready` : son modèle se télécharge
  au premier usage. L'interface n'affiche le micro des champs que si `Enabled` et `Ready`.
- `POST /api/v1/voix/transcription?mode=Dictation|Command&langue=fr|en`, corps `audio/wav` : texte mis en forme et
  commande éventuelle. 400 pour un audio invalide, trop long ou silencieux, 415 pour un autre type que WAV,
  503 `Speech.EngineUnavailable` ou `Speech.ModelUnavailable` quand le moteur ne répond pas.

L'API envoie `Permissions-Policy: microphone=(self)` : le micro n'est permis qu'à l'application elle-même.

## Ce qui est vérifié, et où

| Vérification | Où |
|---|---|
| Grammaire, mise en forme, lecture des WAV, adaptateurs (avec un faux serveur), route de l'API | Tests de la CI, Linux |
| Corpus de phrases lu par une voix de synthèse (Piper) et transcrit par un vrai Whisper | Workflow « Voix » : Whisper.net sur Linux, Windows et macOS ; serveur whisper.cpp en conteneur |
| Whisper.net sur Android, iOS et Mac Catalyst | Non vérifié : le paquet fournit les bibliothèques natives, rien n'a tourné sur un appareil |
| Vraie voix humaine, bruit de bureau, accents | Non vérifié |

Le rapport du workflow « Voix » (taux d'erreur de mots, commandes reconnues) s'affiche dans le résumé de chaque run.
Le corpus est dans `tests/Lettra.Speech.Tests/Corpus/phrases.json`.

### Corpus déterministe

Piper tire d'ordinaire du bruit au hasard (intonation, durée des phonèmes) : chaque run lisait les phrases un peu
autrement, et le score bougeait sans changement de code (7/12 commandes reconnues, sous le seuil de 60 %, puis 8, 9 ou
10/12). Le job « Corpus et modèle » produit désormais un corpus connu d'avance, octet pour octet :

- Piper lit sans bruit aléatoire (`--noise-scale 0 --noise-w-scale 0`) ;
- Piper, ONNX Runtime et leurs dépendances sont épinglés dans `build/voix/piper-requirements.txt`, Python en 3.13,
  le job tourne sur `ubuntu-24.04` (et son ffmpeg) plutôt que `ubuntu-latest` ;
- les voix (`fr_FR-siwis-medium`, `en_US-lessac-medium`) sont vérifiées par leur SHA-256 (`build/voix/voices.sha256`) ;
- les WAV produits doivent être identiques à l'une des variantes de `build/voix/corpus/*.sha256`, sinon le job
  échoue et une annotation donne les empreintes obtenues et le processeur du runner.

Pourquoi des variantes : même sans bruit, ONNX Runtime choisit ses noyaux de calcul selon le processeur (AVX2,
AVX-512…), et les runners GitHub n'ont pas tous le même. Les derniers bits des échantillons changent alors d'un
processeur à l'autre ; à processeur égal, le corpus est le même octet pour octet. Chaque variante épinglée a été
transcrite par les quatre moteurs, avec le même score (voir la PR qui l'ajoute) : le score ne dépend plus du hasard.

Le seuil (60 % des commandes, 50 % d'erreurs de mots au plus) ne change pas ; seul le hasard du corpus disparaît.

Pour régénérer le corpus après un changement de `phrases.json`, de voix ou de version de Piper (Python 3.13, ffmpeg,
jq et accès à huggingface.co requis) :

```bash
python3 -m pip install -r build/voix/piper-requirements.txt
build/voix/corpus.sh --epingler voix/corpus voix/voices   # réécrit voices.sha256, remplace les variantes par une seule
```

Puis commiter les manifestes avec le changement. Le premier run sur un autre processeur échoue avec une annotation qui
donne les lignes de sa variante : les ajouter dans `build/voix/corpus/<nom>.sha256`, après avoir vérifié son score.

## Dans l'interface (lane F)

Le bouton micro de la barre du haut, le micro des champs, F8 maintenue, l'enregistreur du navigateur et l'exécution
des commandes dans les écrans sont décrits dans `docs/aide-a-la-saisie.md` (section 3).

## Branchement dans les hôtes

- **Démonstration Docker** (`deploy/demo/compose.yaml`) : conteneur `whisper` construit depuis
  `build/voix/whisper-server`, modèle dans le volume `whisper-modeles`, API en `WhisperServer`. L'API démarre sans
  attendre le modèle ; tant qu'il n'est pas chargé, le statut répond `Ready: false` (l'interface affiche « La dictée se
  prépare » et redemande toute seule toutes les 15 secondes) et la transcription 503.
- **Application MAUI** (`MauiProgram.cs`) : `AddOnDeviceSpeech` après `AddLettra`, modèle dans
  `FileSystem.AppDataDirectory/voix`. `Whisper.net.Runtime` est référencé par l'application elle-même (ses cibles
  MSBuild choisissent les bibliothèques natives de chaque plateforme : Android arm64-v8a et x86_64, iOS, Mac Catalyst,
  Windows). Micro : `RECORD_AUDIO` et capture audio accordée à la seule page de l'application (Android),
  `NSMicrophoneUsageDescription` (iOS, Mac Catalyst) et `com.apple.security.device.audio-input` (Mac Catalyst),
  autorisation WebView2 pour la page de l'application (Windows). Rien de cela n'a tourné sur un appareil.
