# POC synchronisation hors ligne

POC de la phase 1 de l'annexe C : *faits et intentions tiennent-ils les coupures et les conflits ?* Il suit ADR-0007, ADR-0008 et l'annexe A §3.7.

```bash
dotnet test                                              # 34 tests, ~2 s
dotnet run --project src/Lettra.Poc.Sync.Demo            # démo interactive (M / P / D)
dotnet run --project src/Lettra.Poc.Sync.Demo -- --auto  # même scénario, sans clavier
```

Prérequis : SDK .NET 10. Aucune dépendance au reste du dépôt.

## Verdict proposé : go

| Critère de sortie (annexe C) | Résultat | Tests |
|---|---|---|
| MAUI valide hors ligne dans la série du poste | ✅ `F-2026-P01-000001…`, deux postes sans collision, conformité locale | `HorsLigneMauiTests` |
| Le navigateur ne garde que des brouillons, numérotés à la synchro | ✅ `F-2026-SRV-…` attribué par le serveur ; stockage effacé = aucun trou | `NavigateurTests` |
| Demande à l'utilisateur au retour du réseau | ✅ Maintenant / Plus tard / Détail, préférences, portail captif, rappels 24 h / 72 h | `DemandeUtilisateurTests` |
| Coupure simulée à chaque étape, reprise sans doublon | ✅ 8 coupures (requête ou réponse perdue × 4 étapes), coupure au milieu d'un lot, redémarrage de l'app | `CoupuresTests` |
| Factures validées jamais en conflit | ✅ le fait est revérifié (série, chaîne de hash, totaux) puis accepté | `HorsLigneMauiTests`, `SecuriteTests` |
| Conflit de brouillon résolu champ par champ | ✅ champs sans conflit appliqués, champ disputé en attente, fusion saisie, autres brouillons non bloqués | `ConflitsTests` |
| Appareil révoqué, jeton expiré pendant le hors ligne (ADR-0008, Vérification) | ✅ effacement local ; reconnexion exigée avant toute écriture | `SecuriteTests` |

## Ce que le POC a appris (à reporter dans les ADR)

1. **Rotation du jeton de rafraîchissement et réponse perdue.** Si la réponse d'un rafraîchissement se perd, le poste garde l'ancien jeton, déjà remplacé côté serveur : il est bloqué jusqu'à une reconnexion complète. Correctif appliqué : l'ancien jeton reste accepté tant que le nouveau n'a pas servi. À reporter dans ADR-0010 / annexe F (réglage Keycloak « refresh token reuse »).
2. **La validation différée du navigateur compare le contenu, pas la version.** ADR-0008 parle d'`ETag` de la version de départ. Pour `ValidateDraft`, c'est faux : les propres modifications du poste, rejouées juste avant, font avancer la version. Le POC envoie l'empreinte du contenu vu par l'utilisateur ; le serveur ne numérote que si le brouillon est identique.
3. **Un fait écrase un brouillon modifié au bureau entre-temps.** C'est la règle « un fait n'est jamais en conflit », mais la modification du collègue est perdue. Le POC le signale dans le compte rendu de synchro. À trancher dans ADR-0013 : prévenir le collègue, ou verrouiller un brouillon « disponible hors ligne » pour les autres.
4. **Une entrée d'outbox n'est jamais réécrite.** Fusionner après coup une création et ses retouches casserait l'`Idempotency-Key` si la première requête était arrivée sans réponse. Les intentions couvertes par un fait du poste (création puis validation hors ligne) ne sont simplement pas envoyées.
5. **Rien à envoyer : réception sans question.** Choix du POC, à confirmer : la demande porte sur ce qui part ; si l'outbox est vide, on reçoit les changements du serveur sans dialogue.

## Hors du périmètre du POC

UI Blazor/MAUI réelle (le dialogue est un port `ISyncConsentPrompt`, joué en console et dans les tests), SQLCipher et coffre de l'OS, IndexedDB (le navigateur est simulé par la même base locale avec la politique « brouillons seulement »), Keycloak (jetons simplifiés), PostgreSQL et outbox serveur (état serveur en mémoire sous verrou), plusieurs dossiers par poste, compaction de l'outbox.

## Structure

| Projet | Rôle |
|---|---|
| `Lettra.Poc.Sync.Contracts` | Faits, intentions (`CreateDraft`, `UpdateDraft`, `ValidateDraft`), résultats. Même format pour l'appel HTTP et le journal hors ligne. |
| `Lettra.Poc.Sync.Domain` | Facture, conformité minimale, numérotation par série, chaîne de hash, fusion à trois voies champ par champ. Partagé client et serveur. |
| `Lettra.Poc.Sync.Client` | Base locale SQLite (facture + outbox dans la même transaction), cas d'usage hors ligne, `SyncCoordinator`, client HTTP, sonde réseau. |
| `Lettra.Poc.Sync.Server` | API minimale : `/api/health`, `/api/auth/*`, `/api/sync/facts`, `/api/sync/intents`, `/api/sync/changes`. Idempotence par clé et empreinte de requête. |
| `Lettra.Poc.Sync.Demo` | Scénario console : MAUI + navigateur hors ligne, collègue au bureau, retour du réseau, conflit résolu. |
| `Lettra.Poc.Sync.Tests` | xUnit + `WebApplicationFactory`, réseau simulé (`FlakyNetwork`) qui coupe la requête ou la réponse à l'étape voulue. |
