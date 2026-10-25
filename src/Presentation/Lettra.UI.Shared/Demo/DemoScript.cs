namespace Lettra.UI.Shared.Demo;

/// <summary>Ce que la démo commentée fait à la place de l'utilisateur.</summary>
public enum DemoActionKind
{
    /// <summary>Ouvre un écran (chemin relatif, comme un lien du menu).</summary>
    Navigate,

    /// <summary>Attend qu'un élément apparaisse (écran qui charge, pièce qui s'enregistre).</summary>
    WaitFor,

    /// <summary>Tape un texte dans un champ, ou choisit l'option d'une liste déroulante par son texte.</summary>
    Type,

    /// <summary>Clique un bouton ou un lien.</summary>
    Click,
}

/// <summary>
/// Un geste de la démo. <see cref="Target"/> est un sélecteur CSS, ou « css &gt;&gt; texte » pour le premier élément
/// visible qui contient ce texte. <see cref="Value"/> est le texte tapé ou le chemin ouvert ; il peut porter des jetons
/// (<see cref="DemoValues"/>) et, s'il commence par <see cref="DemoValues.ResourcePrefix"/>, il est lu dans les
/// ressources pour suivre la langue de l'écran.
/// </summary>
public sealed record DemoAction(DemoActionKind Kind, string Target, string? Value = null)
{
    public static DemoAction Go(string path) => new(DemoActionKind.Navigate, string.Empty, path);

    public static DemoAction Wait(string target) => new(DemoActionKind.WaitFor, target);

    public static DemoAction Type(string target, string value) => new(DemoActionKind.Type, target, value);

    public static DemoAction Click(string target) => new(DemoActionKind.Click, target);
}

/// <summary>
/// Une étape : une bulle (titre et texte dans les ressources sous <c>Demo.&lt;chapitre&gt;.&lt;étape&gt;.Title</c> et
/// <c>.Text</c>), l'élément montré pendant la lecture, et les gestes joués avant. Une étape sans geste ne fait
/// qu'expliquer ce qui est à l'écran.
/// </summary>
public sealed record DemoStep(string Id, string? Highlight, IReadOnlyList<DemoAction> Actions)
{
    public DemoStep(string id, string? highlight = null, params DemoAction[] actions)
        : this(id, highlight, (IReadOnlyList<DemoAction>)actions)
    {
    }
}

/// <summary>
/// Un chapitre de la démo (une fonctionnalité), titre sous <c>Demo.&lt;chapitre&gt;.Title</c>. Un chapitre commence par
/// ouvrir son écran, sauf <paramref name="ContinuesPrevious"/> : il reprend là où le précédent s'arrête (la facture
/// naît du devis accepté) et ne se lance pas seul.
/// </summary>
public sealed record DemoChapter(string Id, string Icon, IReadOnlyList<DemoStep> Steps, bool ContinuesPrevious = false)
{
    public string TitleKey => $"Demo.{Id}.Title";

    public string SummaryKey => $"Demo.{Id}.Summary";

    public string StepTitleKey(DemoStep step) => $"Demo.{Id}.{step.Id}.Title";

    public string StepTextKey(DemoStep step) => $"Demo.{Id}.{step.Id}.Text";
}
