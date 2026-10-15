namespace Lettra.Contracts.Common;

/// <summary>
/// Catalogue des codes d'erreur visibles par les clients. Le serveur n'envoie jamais de texte
/// localisé : l'UI traduit ces codes (ressources <c>Error.&lt;code&gt;</c>).
/// Un test d'architecture vérifie que chaque code émis par le domaine ou l'application figure
/// ici et possède une traduction française et anglaise.
/// <para>
/// Un fichier par module (<c>ErrorCodes.&lt;Module&gt;.cs</c>) : un nouveau module ajoute son fichier au lieu de
/// modifier un fichier partagé.
/// </para>
/// </summary>
public static partial class ErrorCodes;
