# Assistant IA : points ouverts, repli sans IA et transparence

L'assistant IA est désactivé par défaut (section `Lettra:Ai`, démo comprise). Quand un cabinet l'active, il choisit un
moteur et les points qu'il ouvre, un par un. Les règles de conformité restent en C# ; l'assistant ne fait qu'expliquer
ou proposer, et il ne valide ni n'émet jamais rien (ADR-0006, ADR-0012).

| Point | Code | État dans Lettra |
|---|---|---|
| 1. Brouillon de devis à partir d'un texte libre | `DraftFromText` | **Fermé** tant que Lettra ne sait pas repérer les noms propres dans le texte (voir ci-dessous) |
| 3. « Pourquoi ? » sur la checklist de conformité | `ExplainCompliance` | Ouvrable par le cabinet |
| 4. Diagnostic e-facturation de la pièce ouverte | `EInvoicingDiagnosis` | Ouvrable par le cabinet |
| 6. Questions TVA et réglementaires | `RegulatoryQuestions` | Ouvrable par le cabinet |

## Bien utiliser l'IA de Lettra (page d'aide)

Menu **Pilotage → Bien utiliser l'IA** (`/ai`), en français et en anglais, sur ordinateur, tablette et téléphone. La
page marche sans IA ; on y arrive aussi par le lien « Bien utiliser l'IA » du panneau assistant, par la question « À
quoi sert l'assistant IA ? » de l'aide, et par la démo commentée (chapitre « L'IA, à sa juste place »). Elle sert de
mesure de maîtrise de l'IA pour les cabinets (article 4 du règlement européen sur l'IA).

- **État de l'IA dans le dossier** : éteinte (cas par défaut), active (moteur et fonctions ouvertes), ou moteur en pause.
- **Ce que fait l'IA** : expliquer un constat de conformité, aider au diagnostic e-facturation, répondre aux questions
  de TVA en citant ses sources, toujours par les calculs de Lettra pour les montants.
- **Ce qu'elle ne fait jamais** : valider, émettre ou envoyer un document ; décider à la place des règles ; voir les
  noms du dossier ; noter la solvabilité d'une personne ; évaluer, répartir ou surveiller le travail des collaborateurs.
- **Reconnaître un texte de l'IA**, **la couper ou l'activer**, **quand le moteur ne répond pas** (voir plus bas).
- **Registre des usages de l'IA** : chaque usage, son rôle, son niveau de risque au sens du règlement européen sur
  l'IA et son état dans le dossier (ouvert, non ouvert, fermé par Lettra, réglé ailleurs). Usages : « Pourquoi ? »,
  diagnostic e-facturation, questions TVA, brouillon de devis (fermé), assistant IA du poste (MCP), dictée et
  commandes vocales. Une nouvelle fonction de l'assistant doit y entrer : un test le vérifie.
- **Journal de l'IA** du dossier, pour qui a l'accès à l'assistant IA : date, événement (demande, appel au moteur,
  outil utilisé ou refusé, brouillon, réponse, refus, indisponible, panne du moteur), détail aux noms masqués et
  moteur ; recherche, filtre par événement et tri sur chaque colonne. Les 200 entrées les plus récentes. Sans cet accès,
  la page le dit et ne demande pas le journal (aucune erreur dans la console).

## Reconnaître un texte de l'IA (transparence, article 50)

- **À l'écran** : chaque texte rédigé par l'IA porte la mention « Texte rédigé par l'IA, à vérifier » (« Text written
  by AI, to be checked ») et l'icône de l'IA. L'info-bulle de la mention donne le moteur qui l'a écrit. Les étapes,
  règles, sources et brouillons de Lettra s'affichent à part, sans cette mention.
- **Pour les machines** : l'événement de réponse du flux (`text`) porte `aiGenerated: true`, le moteur (`engine`) et
  `digitalSourceType: "http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia"` (vocabulaire IPTC des
  contenus générés par IA). Le panneau reporte ces marques sur l'élément affiché (`data-ai-generated`,
  `data-digital-source-type`, `data-ai-engine`).
- **Au journal** : le moteur de chaque appel et de chaque réponse, comme avant.

## Quand le moteur ne répond pas (repli sans IA)

Le moteur peut tarder, couper la connexion, refuser (quota, identifiants) ou renvoyer une réponse illisible, y compris
au milieu d'une réponse, après une étape déjà faite. Rien ne casse :

1. **La réponse se termine proprement** sur « moteur indisponible » (code `Ai.EngineFailed`, réponse HTTP 200, flux
   NDJSON complet). Les étapes déjà affichées restent ; aucun texte n'est inventé.
2. **Le journal IA du dossier note la panne** (événement `EngineFailed`) avec sa raison en clair : « délai dépassé »,
   « moteur injoignable », « réponse illisible » ou « moteur en pause après des échecs répétés ». Ni le message du
   fournisseur ni la pile d'appels n'y vont, ni dans les journaux techniques : ils pourraient citer la demande.
3. **Disjoncteur** : un disjoncteur par type de moteur (Claude UE, modèle local), posé sur le port des moteurs. Quand au
   moins la moitié des appels des cinq dernières minutes ont échoué (à partir de trois appels), Lettra cesse d'appeler
   ce moteur pendant deux minutes : l'assistant répond aussitôt « moteur en pause » (`Ai.EnginePaused`) sans l'appeler.
   Après la pause, l'appel suivant sert d'essai ; s'il réussit, tout repart.
4. **À l'écran** : le panneau assistant affiche « IA disponible » ou « IA en pause : le moteur ne répond pas » ; le
   statut de l'assistant (`GET /api/v1/assistant/statut`) porte `engineResponding`. Les fonctions ouvertes par le
   cabinet restent affichées.
5. **Pour l'exploitation** : deux métriques agrégées, sans dossier, personne ni texte (type de moteur seulement :
   `claude_eu`, `local`) : `lettra.ai.engine.available` (1 si le moteur répond, 0 pendant la pause ; Prometheus :
   `lettra_ai_engine_available`) et `lettra.ai.fallback.count` (replis sans IA, par raison `timeout`, `unreachable`,
   `unreadable`, `paused` ; Prometheus : `lettra_ai_fallback_count_total`). La règle `LettraMoteurIaNeRepondPlus`
   (S3, centre d'alertes, personne n'est réveillé) se déclenche quand tous les réplicas de l'API qui ont appelé un
   moteur l'ont mis en pause depuis cinq minutes ; elle se ferme au premier essai réussi.
6. **L'arrêt demandé par l'utilisateur** (bouton « Arrêter ») n'est pas une panne : rien au journal, le disjoncteur
   n'en tient pas compte.
7. **Côté navigateur** : si la connexion au serveur coupe pendant la réponse, ou qu'une ligne du flux est illisible,
   le panneau garde les étapes reçues et se termine sur « serveur injoignable » (`Api.Unreachable`).

### Réglages

| Réglage | Par défaut | Rôle |
|---|---|---|
| `Lettra:Ai:Claude:Timeout` | `00:01:30` | Délai d'un appel au moteur Claude (un tour de l'assistant). Une seule nouvelle tentative du client Anthropic. Doit être positif, sinon l'API refuse de démarrer. |
| `Lettra:Ai:Local:Timeout` | `00:00:30` | Délai d'un appel au modèle local. Suffit sur GPU ; l'allonger sur un serveur sans GPU. Doit être positif. |
| `Lettra:Ai:Resilience:MinimumThroughput` | `3` | Appels sur cinq minutes à partir desquels le disjoncteur peut s'ouvrir (au moins la moitié en échec). Au moins 2. |
| `Lettra:Ai:Resilience:BreakDuration` | `00:02:00` | Durée de la pause pendant laquelle Lettra n'appelle plus le moteur. Au moins une seconde. |

## Pourquoi le point 1 est fermé

Avant tout envoi au moteur, Lettra masque les noms et identifiants **connus du dossier** (clients, fournisseurs,
articles). Le point 1 envoie le texte tapé par l'utilisateur : un nom de personne ou d'entreprise qui n'est pas encore
dans le dossier passerait en clair. L'AIPD (annexe I §7 et §8), l'analyse de risques (annexe J §5.2) et l'annexe F §10
exigent un repérage des noms propres (détection d'entités nommées) avant d'ouvrir ce texte libre à l'IA. Ce repérage
n'existe pas encore : le DPO a décidé de fermer le point 1 jusque-là.

## Ce que fait Lettra

La fermeture vaut pour tous les modes de déploiement : serveur (API), poste isolé et application installée hors ligne
(même cas d'usage, appelé dans le processus), et quelle que soit la source du réglage (base Catalog, entrée du cabinet
dans `Lettra:Ai:Organisations`, réglage du déploiement).

- **Enregistrer** des réglages qui contiennent le point 1 (`PUT /api/v1/cabinets/{id}/assistant/reglages`) est refusé
  avec le code `Ai.FeatureBlocked` sur le champ `Features` (400), même si l'IA reste éteinte.
- **Un réglage enregistré avant la fermeture** n'est ni effacé ni modifié (les données survivent aux mises à jour) :
  il est ignoré. La lecture des réglages ne montre pas le point 1, le statut de l'assistant (`/api/v1/assistant/statut`)
  ne le propose pas, et une demande sur ce point reçoit `Ai.FeatureBlocked` (403) sans que le moteur soit appelé.
- **À l'écran**, le panneau « Assistant IA » ne propose plus « Brouillon de devis » et l'explique en une phrase :
  « Le brouillon de devis à partir d'un texte libre arrivera quand Lettra saura masquer les noms tapés dans le texte. »
  Le message d'erreur `Ai.FeatureBlocked` est traduit en français et en anglais.

Parcours d'une demande (trait plein : construit ; « (non réalisé) » : cible) :

```text
Demande ─► IA activée par le cabinet ? ── non ─► Ai.Disabled
              │ oui
              ▼
           Point qui exige le repérage des noms propres (point 1) ?
              │ oui ─► repérage disponible ? ── non ─► Ai.FeatureBlocked   (aujourd'hui, toujours)
              │                     │ oui (non réalisé)
              ▼                     ▼
           Point activé, dossier non exclu, moteur présent ? ─► masquage
                                                                   │
                                                                   ▼
                                                 disjoncteur du moteur ── en pause ─► Ai.EnginePaused
                                                                   │ fermé
                                                                   ▼
                                       moteur (délai Claude 90 s, local 30 s) ── panne ─► Ai.EngineFailed
                                                                   │ réponse            (journal IA, compteur de replis,
                                                                   ▼                     « IA en pause » si répété)
                                         « Texte rédigé par l'IA, à vérifier » + marque lisible par machine
```

## Dans le code

- `INamedEntityDetection` et `AiFeatureSafeguards` (`src/Core/Lettra.Application/Abstractions/Ai/NamedEntities.cs`) :
  le port du repérage et la liste des points qui l'exigent. La seule implémentation, `INamedEntityDetection.None`, dit
  qu'il n'est pas disponible ; elle est enregistrée par défaut par l'application et par la couche IA.
- `AiErrors.CheckPolicy` (`Features/Assistant/AskAssistant.cs`) : le contrôle unique utilisé par la demande, le statut
  et l'orchestrateur (défense en profondeur).
- `SaveAiSettingsValidator` et `AiSettingsMapping` (`Features/Assistant/AiSettingsUseCases.cs`) : refus à
  l'enregistrement et point masqué à la lecture.
- Les outils du point 1 (`chercher_client`, `chercher_article`, `creer_brouillon_devis`) restent en place et testés,
  avec un double de test qui ouvre le repérage (`AssistantHarness(namedEntityDetection: true)`).
- **Repli** : `AccountingAssistant` (`src/Ai/Lettra.Ai.Orchestration`) rattrape toute erreur du moteur sauf l'arrêt
  demandé ; `EngineResilience.cs` (même dossier) porte le disjoncteur (Polly, la bibliothèque de
  `Microsoft.Extensions.Http.Resilience` déjà utilisée pour l'annuaire et VIES), le classement des pannes et
  `IAiEngineHealth` (état « IA disponible »). Les adaptateurs nomment une réponse illisible (`AiEngineException`) et
  portent leur délai.
- **Sans couche IA**, chaque port d'IA de l'application a une implémentation par défaut qui coupe l'IA
  (`IAiEngineHealth.None` compris) ; un test d'architecture (`AiPortDefaultsTests`) échoue si un port d'IA n'en a pas.
- **Écran** : panneau `Components/Assistant/AssistantPanel.razor`, page `Pages/Ai/AiGuidePage.razor`, registre
  `Pages/Ai/AiUsages.cs` (`Lettra.UI.Shared`).

Rouvrir le point 1 demande une vraie implémentation de `INamedEntityDetection`, son branchement dans le masquage du
texte libre, et l'accord du DPO : ne jamais l'enregistrer sans le repérage réel.
