# Documentation de Lettra

## Principes de conception

Le code C# (.NET 10) suit la Clean Architecture et les principes SOLID, KISS et DRY, vérifiés à chaque modification
plutôt que simplement déclarés : tests d'architecture (dépendances vers le domaine, domaine sans framework, ports
déclarés par l'application et implémentés par l'infrastructure, passerelles HTTP et en processus au même résultat),
zéro avertissement, 21 règles de style de l'IDE en erreur de build, couverture minimale imposée en CI. Le résumé est
dans le [README](../README.md#principes-de-conception), le détail, les mesures et ce qui reste ouvert dans
[Qualité du code et couverture des tests](qualite-du-code.md).

## Documents

- [Accompagnement, aide à la saisie et voix dans l'interface](aide-a-la-saisie.md)
- [Assistant IA : points ouverts et point fermé](assistant-ia.md)
- [Dates et nombres dans la langue de l'écran](dates-et-langue.md)
- [Déclarations : TVA, DES et calendrier fiscal](declarations-fiscales.md)
- [Démo commentée](demo-commentee.md)
- [Encaissements, rapprochement bancaire et lettrage](encaissements.md)
- [Factures récurrentes](factures-recurrentes.md)
- [Indicateurs de pilotage dans Grafana](indicateurs-grafana.md)
- [Kit contrôle fiscal](kit-controle-fiscal.md)
- [Menu réglable et aide](menu-et-aide.md)
- [Passerelles vers les logiciels du cabinet](passerelles-logiciels-cabinet.md)
- [Préférences de l'utilisateur](preferences-utilisateur.md)
- [Qualité du code et couverture des tests](qualite-du-code.md)
- [Relances des factures échues et envoi d'e-mails](relances-et-e-mails.md)
- [Se faire payer plus vite](se-faire-payer.md)
- [Serveur commun : les mêmes données sur tous les appareils](serveur-commun.md)
- [Supprimer une ligne d'un tableau](suppression-des-lignes.md)
- [Tableaux de bord dans Lettra](tableaux-de-bord.md)
- [Trier et filtrer les listes](tris-et-filtres.md)
- [Vue cabinet : tous les dossiers d'un coup d'œil](vue-cabinet.md)

Système de design : [design/systeme-de-design.md](design/systeme-de-design.md) et [design/pages-de-consultation.md](design/pages-de-consultation.md).
