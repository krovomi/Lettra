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

Le serveur est l'image construite par `build/voix/whisper-server` : whisper.cpp v1.8.7 compilé sans optimisations
propres à la machine de construction, utilisateur non privilégié, modèle (`WHISPER_MODEL=base`) téléchargé au premier
démarrage dans le volume `/models` et vérifié par empreinte. L'image publique `main` de whisper.cpp s'arrêtait au
chargement du modèle sur les machines de la CI.

Autres réglages : `Lettra:Speech:Languages` (`fr`, `en`), `Lettra:Speech:MaxSeconds` (30). Une adresse de serveur
Whisper publique est **refusée au démarrage** : seuls les noms de service, `*.svc`, `*.cluster.local`, `*.internal`,
`*.local`, `*.lan` et les adresses privées passent.

Sur le poste, le modèle (`base` par défaut, 142 Mio ; `tiny` et `small` possibles) est téléchargé une fois depuis le
dépôt public de whisper.cpp, puis vérifié par l'empreinte SHA-1 publiée. Un fichier altéré est supprimé et jamais
chargé. Un poste sans réseau reçoit le fichier `ggml-base.bin` dans son dossier `voix` (`AllowDownload = false`).

## Route

- `GET /api/v1/voix/statut` : moteur, langues, durée maximale ; l'interface n'affiche le micro que si `Enabled`.
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

## Ce qui viendra ensuite (lane F)

Le bouton micro, l'enregistreur du navigateur et l'application des commandes dans les écrans et les formulaires.
Le branchement dans la démonstration (conteneur whisper.cpp) et dans l'application MAUI (moteur du poste,
autorisations du micro) suit cette PR.
