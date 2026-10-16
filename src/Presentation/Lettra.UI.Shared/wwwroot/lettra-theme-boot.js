// Thème retenu posé avant le premier rendu (vague 5) : sans lui, la page s'afficherait un instant dans le thème du
// système avant que Blazor ne démarre. Script classique, synchrone, chargé dans le <head> des hôtes Web et MAUI :
//   <script src="_content/Lettra.UI.Shared/lettra-theme-boot.js"></script>
// Fichier séparé plutôt que script en ligne : la CSP de l'API n'admet que les scripts de l'origine.
(function () {
  var themes = { Light: "light", Dark: "dark", Slate: "slate", Contrast: "contrast" };
  try {
    var theme = themes[window.localStorage.getItem("lettra.theme")];
    if (theme) { document.documentElement.setAttribute("data-theme", theme); }
  } catch (e) {
    // Stockage indisponible (navigation privée stricte) : le thème du système s'applique.
  }
})();
