# company.json virtuel

Lettra génère le `company.json` attendu par le skill `comptable` à partir des paramètres du dossier :

| Champ company.json | Source dans Lettra |
|---|---|
| `name`, `legal_form`, `capital`, `address`, `siren`, `siret`, `rcs`, `naf` | Fiche du dossier (émetteur) |
| `tax.regime_tva`, `tax.tva_rate`, `tax.option_debits` | Paramètres fiscaux du dossier |
| `invoicing.prefix`, `invoicing.avoir_prefix` | Séries de numérotation du dossier |
| `einvoicing.*` | Plateforme Agréée choisie et état de raccordement |
| `payment.*` | Conditions de paiement par défaut du dossier |

Les identifiants et coordonnées y sont pseudonymisés avant l'envoi au moteur.
