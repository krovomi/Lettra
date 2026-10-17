using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace Lettra.App.Maui;

/// <summary>
/// Retour de la connexion du cabinet sous Android (serveur commun) : l'onglet du navigateur rouvre l'application par
/// <c>fr.lettra.app://callback</c>, adresse de retour du client <c>lettra-maui</c> du realm.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "fr.lettra.app", DataHost = "callback")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity;
