# Se faire payer plus vite

## QR code de paiement

Une facture ou une facture d'acompte validée porte un QR code de virement, au format défini par le Conseil européen des
paiements (EPC069-12, version 002), reconnu par la plupart des applications bancaires européennes.

- **Sur le PDF** : à droite des conditions de paiement, pour le net à payer du document, avec la légende « Scanner pour
  payer » et une phrase d'explication, en français ou en anglais selon la langue du PDF.
- **À l'écran** : panneau « Payer par QR code » de la facture, pour le reste à payer (total TTC moins les avoirs et les
  encaissements déjà reçus). Il disparaît quand la facture est soldée.

Le code contient : le BIC (s'il est bien formé), le titulaire du compte de la fiche entreprise (à défaut, le nom de
l'entreprise), l'IBAN, le montant en euros et, en libellé, le
numéro de la facture. Ce libellé arrive sur le relevé bancaire : la règle « numéro de facture » du rapprochement le
reconnaît.

Pas de code : sur un devis, un avoir ou un brouillon ; sans IBAN valide (clé de contrôle fausse) dans la fiche
entreprise ; hors euro. Aucune donnée ne sort de Lettra : le code est calculé sur le poste.

Technique : encodeur QR (niveau de correction M, mode octet) et image PNG écrits en C# dans `Lettra.Printing`, sans
paquet supplémentaire ; le PDF reste PDF/A-3b (images sans lissage).

## Paiement par carte et devis signé en ligne

Le QR code apparaît aussi dans l'espace client, où le client peut également payer par carte et signer ses devis, avec
une facture d'acompte créée à la signature. Voir [Espace client : paiement en ligne et devis signé](espace-client.md).
