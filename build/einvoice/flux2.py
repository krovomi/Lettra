"""Dit si une facture CII relève du Flux 2 de la réforme (e-invoicing) : un acheteur établi en France, identifié par son
SIREN. Le reste (étranger, particulier sans SIREN) relève de l'e-reporting et n'est pas soumis aux règles BR-FR."""
import sys
import xml.etree.ElementTree as ET

RAM = "{urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100}"
buyer = ET.parse(sys.argv[1]).find(f".//{RAM}BuyerTradeParty")
country = buyer.findtext(f"{RAM}PostalTradeAddress/{RAM}CountryID") if buyer is not None else None
siren = buyer.findtext(f"{RAM}SpecifiedLegalOrganization/{RAM}ID") if buyer is not None else None
print("yes" if country == "FR" and siren else "no")
