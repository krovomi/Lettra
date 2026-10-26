# En bref

Le dirigeant d'une entreprise veut savoir en dix secondes, depuis son téléphone, comment va le mois. « En bref »
rassemble les chiffres que Lettra calcule déjà pour d'autres écrans, sans règle de calcul nouvelle.

**Où** : menu, juste sous l'Accueil (`/brief`). Chaque carte mène à l'écran détaillé.

## Les cartes

| Carte | D'où vient le chiffre |
|---|---|
| Chiffre d'affaires HT du mois | L'accueil : factures et avoirs émis ce mois, et l'évolution par rapport au mois précédent |
| Encaissé ce mois | Les encaissements enregistrés depuis le 1er du mois, sans les encaissements contre-passés |
| TVA à payer (ou crédit de TVA) | La déclaration de TVA de la période en cours (mois, trimestre ou année selon le régime), avec sa date limite |
| Mouvements bancaires du mois | Le relevé importé : entrées moins sorties depuis le 1er du mois, opérations ignorées comprises |
| Factures en retard | Le point des créances de l'accueil |

## À surveiller

Les encaissements des 30 derniers jours à partir d'un seuil (500, 1 000, 5 000 ou 10 000 €, 1 000 € par défaut), les
plus récents d'abord, puis les factures en retard, les plus anciennes d'abord.

## Droits

Chaque carte suit le droit de l'écran d'où elle vient. Une carte dont la lecture est refusée ne s'affiche pas, sans
message d'erreur ; une panne du serveur s'affiche une fois.

## Architecture

L'écran vit dans la couche d'interface et ne lit que cinq passerelles existantes (accueil, encaissements,
rapprochement bancaire, déclaration de TVA, point des créances). Pas de nouvelle route d'API, pas de nouveau cas
d'usage côté serveur, pas de migration.

## Limites

- La carte bancaire montre les mouvements du mois, pas le solde du compte : Lettra ne lit pas encore le solde dans
  les relevés.
- L'idée d'origine prévoyait une notification sur le téléphone quand une grosse facture est payée ou passe en retard.
  Elle n'est pas faite (il faudrait un service de notifications push) : l'écran se consulte.
- Au-delà de 200 encaissements sur la période, le montant encaissé est donné comme un minimum.

## Vérifié

Démo Docker, navigateur sur ordinateur et téléphone.
