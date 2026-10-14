namespace Lettra.UI.Abstractions.Concurrency;

/// <summary>
/// Fusion champ par champ à trois versions (ADR-0013), pour une écriture refusée en 412 :
/// <list type="bullet">
/// <item><b>base</b> : le document tel que l'utilisateur l'a lu ;</item>
/// <item><b>mine</b> : sa saisie ;</item>
/// <item><b>theirs</b> : le document actuel du serveur (<c>OperationResult.Current</c>).</item>
/// </list>
/// Un champ changé d'un seul côté prend ce côté ; changé des deux côtés à l'identique, il n'y a pas de conflit ;
/// changé des deux côtés différemment, c'est un <b>conflit</b> que l'utilisateur tranche (la fusion garde alors
/// la valeur du serveur, pour ne rien écrire à son insu). Les champs sont déclarés à la main, comme les mappers :
/// pas de réflexion (WebAssembly élagué). La même fusion servira à la synchronisation hors ligne (phase 4).
/// </summary>
/// <typeparam name="T">Contrat de saisie (ex. <c>SaveCatalogItemRequest</c>).</typeparam>
public sealed class FieldMerge<T>
    where T : class
{
    private readonly List<IField> _fields = [];

    /// <summary>Déclare un champ : son nom (celui du contrat, pour l'affichage), sa lecture et sa recopie.</summary>
    public FieldMerge<T> Field<TValue>(string name, Func<T, TValue> get, Func<T, TValue, T> set)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _fields.Add(new FieldOf<TValue>(name, get, set));
        return this;
    }

    public MergeResult<T> Merge(T @base, T mine, T theirs)
    {
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);

        T merged = theirs;
        List<string> conflicts = [];
        foreach (IField field in _fields)
        {
            merged = field.Apply(@base, mine, theirs, merged, conflicts);
        }

        return new MergeResult<T>(merged, conflicts);
    }

    private interface IField
    {
        T Apply(T @base, T mine, T theirs, T merged, List<string> conflicts);
    }

    private sealed class FieldOf<TValue>(string name, Func<T, TValue> get, Func<T, TValue, T> set) : IField
    {
        public T Apply(T @base, T mine, T theirs, T merged, List<string> conflicts)
        {
            EqualityComparer<TValue> eq = EqualityComparer<TValue>.Default;
            TValue b = get(@base);
            TValue m = get(mine);
            TValue t = get(theirs);
            if (eq.Equals(m, b) || eq.Equals(m, t))
            {
                return merged;   // inchangé chez moi, ou même changement des deux côtés : la valeur du serveur suffit
            }

            if (eq.Equals(t, b))
            {
                return set(merged, m);   // changé seulement chez moi
            }

            conflicts.Add(name);
            return merged;
        }
    }
}

/// <summary>Résultat d'une fusion : le document fusionné et les champs en conflit (vide si la fusion est sûre).</summary>
public sealed record MergeResult<T>(T Merged, IReadOnlyList<string> Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}
