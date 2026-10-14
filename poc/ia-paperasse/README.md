# POC « IA métier » : couche IA sur les skills Paperasse

POC de la phase 1 du plan (annexe C, ligne « IA métier »), selon l'[annexe B](../../architecture/annexes/B-couche-ia-paperasse.md), l'[ADR-0006](../../architecture/adr/0006-conformite-deterministe-ia-assistante.md) et l'[ADR-0012](../../architecture/adr/0012-ia-confidentielle.md). Il est autonome : sa propre solution `LettraAiPoc.slnx`, sans dépendance au socle.

**Question à trancher** : l'orchestrateur et la skill `comptable` sont-ils exacts et sûrs ?

## Lancer

Prérequis : SDK .NET 10. Node.js 22 est facultatif (il sert à régénérer et revérifier les résultats de référence Paperasse).

```bash
dotnet test                                                     # 87 tests : parité, garde-fous, adapters, évals, API
dotnet run --project src/Lettra.Ai.Evals -- --details           # banc d'évals sur le moteur simulé
dotnet run --project src/Lettra.Ai.PocApi                       # API de démonstration
node tests/parity/harness.js                                    # régénère les résultats de référence Paperasse
node tools/update-skillpack-manifest.js --reviewed-on 2026-10-20 --reviewed-by "Nom"  # après une montée de version
```

Moteurs réels, si les accès existent :

```bash
ANTHROPIC_API_KEY=... dotnet run --project src/Lettra.Ai.Evals -- --engine claude --details
LETTRA_LOCAL_MODEL_URL=http://serveur:8000/v1/chat/completions LETTRA_LOCAL_MODEL=mistral-small \
  dotnet run --project src/Lettra.Ai.Evals -- --engine local --details
```

**Aucune clé d'API Claude n'était disponible pour ce POC.** Tout tourne et est testé sur un moteur simulé déterministe (`Lettra.Ai.Stub`). Les adapters Claude et modèle local sont testés contre un faux serveur HTTP (forme des requêtes, cache du prompt, outils stricts, appels d'outils, refus), pas contre un vrai modèle.

## Ce qui est construit

| Projet | Rôle |
|---|---|
| `Lettra.Compliance` | Règles déterministes portées en C# : mentions obligatoires (`validate-facture.js`), calculs (`calc.js`), totaux par taux en décimal exact. Aucune dépendance IA. |
| `Lettra.Ai.Abstractions` | Ports : `IAiTool` (pouvoir `ReadOnly` ou `CreatesDraft`, rien d'autre n'est exprimable), `IAiEngine`, `IAccountingAssistant`, `IAiUsagePolicy`, `IAiAuditLog`. |
| `Lettra.Ai.Skills` | Skill pack Paperasse épinglé sur `f2e15c4`, embarqué, vérifié fichier par fichier par SHA-256 au démarrage ; surcouche Lettra ; lecture des fiches en liste blanche. |
| `Lettra.Ai.Privacy` | Pseudonymisation : identités connues du dossier et détecteurs (SIREN, SIRET, TVA, IBAN, e-mail, téléphone), ré-identification côté serveur. |
| `Lettra.Ai.Tools` | 8 outils : `lire_reference`, `lister_references`, `contexte_dossier`, `valider_facture`, `calculer`, `rechercher_client`, `brouillon_devis` (seul outil qui crée quelque chose : un brouillon), `consulter_site_officiel`. |
| `Lettra.Ai.Orchestration` | Orchestrateur côté serveur : politique du cabinet, droits, minimisation, boucle d'outils, validation des entrées, contrôle de la réponse, journal. |
| `Lettra.Ai.Claude` | Adapter Claude (SDK officiel C#, `claude-opus-5-5`, effort `medium`, prompt stable en cache, outils stricts). |
| `Lettra.Ai.Local` | Adapter modèle à poids ouverts (API compatible chat completions : vLLM, Ollama…). |
| `Lettra.Ai.Stub` | Moteur simulé déterministe pour les tests et la démo sans clé. |
| `Lettra.Ai.Evals` | Banc d'évals (14 cas) rejouable sur chaque moteur, avec contrôle de fuite de données. |
| `Lettra.Poc.Application`, `Lettra.Ai.Composition`, `Lettra.Ai.PocApi` | Dossier fictif en mémoire, composition root, API minimale de démonstration. À remplacer par le socle. |

## Résultats

### Parité avec les scripts Paperasse

Les scripts Paperasse sont exécutés tels quels (contexte Node isolé, date fixée) sur un jeu de cas généré de façon déterministe ; les résultats sont commités dans `tests/parity/golden/` et rejoués en C#. Un test relance le banc Node quand Node est présent et échoue si les résultats commités ne correspondent plus aux scripts épinglés.

| Règle portée | Cas | Écarts |
|---|---|---|
| Mentions obligatoires (`validate-facture.js`) | 1 006 factures (717 bloquées, 247 avec avertissements) | **0** |
| Calculs (`calc.js` : CCA, amortissement, IS, acomptes TVA, prorata, refus de paramètres) | 1 010 | **0** |
| Totaux de facture (`computeTotals` de `generate-facturx.js`) | 1 000 | 689 identiques ; 311 écarts tous expliqués : 78 arrondis flottants faux de la référence, 113 taux à 0 % que la référence remplace par 20 %, 120 où Lettra calcule la TVA sur le HT arrondi au centime ; 0 inexpliqué |

Contrôle du banc lui-même : décaler d'un jour la date d'entrée en vigueur de la réforme dans le port C# fait apparaître 30 écarts.

### Garde-fous testés

- IA désactivée par défaut : sans réglage du cabinet, le moteur n'est jamais appelé. Fonction par fonction, dossier exclu, droit `Ia.Utiliser`.
- Aucun nom, SIREN, SIRET, numéro de TVA, IBAN, e-mail, adresse ou téléphone ne part en clair vers le moteur, y compris dans le contenu importé et les résultats d'outils (vérifié sur tout ce que le moteur reçoit, à chaque cas du banc).
- Un outil qui valide, émet, envoie, exporte ou supprime ne peut pas être déclaré ; un appel à un outil inconnu ou à une entrée invalide est refusé et journalisé.
- Le brouillon créé par l'IA reste un brouillon d'origine `AssistantIA` ; `ValidateDraft` lève une exception si l'acteur n'est pas humain.
- Seule la facture ouverte à l'écran est lisible.
- Réponse ré-identifiée côté serveur, sans image ni lien hors sites officiels ; montants non issus d'un outil et jetons inventés signalés.
- Consultation web : liste blanche de domaines, https, sans paramètres, et jamais de valeur réelle dans l'URL.
- Skill pack : un fichier modifié, ajouté ou retiré empêche le démarrage.
- Refus du moteur et boucle d'outils sans fin gérés.

### Banc d'évals (moteur simulé)

14/14, dont 11/11 sur les cas bloquants et de sécurité. Sur le moteur simulé, ce score prouve la plomberie, pas la qualité d'un modèle : il faut le rejouer sur Claude et sur le modèle local.

## Critères de sortie (annexe C, POC « IA métier »)

| Critère | État |
|---|---|
| Outils branchés sur les cas d'usage | ✅ sur une couche application de POC ; à rebrancher sur les cas d'usage du socle |
| Premier jeu d'évals validé par l'expert | ⏳ 14 cas écrits et passés sur le moteur simulé ; à faire relire par l'expert-comptable référent, puis à rejouer sur Claude et sur le modèle local |
| Aucune action d'écriture sans relecture humaine | ✅ prouvé par les tests |
| (Phase 6, en avance) un test vérifie qu'aucun nom, SIREN ou IBAN ne part en clair | ✅ |

Proposition de verdict pour J1 : **go sous condition** d'un passage du banc sur Claude en région européenne (clé à fournir) et de la relecture des cas par l'expert.

## Ce que le POC change dans l'architecture

Points à reporter dans les documents d'architecture (propriété du thread « Plan du projet ») :

1. **Annexe B §4.2 et §5.4 : la boucle d'outils doit rester dans l'orchestrateur, pas dans le `BetaToolRunner` du SDK.** Chaque résultat d'outil doit être pseudonymisé et contrôlé avant de repartir vers le moteur, et la même boucle doit servir au modèle local. L'adapter Claude ne fait donc qu'un appel `Messages.Create` (API non bêta) par tour.
2. **Annexe B §5.1 : le contrat `IAiTool` a besoin de trois ajouts** : le droit exigé (`RequiredPermission`), les fonctions IA où l'outil est proposé (`Features`), et `ReceivesRealValues` (un outil qui sort du serveur, comme la consultation web, reste sur les jetons). Le contexte d'outil porte le document ouvert, pour la minimisation.
3. **Annexe B §3.1 : `generate-facturx.js` ne peut pas servir de référence pour les montants.** Il calcule en virgule flottante avec un seul taux : sur 1 000 cas, 78 arrondis faux au demi-centime, des totaux où HT + TVA ≠ TTC affiché, un taux de 0 % remplacé par 20 %, pas de ventilation par taux. Lettra calcule en décimal exact par taux ; la parité des totaux se définit comme « tout écart est expliqué ».
4. **Écarts du référentiel `validate-facture.js` à arbitrer avec l'expert** (conservés pour la parité, listés dans `InvoiceComplianceValidator.KnownReferenceQuirks`) : conditions émetteur non évaluées (RCS exigé d'un entrepreneur individuel), condition de date de livraison évaluée sur l'adresse, indemnité de 40 EUR non contrôlée, B2B deviné par le nom, montants non recontrôlés.
5. **Fraîcheur du skill `comptable`** : il date du 18/04/2026 et demande lui-même une vérification au-delà de six mois, soit à partir du 19/10/2026. La première relecture par l'expert est donc due avant toute démonstration en commission.
6. **Pseudonymisation, limite connue** : les noms de personnes ou d'entreprises absents du dossier (un tiers cité dans un e-mail) ne sont pas détectés ; seuls les identités du dossier et les identifiants structurés le sont. À inscrire dans l'AIPD, ou à compléter par une détection d'entités nommées.
7. **Nom d'outil** : « valider_facture » (annexe B §5.3) évoque une validation. Le registre le tolère en lecture seule, mais « verifier_facture » serait plus clair pour le modèle comme pour le journal.
8. **Refus du moteur** : le repli automatique sur un autre modèle n'est pas activé, car il enverrait les données à un moteur que le cabinet n'a pas choisi, et il n'existe pas sur Bedrock ni Vertex. Le refus est affiché et journalisé.
9. **Évals Paperasse** : leurs assertions sont en langage naturel et demandent un juge (modèle ou expert). Cinq des dix évals `comptable` (configuration, clôture) sont hors du périmètre de Lettra ; les évals 6, 9 et 10 sont reprises sous forme vérifiable dans notre banc.

## Pas encore fait

Passage sur un vrai modèle (Claude et modèle local), relecture des évals par l'expert, outils `rechercher_compte_pcg` et `exporter_fec_revue`, points d'IA 2 (photo) et 5 (pré-contrôle fiscal), streaming vers l'UI, agent Vigie.
