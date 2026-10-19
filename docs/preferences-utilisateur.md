# Préférences de l'utilisateur

Lettra retient ce que chaque utilisateur règle ou choisit souvent, pour alléger la saisie et garder sous la main ce
qui sert au quotidien. Tout se voit, se règle et s'efface dans **Paramètres › Mes préférences**
(`/settings/preferences`, menu « Paramètres », palette Ctrl+K « Mes préférences »).

## Ce qui est retenu

| Préférence | Où elle sert | Portée |
|---|---|---|
| Thème, densité | Toute l'interface | Compte |
| Langue de l'écran (automatique, français, anglais) | Textes, dates et nombres ([dates-et-langue.md](dates-et-langue.md)) | Compte |
| Écran d'accueil (un écran du menu) | Ouvert à la connexion, si l'on arrive sur l'accueil | Compte |
| Favoris et sections repliées du menu | Menu latéral ([menu-et-aide.md](menu-et-aide.md)) | Compte |
| Dossiers favoris et récents | Sélecteur de dossier, palette | Compte |
| Taux de TVA et unité des nouvelles lignes | Factures, avoirs, devis | Compte, par dossier |
| Conditions de paiement, mentions des factures | Nouvelle facture | Compte, par dossier |
| Mentions et validité (en jours) des devis | Nouveau devis | Compte, par dossier |
| Clients et articles les plus choisis | Listes de choix (client, article), en tête | Compte, par dossier |
| Dernières factures et derniers devis ouverts | Accueil « Reprendre où j'en étais », palette « Reprendre » | Compte, par dossier |

« Compte » : les préférences suivent l'utilisateur sur tous ses appareils (navigateur, PWA, applications MAUI reliées
au serveur commun). Elles sont dans la base Catalog, une ligne par utilisateur (table `UserPreferences`, document JSON
versionné).

## Dans les écrans

- **Pré-remplissage** : une nouvelle facture reprend les conditions de paiement et les mentions retenues ; un nouveau
  devis, ses mentions et sa date de validité (aujourd'hui + la validité retenue) ; une nouvelle ligne, le taux de TVA et
  l'unité retenus. Rien n'est retenu : la saisie se comporte comme avant.
- **« Retenir par défaut »** apparaît sous les conditions de paiement et les mentions dès que la valeur saisie diffère
  de celle retenue ; un clic la garde pour les prochaines pièces du dossier (toaster de confirmation).
- **Suggestions** : les clients et articles les plus choisis (puis les plus récents) remontent en tête des listes. La
  case « Classer les suggestions selon mon usage » le coupe ; « Oublier pour ce dossier » et « Oublier partout »
  effacent les choix comptés.
- **Reprendre où j'en étais** : la carte de l'accueil et la palette (Ctrl+K, vide) proposent les cinq dernières pièces
  ouvertes dans le dossier. Une pièce supprimée ou devenue inaccessible disparaît.
- **Réinitialiser mes préférences** (avec confirmation) remet tout aux valeurs de départ, sur tous les appareils.

## Données personnelles

Le compte ne retient que des réglages et des identifiants : aucun nom de client, aucun montant. Le numéro et le client
d'une pièce récente sont relus dans le dossier, avec les droits de l'utilisateur. Les volumes sont bornés (40 entrées
de menu, 50 dossiers, 30 clients ou articles et 8 pièces par genre et par dossier, textes à 500 ou 2 000 caractères).
Chacun ne lit et n'écrit que son propre compte : l'identifiant vient de la session, jamais de la requête. Les
préférences n'apparaissent ni dans les journaux, ni dans les métriques, ni dans Grafana. L'utilisateur peut tout
effacer lui-même (droit à l'effacement).

## Sans compte

Le poste isolé (mode 1) n'a pas de base Catalog, et un serveur plus ancien n'a pas la route : les préférences restent
alors **sur l'appareil**, par utilisateur, comme les réglages d'avant (la page l'indique en tête). Si le serveur ne
répond plus pendant une écriture, le changement reste sur l'appareil. Au premier passage par un compte, ce que
l'appareil retenait (menu, dossiers, thème, densité, valeurs de saisie) y est rangé, sans rien perdre.

## Comment c'est fait

- **Domaine** : `Lettra.Domain/Preferences/UserPreferences.cs` (validation, bornes, classement par usage).
- **Cas d'usage** : `Lettra.Application/Features/Preferences` (`[PublicUseCase]`, aucune permission : son propre
  compte). Écriture de la page avec la version lue (412 si un autre appareil a écrit) ; les choix comptés et les
  effacements relisent et refont l'écriture, trois fois au plus.
- **Stockage** : `IUserPreferencesStore`, implémenté par `CatalogUserPreferencesStore` (modes 2, 3 et 5) ; migration
  Catalog `PreferencesUtilisateur` pour SQLite, PostgreSQL et SQL Server. Un document illisible revient aux valeurs
  de départ.
- **API** : `/api/v1/me/preferences` — `GET`, `PUT` (enregistrer), `DELETE` (réinitialiser), `POST /usage` (compter un
  choix), `DELETE /usage?dossierId=` (oublier). Politique `User`, jeton CSRF.
- **Interface** : `IUserPreferencesGateway` (HTTP et en direct) ; `UserPreferencesState` (lecture, écriture, repli sur
  l'appareil), `PreferencesSync` (relie menu, dossiers, thème, densité, langue et écran d'accueil au compte, démarré
  par `MainLayout`), `EntryPreferences` (ce que lisent les écrans de saisie), `RecentDocuments`, composants
  `RememberDefault` et `ResumeWork`.
- **Langue** : `ScreenLanguage` retient la langue sur l'appareil (clé `lettra.langue`) et recharge la page une fois,
  car le navigateur ne charge les textes d'une langue qu'au démarrage. L'hôte la pose avant le premier rendu avec
  `ScreenLanguage.ApplyStoredAsync`.

## Tester

- Ouvrir **Paramètres › Mes préférences**, choisir l'unité « Heure », une validité de 45 jours et des mentions de
  devis, enregistrer : un nouveau devis les reprend.
- Saisir d'autres conditions de paiement dans une facture : « Retenir par défaut » apparaît dessous.
- Choisir plusieurs fois le même client : il passe en tête de la liste des clients.
- Ouvrir une facture puis revenir à l'accueil : « Reprendre où j'en étais » la propose.
- Tests : `UserPreferencesTests` (domaine, cas d'usage), `CatalogScenarios.UserPreferencesAsync` (trois bases),
  `UserPreferencesGatewayTests` (HTTP, serveur sans la route, poste isolé), `UserPreferencesUiTests` (écrans).
