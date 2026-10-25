# Annuaire SIRENE

Lettra interroge l'annuaire public des entreprises (SIRENE) pour remplir les fiches et repérer les clients qui ont
cessé leur activité. Seuls des SIREN, des SIRET ou un nom recherché partent vers l'annuaire.

## Fiche de votre entreprise

Menu **Entreprise**, champ **Rechercher l'entreprise (annuaire SIRENE)** : recherche par nom, SIREN ou SIRET, comme
pour les clients et les fournisseurs. Le choix remplit le nom, le SIREN, le SIRET, le numéro de TVA et l'adresse du
siège. La forme juridique, le capital, le greffe et les conditions de paiement restent à saisir. Rien n'est enregistré
avant **Enregistrer**.

## Clients cessés

Écran **Clients**, bouton **Vérifier dans l'annuaire** : Lettra contrôle les clients entreprises qui ont un SIREN (500
au plus), un appel par SIREN. Le résultat liste les entreprises cessées (radiées) et les SIREN inconnus, avec un lien
vers chaque fiche. Une réponse manquée n'arrête pas le contrôle ; trois d'affilée l'arrêtent, avec le message
« L'annuaire n'a pas répondu pour certains clients ». Le contrôle se lance à la demande : aucune alerte automatique.

API : `GET /api/v1/directory/customer-check`, permission « consulter les tiers ».

## Dans la démonstration

L'hôte de l'API ajoute `AddDemoCompanyDirectory` : un annuaire fictif, tiré du même scénario que le jeu « Cabinet
Démo » (entreprises des trois dossiers, clients, fournisseurs), répond avant l'annuaire réel. Les clients
« défaillants » du jeu y sont cessés. Toute autre recherche part vers l'annuaire réel. Rien de cela hors
`Lettra:Demo:Seed=true`, ni en Production. L'application installée (MAUI) n'a pas cet annuaire fictif : sa recherche
interroge l'annuaire réel.
