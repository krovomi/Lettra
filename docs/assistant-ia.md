# Assistant IA : points ouverts et point fermé

L'assistant IA est désactivé par défaut (section `Lettra:Ai`, démo comprise). Quand un cabinet l'active, il choisit un
moteur et les points qu'il ouvre, un par un. Les règles de conformité restent en C# ; l'assistant ne fait qu'expliquer
ou proposer, et il ne valide ni n'émet jamais rien (ADR-0006, ADR-0012).

| Point | Code | État dans Lettra |
|---|---|---|
| 1. Brouillon de devis à partir d'un texte libre | `DraftFromText` | **Fermé** tant que Lettra ne sait pas repérer les noms propres dans le texte (voir ci-dessous) |
| 3. « Pourquoi ? » sur la checklist de conformité | `ExplainCompliance` | Ouvrable par le cabinet |
| 4. Diagnostic e-facturation de la pièce ouverte | `EInvoicingDiagnosis` | Ouvrable par le cabinet |
| 6. Questions TVA et réglementaires | `RegulatoryQuestions` | Ouvrable par le cabinet |

## Pourquoi le point 1 est fermé

Avant tout envoi au moteur, Lettra masque les noms et identifiants **connus du dossier** (clients, fournisseurs,
articles). Le point 1 envoie le texte tapé par l'utilisateur : un nom de personne ou d'entreprise qui n'est pas encore
dans le dossier passerait en clair. L'AIPD (annexe I §7 et §8), l'analyse de risques (annexe J §5.2) et l'annexe F §10
exigent un repérage des noms propres (détection d'entités nommées) avant d'ouvrir ce texte libre à l'IA. Ce repérage
n'existe pas encore : le DPO a décidé le 23/10/2026 de fermer le point 1 jusque-là.

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
           Point activé, dossier non exclu, moteur présent ? ─► masquage ─► moteur
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

Rouvrir le point 1 demande une vraie implémentation de `INamedEntityDetection`, son branchement dans le masquage du
texte libre, et l'accord du DPO : ne jamais l'enregistrer sans le repérage réel.
