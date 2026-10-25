# Passerelles vers les logiciels du cabinet

Le cabinet garde son logiciel de production : Lettra lui remet les écritures du dossier dans le format que ce logiciel importe.

**Où** : Comptabilité > FEC, section « Vers le logiciel du cabinet ». Choisir le logiciel, puis « Exporter les écritures ». La période est celle de l'exercice saisi en haut de l'écran. Permission : celle de l'export du FEC.

**Quoi** : les mêmes lignes que le FEC (journal des ventes VE et journal de banque BQ), sans aucun recalcul. Le compte client 411 porte le compte auxiliaire du client.

| Logiciel | Fichier | Particularités |
|---|---|---|
| Quadra | `.txt`, ASCII à positions fixes, ISO 8859-15 | une ligne `C` par client (compte de 8 caractères rattaché au collectif 41100000), puis une ligne `M` par ligne d'écriture ; dates JJMMAA ; montants en centimes signés |
| Sage 100 | `.csv`, point-virgule, ISO 8859-15 | une ligne d'en-tête ; dates JJMMAA ; virgule décimale ; à charger avec un modèle d'import paramétrable créé une fois |
| Cegid Expert | `.csv`, point-virgule, UTF-8 | modèle « écritures Excel », sans en-tête ; dates JJ/MM/AAAA ; point décimal ; pièce et libellé à 35 caractères |
| ACD | `.csv`, point-virgule, ISO 8859-15 | une ligne d'en-tête ; dates JJ/MM/AAAA ; virgule décimale ; libellé du client inclus ; à charger avec un modèle d'import paramétrable |

Les comptes généraux sont complétés de zéros à droite : 706 devient 706000 (70600000 dans Quadra).

**À relire avec un cabinet utilisateur** : longueur des comptes, forme des comptes auxiliaires, colonnes des imports paramétrables de Sage et d'ACD. Ces choix sont regroupés dans un seul fichier du code (BridgeWriters) ; aucun import n'a encore été essayé dans un vrai logiciel de production.

**API** : `GET /api/v1/exports/entries?software=Quadra|Sage|Cegid|Acd&from=AAAA-MM-JJ&to=AAAA-MM-JJ`. Refus : mêmes que le FEC (période, fiche entreprise, SIREN), plus `Fec.SoftwareUnknown` pour un logiciel inconnu.
