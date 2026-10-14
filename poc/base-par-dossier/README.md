# POC « base par dossier » (phase 1, échelle d'une cellule)

Ce POC répond à la question de l'[annexe C](../../architecture/annexes/C-plan-implementation.md) (POC « Échelle d'une cellule », ADR-0003, ADR-0009, ADR-0011) côté **persistance** : un seul `DbContext` peut-il servir une base par dossier sur SQLite chiffré, PostgreSQL et SQL Server, avec un catalogue, des migrations orchestrées sur des milliers de bases, et **aucune requête vers le mauvais dossier** ?

Il est autonome : sa propre solution (`Lettra.Poc.BaseParDossier.slnx`), ses `Directory.*.props`, rien à la racine du dépôt. Les noms de projets et d'espaces de noms sont ceux de l'[annexe A §1](../../architecture/annexes/A-blueprint-technique.md) pour que le code puisse être repris tel quel en phase 2.

## Verdict

| Critère de l'annexe C | Résultat | Preuve |
|---|---|---|
| 3 fournisseurs, un seul modèle | **Go** | 21 tests verts sur SQLite chiffré, PostgreSQL 16 et SQL Server 2022 (même suite, `PersistenceSuite`) |
| Migration orchestrée | **Go** | 2 500 bases migrées en 67 s (37 bases/s, p95 544 ms par base), canari de 25 bases d'abord, reprise après coupure, bail par dossier, arrêt automatique au-delà de 5 % d'échecs |
| Aucune requête vers le mauvais dossier | **Go** | 9 859 factures écrites sous charge sur 2 500 bases : 0 dans le mauvais dossier ; 1 000 routages volontairement faux : 1 000 bloqués avant toute requête |
| Numérotation sans trou sous concurrence | **Go** (après correction, voir constat 4) | 120 validations parallèles sur un dossier, et 2 500 séries contrôlées : 0 trou, 0 doublon |
| 10 000 bases sur un cluster derrière PgBouncer | **Non prouvé ici** | 2 500 bases créées : le disque du conteneur (≈ 26 Go) ne permet pas plus. Une base vide pèse 7,6 Mo : **10 000 bases = 76 Go** avant la première facture. À refaire sur la cellule de recette |
| Débit de validations par dossier | **Mesuré** | 115 validations/s sur un seul dossier (16 fils en concurrence) ; débit global selon le nombre de dossiers actifs, voir ci-dessous |
| Cellule créée par OpenTofu et Argo CD, tenue de Keycloak | **Hors périmètre** | Ce POC ne couvre que la persistance |

**Conclusion** : le modèle « une base par dossier » tient. La limite d'un cluster n'est pas le nombre de bases (2 500 bases inactives ne coûtent presque rien en CPU), c'est **le nombre de dossiers actifs en même temps** comparé au budget de connexions serveur, parce que PgBouncer ne peut pas réutiliser une connexion d'une base pour une autre (constat 1).

## Mesures

Machine : 1 VM, 4 vCPU, 16 Go, PostgreSQL 16.15, PgBouncer 1.22 (mode transaction, 120 connexions serveur pour l'application), SQL Server 2022 en conteneur, .NET 10.0.1xx, EF Core 10.0.13, Npgsql 10.0.3. PostgreSQL, PgBouncer et le banc tournent sur les mêmes 4 vCPU : les valeurs absolues sont pessimistes, ce sont les **tendances** qui comptent.

| Opération | Résultat |
|---|---|
| Création d'un dossier (CREATE DATABASE + 2 migrations + estampille), 16 en parallèle | 13,3 dossiers/s, p50 1,16 s, p95 1,68 s (soit ≈ 13 min pour 10 000) |
| Taille d'une base de dossier vide | 7,6 Mo (catalogues système de PostgreSQL) |
| CPU de PostgreSQL au repos avec 2 500 bases | < 1 % (l'autovacuum ne pose pas de problème à ce volume) |
| Vague de migration sur 2 500 bases, 16 en parallèle | 67 s, 0 échec, p50 258 ms, p95 544 ms par base |
| Retour arrière de 2 500 bases (outil du POC) | 60 s |
| Contrôle d'isolation complet des 2 500 bases | 58 s |
| Ouverture d'un dossier SQLite chiffré | 98 ms à froid (dérivation de clé), 2,8 ms à chaud (pool) |

Banc de validations (64 fils ; chaque opération = résolution par le catalogue, ouverture avec garde, création d'un client et d'une facture, validation numérotée) :

| Dossiers actifs | Validations/s | Ouverture + garde p95 | Total p95 |
|---|---|---|---|
| 1 (16 fils) | 115 | 50 ms | 260 ms |
| 50 | 262 | 86 ms | 506 ms |
| 120 | 167 | 144 ms | 667 ms |
| 500 | 117 | 330 ms | 866 ms |
| 1 000 | 93 | 520 ms | 1,1 s |
| 2 500 | 54 | 5,0 s | 5,3 s |

Dès que les dossiers actifs dépassent le budget de connexions serveur (120 ici), chaque requête paie l'ouverture d'un nouveau processus PostgreSQL sur une autre base : le CPU passe à 43 % en système (création de processus) et la latence décroche.

## Constats à reporter dans l'architecture

Les documents d'architecture appartiennent au fil « Plan du projet » ; ce POC ne les modifie pas. Voici ce qu'il faudrait y corriger ou y ajouter.

1. **Annexe E §« Une base par dossier, à grande échelle » et ADR-0009** : la cible « 10 000 bases par cluster » est à reformuler en deux limites distinctes. (a) **Disque** : 7,6 Mo par base vide, donc 76 Go de socle pour 10 000 bases, à intégrer au dimensionnement et au coût. (b) **Dossiers actifs simultanés** : PgBouncer garde un pool par couple (base, utilisateur) et ne mutualise rien entre bases. Règle de dimensionnement proposée : budget de connexions serveur ≥ dossiers actifs au même moment (et non utilisateurs), `max_connections` de PostgreSQL en conséquence, et éviction des connexions inactives courte (`server_idle_timeout`). L'hibernation des dossiers inactifs reste utile pour le disque, pas pour les connexions.
2. **Annexe A §3.5** : `HasDefaultSchema(tenant.Schema)` sans clé de cache de modèle fait réutiliser le schéma du premier dossier pour tous les autres. Le POC ajoute un `IModelCacheKeyFactory` (`LettraDbContext.cs`).
3. **Annexe A §2 point 6, ADR-0008 et §3.7 (« SQLCipher »)** : `SQLitePCLRaw.bundle_e_sqlcipher` n'est plus maintenu, et le SQLCipher officiel est sous licence commerciale. Le POC utilise **SQLite3 Multiple Ciphers** (`SQLite3MC.PCLRaw.bundle`, licence MIT, ChaCha20-Poly1305 par défaut, sait aussi lire le format SQLCipher). À acter dans le blueprint.
4. **Numérotation (ADR-0007, critère J2)** : la concurrence optimiste « naïve » d'EF (`SaveChanges` qui met à jour la série et la facture) produit des violations d'unicité sous concurrence, parce qu'EF peut écrire la facture avant la série. Le POC réserve le numéro par un `UPDATE … WHERE Version = @lue` **en premier**, dans une transaction explicite (`InvoiceValidation.cs`). À reprendre tel quel dans le module Facturation.
5. **Composition root (annexe A §3.6)** : `Microsoft.Data.SqlClient` refuse `InvariantGlobalization`. Le mode 2 sur SQL Server interdit donc cette option.
6. **Mode 2 (LAN)** : le catalogue du POC est en PostgreSQL. Un serveur de cabinet sous SQL Server demandera les migrations du catalogue pour SQL Server aussi (non fait).
7. **Convention de migrations** : les trois assemblies doivent porter **le même identifiant** de migration (`20261014000001_Initial`…) pour que le Worker compare les versions d'un fournisseur à l'autre. `dotnet ef` génère des horodatages différents par projet : on renomme après génération.
8. **Résolution du dossier** : le droit d'accès est relu dans le catalogue à chaque requête (pas de cache, pour qu'un retrait de droit soit immédiat). Sous charge, c'est un aller-retour de plus vers le plan de contrôle : à porter par le jeton (Keycloak) ou par un cache court invalidé à la modification des droits.

## Ce que fait le code

```
src/
  Lettra.Domain                          Customer, Invoice, NumberSeries, DossierIdentity
  Lettra.Application.Abstractions        DeploymentMode, TenantDescriptor, ITenantResolver, ITenantCatalog, ISecretStore, IDossierKeyring
  Lettra.Infrastructure.Persistence      LettraDbContext (un modèle), fabriques, garde d'identité, TenantMigrator, validation numérotée
  Lettra.Infrastructure.Persistence.Sqlite      configurateur + migrations + dossiers locaux chiffrés (modes 1 et 4)
  Lettra.Infrastructure.Persistence.PostgreSql  configurateur + migrations + CREATE/DROP DATABASE
  Lettra.Infrastructure.Persistence.SqlServer   configurateur + migrations + CREATE/DROP DATABASE
  Lettra.Infrastructure.Security         KEK par cabinet, DEK par dossier (AES-256-GCM), références de secrets par cluster
  Lettra.Infrastructure.Catalog          base Catalog, résolveur avec contrôle d'accès, provisioning, vagues de migration
  Lettra.Composition                     AddLettraPersistence(config) : le seul endroit qui connaît le mode
  Lettra.Worker                          commandes init, provision, migrate, downgrade, bench, isolation, stats
tests/
  Lettra.Persistence.Tests               même suite sur les 3 fournisseurs + chiffrement + catalogue
```

- **Garde d'identité** : chaque base contient une ligne `DossierIdentity` écrite à sa création. La fabrique la relit avant de rendre le `DbContext` ; une base qui n'appartient pas au dossier actif lève `DossierMismatchException` avant toute requête métier. Le migrateur fait la même vérification avant de modifier une base existante. En local, la clé liée au dossier bloque déjà l'ouverture d'un autre fichier.
- **Secrets** : le catalogue stocke une référence (`cluster:pg-a/d_…`), jamais une chaîne de connexion ; les identifiants du cluster viennent de la configuration (coffre de la cellule).
- **Clés (ADR-0011)** : une DEK de 256 bits par dossier, enveloppée par la KEK du cabinet avec l'identité du cabinet et du dossier en données associées. Détruire l'enveloppe rend le dossier illisible (testé). Le POC garde la KEK dans un fichier : c'est le bouchon du KMS / coffre de l'OS. En PostgreSQL et SQL Server, la DEK est créée et stockée mais pas encore utilisée (chiffrement de champs : phase 2).
- **Vagues de migration** : bail par dossier dans le catalogue, plusieurs Workers possibles, reprise en relançant la commande, `--limit` pour le canari, arrêt de la vague au-delà de 5 % d'échecs. EF Core 10 ajoute son propre verrou par base.

## Lancer

Prérequis : SDK .NET 10, PostgreSQL 16 local, SQL Server (conteneur `mcr.microsoft.com/mssql/server:2022-latest`), PgBouncer pour le Worker.

```bash
# Tests (connexions par défaut : PostgreSQL 127.0.0.1:5432 user postgres, SQL Server 127.0.0.1:1433 sa / Lettra#Poc2026)
export LETTRA_TEST_PG="Host=127.0.0.1;Port=5432;Username=postgres;Password=..."
export LETTRA_TEST_MSSQL="Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=true"
dotnet test --project tests/Lettra.Persistence.Tests

# Échelle (voir src/Lettra.Worker/appsettings.json et pgbouncer/lettra.ini)
dotnet build -c Release src/Lettra.Worker
W="dotnet src/Lettra.Worker/bin/Release/net10.0/Lettra.Worker.dll"
$W init --orgs 50
$W provision --count 2500 --parallel 16
$W downgrade && $W migrate --limit 25 && $W migrate
$W bench --seconds 60 --concurrency 64 --hot 500
$W isolation --swaps 1000
```

Un serveur absent fait passer ses tests en « ignoré » avec la raison, jamais en vert.

## Hors périmètre

Cellule décrite en OpenTofu et Argo CD, tenue de Keycloak, hibernation des dossiers, chiffrement de champs côté serveur, plusieurs nœuds API, et le vrai palier de 10 000 bases : ils relèvent de la cellule de recette (phase 2) ou d'autres fils.
