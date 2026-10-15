# Lettra

Logiciel de devis et de factures pour les cabinets d'experts-comptables : une base par dossier client, la facture
électronique (Factur-X, UBL), la transmission à une plateforme agréée et le travail hors ligne.

> **Prototype interne.** Lettra se présente aujourd'hui en interne, sur **données fictives uniquement**. La plateforme
> agréée est **simulée** : rien ne quitte l'ordinateur. Ne saisissez aucune donnée réelle de client.

- [Démarrer la démo sous Windows](#démarrer-la-démo-sous-windows) (5 minutes, sans installation)
- [Premier usage, pas à pas](#premier-usage-pas-à-pas)
- [Ce que la démo ne fait pas encore](#ce-que-la-démo-ne-fait-pas-encore)
- [En cas de problème](#en-cas-de-problème)
- [Pour les développeurs](#pour-les-développeurs)

---

## Démarrer la démo sous Windows

La démo est l'**application de bureau** Lettra. Elle fonctionne seule sur un PC, sans serveur, sans base de données à
installer et sans connexion Internet.

**Il faut** un PC sous Windows 10 (version 1809 ou plus récente) ou Windows 11, en 64 bits. Le composant WebView2,
présent d'office sous Windows 11 et sur un Windows 10 à jour, suffit ; rien d'autre n'est à installer.

1. **Télécharger.** Sur GitHub, ouvrez l'onglet [Actions](https://github.com/krovomi/Lettra/actions/workflows/ci.yml?query=branch%3Amain+is%3Asuccess),
   cliquez sur l'exécution la plus récente de la branche `main` (coche verte), puis, en bas de la page, sur
   l'artefact **`lettra-windows`**. Il faut être connecté à GitHub. Vous obtenez `lettra-windows.zip` (environ 160 Mo).
2. **Débloquer** (une seule fois). Clic droit sur le fichier zip, **Propriétés**, cochez **Débloquer** en bas de
   l'onglet Général, puis **OK**. Sans cela, Windows bloque les fichiers extraits un par un.
3. **Extraire.** Clic droit sur le zip, **Extraire tout…**, vers un dossier de votre choix (par exemple
   `Documents\Lettra`).
4. **Lancer.** Dans le dossier extrait, double-cliquez sur **`Lettra.App.Maui.exe`**. Le prototype n'est pas signé :
   si Windows affiche « Windows a protégé votre ordinateur », cliquez sur **Informations complémentaires**, puis
   **Exécuter quand même**. Pour la suite, clic droit sur l'exécutable, **Envoyer vers**, **Bureau (créer un raccourci)**.
5. **Créer le compte du poste.** Au premier lancement, Lettra demande votre nom et une **phrase secrète** d'au moins
   12 caractères. Elle protège les dossiers chiffrés de ce PC et **ne peut pas être récupérée** : notez-la pour la
   durée de la démo.

C'est prêt. Lettra a créé un cabinet local et un premier dossier chiffré, « Dossier principal ».

> La CI construit cet exécutable et le lance à chaque modification de `main` (job « Build MAUI (Windows) ») : il
> s'ouvre, crée le cabinet et le premier dossier sans erreur. Le parcours ci-dessous, avec ses données fictives, est
> rejoué à chaque modification par le test `DemoWalkthroughTests`.

---

## Premier usage, pas à pas

Le parcours va de la fiche de l'entreprise à l'encaissement, en une quinzaine de minutes. Les noms et numéros
proposés sont **fictifs** et acceptés par les contrôles de Lettra ; copiez-les tels quels.

Le menu, à gauche : **Devis**, **Factures**, **Avoirs**, **Clients**, **Fournisseurs**, **Catalogue**, **Alertes**,
**Entreprise**. À tout moment, **Ctrl+K** ouvre la palette de commandes (« Nouveau devis », « Nouvelle facture »).

### 1. La fiche de votre entreprise

Menu **Entreprise**. Ces mentions figurent sur chaque facture ; sans elles, Lettra refuse de valider.

| Champ | Valeur fictive |
|---|---|
| Nom ou raison sociale | Cabinet Démo Conseil |
| Forme juridique | SAS |
| Capital social (€) | 10000 |
| SIREN | 900000019 |
| Immatriculation (RCS ou RM) | RCS Paris 900 000 019 |
| N° de TVA intracommunautaire | FR25900000019 |
| Adresse, Code postal, Ville | 12 rue des Exemples, 75010, Paris |
| Taux des pénalités de retard (%) | 10 |
| Escompte pour paiement anticipé | Pas d'escompte pour paiement anticipé |

Cliquez sur **Enregistrer** : « Fiche entreprise enregistrée. » N'utilisez pas le bouton « Vérifier (VIES) » : il
interroge le service européen, qui ne connaît pas ces numéros fictifs.

### 2. Un client

Menu **Clients**. Le formulaire « Nouveau client » est en haut de la page. Saisissez tout à la main (la recherche
dans l'annuaire SIRENE ne sert qu'aux vraies entreprises) :

- Type de client : **Entreprise**
- Nom ou raison sociale : **Boulangerie Exemple**
- SIRET : **90000002700012**
- Adresse : **3 place du Marché**, **69002**, **Lyon**

Cliquez sur **Créer le client**.

### 3. Un devis

Menu **Devis**, **Nouveau devis**.

1. Client : **Boulangerie Exemple** ; Objet : *Accompagnement comptable*.
2. Dans la grille **Lignes**, une ligne : Désignation *Tenue comptable mensuelle*, Qté **3**, PU HT **250**.
   **Inser** ajoute une ligne, **Entrée** ou **Tab** passe à la cellule suivante, **F4** ouvre une liste de choix.
   Le brouillon s'enregistre tout seul.
3. Le panneau **Conformité de la pièce** liste chaque mention : « Bloquant », « À vérifier » ou « Présent ». Un point
   bloquant se corrige d'un clic, qui ouvre le bon écran.
4. **Valider** (Ctrl+Entrée). Le bouton **PDF** (Ctrl+P) produit le devis à remettre au client.
5. Le client accepte : **Accepté**, puis **Facturer**. Lettra crée la facture à partir du devis.
   (« Facture d'acompte » facture un pourcentage du devis.)

### 4. La facture

La facture s'ouvre en brouillon (menu **Factures** pour la retrouver).

1. **Valider** : « Facture validée sous le numéro FAC-… ». Le numéro est définitif, sans trou ni doublon ; la facture
   n'est plus modifiable.
2. **Télécharger le Factur-X** : le PDF lisible par tous, avec sa facture électronique intégrée. **XML UBL** donne
   l'autre format accepté par les plateformes.

### 5. La transmission à la plateforme agréée (simulée)

Sous la facture validée, le panneau **Plateforme agréée** :

1. Format **Factur-X (CII)**, puis **Transmettre à la plateforme**. La facture passe « Émise » et la plateforme
   l'indique « Déposée ».
2. **Actualiser les statuts** : « Émise », « Reçue », « Mise à disposition » apparaissent dans l'historique, comme
   avec une vraie plateforme.
3. **Déclarer un encaissement** : date du jour, Montant TTC **900**, puis **Déclarer**. « Encaissé : 900 sur 900 TTC ».

La plateforme simulée garde ses statuts en mémoire : après un redémarrage de Lettra, la facture reste émise et
transmise, mais l'historique des statuts repart de zéro.

### 6. Un avoir

Sur la facture émise, saisissez un **Motif de l'avoir** (*Geste commercial*), puis **Avoir**. L'avoir reprend toute
la facture et s'ouvre en brouillon : **Valider** lui donne son numéro. Il se retrouve dans le menu **Avoirs**.

### Et ensuite

- **Catalogue** : les articles et prestations, saisis au clavier dans une grille ; facultatif, une ligne de facture
  peut se saisir librement.
- **Alertes** : le centre d'alertes techniques (chaîne anti-fraude d'un dossier, trou de numérotation). Il ne montre
  aucune donnée de client.
- **Verrouiller** (en haut) : ferme la session ; la phrase secrète la rouvre.
- **Langue** : l'écran suit la langue de Windows (français ou anglais). Chaque client et chaque PDF peuvent avoir la
  leur (« Langue des devis et factures imprimés », « Langue du PDF »).

---

## Ce que la démo ne fait pas encore

| Fonction | État |
|---|---|
| Export FEC | Prêt côté serveur (`GET /api/v1/exports/fec`), **pas d'écran** dans l'application. |
| Assistant IA | Désactivé par défaut, sans écran ; il ne valide ni n'émet jamais rien. |
| Vraie plateforme agréée | En attente d'un partenaire ; la démo utilise la plateforme simulée. |
| Version web et serveur du cabinet | Construites et testées, mais leur installation demande Keycloak : réservée aux développeurs (voir plus bas). |
| Poste relié à un serveur, synchronisation | Prête dans l'application (fichier `lettra.json`), mais demande un serveur Lettra et Keycloak. Sur un poste isolé, la page Synchronisation indique « Ce poste ne synchronise pas ». |
| Envoi des devis et factures par e-mail | Non : on télécharge le PDF ou le Factur-X. |
| Application signée | Non : d'où l'avertissement de Windows au premier lancement. |

---

## En cas de problème

| Symptôme | Que faire |
|---|---|
| « Windows a protégé votre ordinateur » | Normal pour un prototype non signé : **Informations complémentaires**, puis **Exécuter quand même**. |
| Rien ne s'ouvre, ou une erreur sur des fichiers bloqués | Le zip n'a pas été débloqué : supprimez le dossier extrait, faites l'étape 2 (Propriétés, **Débloquer**), puis extrayez à nouveau. |
| Fenêtre blanche | Le composant WebView2 manque (vieux Windows 10) : installez « Microsoft Edge WebView2 Runtime » depuis le site de Microsoft, puis relancez. |
| « Phrase secrète incorrecte. » / « Trop d'essais » | Après plusieurs erreurs, le poste se verrouille quelques minutes. Une phrase oubliée ne se récupère pas : voir la ligne suivante. |
| Repartir de zéro (phrase oubliée, nouvelle démo) | Fermez Lettra et supprimez son dossier de données (ci-dessous). Les données de démonstration sont perdues ; le prochain lancement recrée tout. |
| « Remplissez d'abord la fiche de l'entreprise émettrice. » | Étape 1 : menu **Entreprise**. |
| Une mention reste « Bloquant » | Cliquez dessus dans **Conformité de la pièce** : Lettra ouvre l'écran à compléter (fiche entreprise ou client). |

**Où sont les données.** Tout est sur le PC, dans le dossier de données de l'application :
`%LOCALAPPDATA%\Lettra\Lettra.App.Maui\Data` (à coller dans la barre d'adresse de l'Explorateur). On y trouve le compte du poste (`compte-local.json`) et, dans `dossiers`, une base chiffrée par
dossier. La clé du cabinet est dans le coffre de Windows : copier ces fichiers sur un autre PC ne les rend pas
lisibles. Supprimer ce dossier remet Lettra à zéro.

**Mettre à jour la démo.** Fermez Lettra, téléchargez le nouvel artefact et remplacez le dossier extrait. Vos données,
rangées ailleurs, sont conservées.

---

## Pour les développeurs

Prérequis : SDK .NET 10 (`global.json`), Docker pour les tests PostgreSQL, SQL Server et Keycloak.

```bash
dotnet build Lettra.Ci.slnf     # tout sauf l'application MAUI
dotnet test Lettra.Ci.slnf      # tous les tests (hors MAUI)
```

`Lettra.slnx` contient tout ; `Lettra.Ci.slnf` exclut le projet MAUI, qui ne se construit que sous Windows avec la
charge de travail `maui-windows` (`dotnet workload install maui-windows`, puis
`dotnet build src/Hosts/Lettra.App.Maui -c Release`). Hors Windows, c'est la CI qui le construit et le lance.

### Les cinq modes de déploiement

`Lettra:DeploymentMode` choisit les adapters dans `LettraBootstrap` (le seul endroit qui connaît le mode) :

| Mode | Hôte | Dossiers | Identité |
|---|---|---|---|
| 1. Poste isolé (la démo) | Application MAUI, gateway in-process | SQLite chiffré (SQLite3 Multiple Ciphers), clé du cabinet dans le coffre de l'OS | compte local, phrase secrète |
| 2. Serveur du cabinet | API + application web (WASM) | SQLite, PostgreSQL ou SQL Server, registre Catalog | Keycloak |
| 3 et 5. PostgreSQL dédié, SaaS | API + Worker, cellule Kubernetes | une base PostgreSQL par dossier | Keycloak |
| 4. Poste relié | Application MAUI avec `lettra.json` | réplique chiffrée du dossier du serveur, synchronisée | compte local, Keycloak (PKCE) pour la synchronisation |

**Serveur et version web (mode 2).** L'API exige un realm Keycloak (`Lettra:Identity:Oidc:Authority`, MFA
obligatoire) : démarrez d'abord Keycloak comme l'explique [deploy/keycloak/README.md](deploy/keycloak/README.md),
créez-y un utilisateur dans l'organisation du cabinet, puis :

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Hosts/Lettra.Api   # https://localhost:7180
```

En développement, l'API ouvre un dossier SQLite de démonstration et la plateforme agréée simulée
(`appsettings.Development.json`). Sans Keycloak, elle s'arrête au démarrage en nommant le réglage manquant.

**Poste relié (mode 4).** Un fichier `lettra.json` dans le dossier de données de l'application relie le poste :

```json
{ "Lettra": { "Server": { "BaseAddress": "https://lettra.cabinet.example/", "Authority": "https://sso.cabinet.example/realms/lettra", "DossierId": "…" } } }
```

Le poste crée alors la réplique du dossier du serveur, envoie ses pièces au retour du réseau (en demandant
« Maintenant / Plus tard / Détail » selon la préférence) et présente les conflits à trancher.

### Structure

```
src/
  Core/            Calculations (arrondi unique, conformité), Contracts (API publique), Domain, Application (cas d'usage, ports)
  Infrastructure/  Persistence (EF Core) et un projet de migrations par provider, Catalog, Identity.Local, Identity.Oidc,
                   External (SIRENE, VIES), Observability
  Modules/         Supervision (alertes), Exports (FEC, Factur-X, UBL, CII, plateforme agréée), Sync (Client, Server)
  Composition/     Bootstrap : AddLettra(), la composition selon le mode
  Presentation/    UI.Abstractions (ports), UI.Shared (toute l'UI Blazor), UI.Gateway.Http, UI.Gateway.InProcess, Printing (PDF)
  Hosts/           Api (Minimal APIs, BFF, sert le WASM), Web (WASM), App.Maui (Blazor Hybrid), Worker (migrations, escalade)
  Ai/              Couche IA (Paperasse épinglé, pseudonymisation, orchestrateur, moteurs Claude UE et local)
tests/             un projet par couche ou module, dont Api (bout en bout, parité HTTP / in-process) et Architecture
deploy/            Helm, OpenTofu, Argo CD, Keycloak, observabilité, essai de panne local (deploy/local/essai-panne.sh)
```

### Règles tenues par les tests

| Règle | Test |
|---|---|
| Dépendances entre couches ; le domaine et l'UI ne voient jamais EF | `LayerDependencyTests` |
| Chaque cas d'usage déclare sa permission | `UseCaseRulesTests` |
| Une requête ne touche que la base du dossier ouvert (3 providers) | `Une_base_par_dossier` |
| Même résultat par HTTP et en in-process | `GatewayParityTests` |
| Le parcours de ce README fonctionne | `DemoWalkthroughTests` |
| Numérotation sans trou ni doublon sous concurrence | `Numerotation_continue_sous_concurrence` |
| Chaque code d'erreur est traduit en FR et EN | `ErrorCodeAndResourceTests` |
| Aucune donnée de dossier dans les journaux | `NoDossierDataInLogsTests` |
| Factur-X conforme PDF/A-3b et EN 16931 | CI : veraPDF et validateurs EN 16931 |

### Conventions

- Échecs métier en valeurs (`Result`, `Result<T>`), jamais en exceptions ; le serveur ne renvoie que des codes,
  traduits par module (`ErrorCodes.<Module>.cs`, `Localization/Modules/<Module>.resx`).
- Médiateur maison : journalisation, dossier, autorisation, validation.
- Avertissements bloquants, paquets centralisés (`Directory.Packages.props`), identifiants Guid v7, montants
  `decimal(18,4)`, dates UTC.
- Migrations : même identifiant dans les trois providers ; la PR fusionnée en second régénère la sienne.
- Tests nommés en français (`Une_entreprise_francaise_sans_siren_est_refusee`).

### CI

`.github/workflows/ci.yml` : build et tests Linux (Testcontainers PostgreSQL, SQL Server, Keycloak), conformité
Factur-X, build MAUI Windows avec premier lancement de la démo et artefact `lettra-windows`, gitleaks, SBOM CycloneDX,
CodeQL, avis de tiers, images signées (cosign) sur `main`. `.github/workflows/deploy.yml` valide l'IaC, les règles
d'alerte et l'essai de panne (API arrêtée, astreinte appelée en S1).

### Assistant IA

Désactivé par défaut. Il explique et rédige, ne valide ni n'émet rien, ne voit que des données pseudonymisées et
passe toujours par l'orchestrateur du serveur. Le moteur « Claude UE » exige un point d'accès en région UE (Amazon
Bedrock, Google Vertex AI) et refuse de démarrer sinon. Réglages : section `Lettra:Ai` ; la clé ne va jamais dans un
fichier versionné (`Lettra__Ai__Claude__ApiKey`).

## Licence

MIT, voir [LICENSE](LICENSE). Composants tiers : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
