# Rotation de la clé d'un cabinet

> Runbook de la cellule en ligne (mode 5). Voir [la liste des runbooks](README.md).

- **Ce que fait déjà Lettra** : le moteur Transit fait tourner les clés racines ; le Worker réenveloppe les KEK chaque
  jour (`Lettra:Worker:KeyRewrap`). L'ancienne version reste lisible jusqu'au réenveloppement.
- **Rotation à la main** (soupçon de fuite) :
  1. `bao write -f transit/keys/lettra-cabinet-<identifiant du cabinet sans tirets>/rotate` ;
  2. attendre le passage du Worker, ou le relancer ;
  3. vérifier que l'enveloppe de la KEK au catalogue est passée à la version suivante (`vault:v2:…`).
- **Ne pas** monter la version minimale de déchiffrement avant le réenveloppement de toutes les KEK du cabinet.
