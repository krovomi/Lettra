using Lettra.Contracts.Speech;

namespace Lettra.Speech;

/// <summary>
/// Grammaire déterministe des commandes vocales (aucun modèle de langage) : la même phrase donne toujours la même
/// commande, et une phrase hors grammaire donne <c>null</c> plutôt qu'une supposition. Exemples :
/// « nouveau devis », « ouvrir les factures en retard », « chercher le client Dupont », « ajouter trois heures de
/// conseil à cent vingt euros », « remise de 10 % », « devis pour Dupont, trois heures de conseil à 120 € HT ».
/// </summary>
public static class VoiceCommandParser
{
    private static readonly VoiceTarget[] Creatable =
        [VoiceTarget.Quotes, VoiceTarget.Invoices, VoiceTarget.CreditNotes, VoiceTarget.Customers, VoiceTarget.Suppliers, VoiceTarget.Catalog];

    public static VoiceCommand? Parse(string? transcript, SpokenLanguage language)
    {
        string source = SpokenText.Prepare(transcript);
        List<SpokenToken> tokens = SpokenText.Tokenize(source)
            .Where(t => t.Kind is TokenKind.Word or TokenKind.Number || t.IsSymbol('€') || t.IsSymbol('%') || IsSeparator(t))
            .ToList();
        Reader reader = new(source, tokens, language);
        reader.TrimFillers();
        if (reader.IsEmpty)
        {
            return null;
        }

        return reader.ParseCommand()
               ?? reader.ParseDiscount()
               ?? reader.ParseAddLine()
               ?? reader.ParseCreate()
               ?? reader.ParseSearch()
               ?? reader.ParseNavigate();
    }

    private static bool IsSeparator(SpokenToken token) => token.IsSymbol(',') || token.IsSymbol(';');

    /// <summary>Une ligne lue, et si elle a l'air d'une ligne (quantité dite, unité ou prix) ou seulement d'un nom.</summary>
    private sealed record ParsedLine(VoiceLine Line, bool Strong);

    private sealed class Reader(string source, List<SpokenToken> tokens, SpokenLanguage language)
    {
        private readonly VoiceVocabulary _words = VoiceVocabulary.ForLanguage(language);
        private int _start;
        private int _end = tokens.Count;

        public bool IsEmpty => _start >= _end;

        private string Phrase => source;

        public void TrimFillers()
        {
            bool trimmed = true;
            while (trimmed && !IsEmpty)
            {
                trimmed = false;
                if (MatchAny(_words.Fillers, _start, _end) is int next)
                {
                    _start = next;
                    trimmed = true;
                }

                while (!IsEmpty && IsSeparator(tokens[_start]))
                {
                    _start++;
                    trimmed = true;
                }

                foreach (string[] filler in _words.Fillers)
                {
                    if (_end - filler.Length >= _start && Matches(filler, _end - filler.Length, _end))
                    {
                        _end -= filler.Length;
                        trimmed = true;
                    }
                }

                while (!IsEmpty && IsSeparator(tokens[_end - 1]))
                {
                    _end--;
                    trimmed = true;
                }
            }
        }

        /// <summary>« valider », « enregistrer », « annuler », « aide », éventuellement suivis de ce qu'ils visent (« valider la facture »).</summary>
        public VoiceCommand? ParseCommand()
        {
            if (MatchPhrase(_words.Commands, _start, _end) is not (VoiceCommandKind kind, int i))
            {
                return null;
            }

            VoiceTarget? target = null;
            if (i < _end)
            {
                i = Skip(_words.Articles, i);
                if (MatchPhrase(_words.Targets, i, _end) is not (VoiceTarget named, int afterTarget) || afterTarget != _end)
                {
                    return null;
                }

                target = named;
            }

            return new VoiceCommand { Kind = kind, Phrase = Phrase, Target = target, RequiresConfirmation = kind == VoiceCommandKind.Validate };
        }

        /// <summary>« remise de 10 % », « appliquer une remise de dix pour cent », « 10 % de remise ».</summary>
        public VoiceCommand? ParseDiscount()
        {
            int i = MatchAny(_words.DiscountVerbs, _start, _end) ?? _start;
            i = Skip(_words.Articles, i);
            if (MatchAny(_words.DiscountWords, i, _end) is int afterWord)
            {
                i = Skip(_words.Of, afterWord);
                if (SpokenAmounts.TryReadPercent(tokens, i, language, out decimal percent, out int next) && next == _end)
                {
                    return Discount(percent);
                }

                return null;
            }

            if (SpokenAmounts.TryReadPercent(tokens, i, language, out decimal leading, out int afterPercent)
                && MatchAny(_words.DiscountWords, Skip(_words.Of, afterPercent), _end) == _end)
            {
                return Discount(leading);
            }

            return null;
        }

        /// <summary>« ajouter trois heures de conseil à cent vingt euros HT », « ajoute une ligne de déplacement ».</summary>
        public VoiceCommand? ParseAddLine()
        {
            if (MatchAny(_words.AddVerbs, _start, _end) is not int i)
            {
                return null;
            }

            if (MatchAny(_words.LineWords, i, _end) is int afterLine)
            {
                i = Skip(_words.Of, afterLine);
            }

            return ParseLine(i, _end) is { } line
                ? new VoiceCommand { Kind = VoiceCommandKind.AddLine, Phrase = Phrase, Lines = [line.Line] }
                : null;
        }

        /// <summary>« nouveau devis », « créer un client », « devis pour Dupont, trois heures de conseil à 120 € HT ».</summary>
        public VoiceCommand? ParseCreate()
        {
            int i = _start;
            bool verb = false;
            if (MatchAny(_words.CreateVerbs, i, _end) is int afterVerb)
            {
                i = afterVerb;
                verb = true;
            }

            i = Skip(_words.Articles, i);
            if (MatchPhrase(_words.Targets, i, _end) is not (VoiceTarget target, int afterTarget) || !Creatable.Contains(target))
            {
                return null;
            }

            i = afterTarget;
            if (i == _end)
            {
                return verb ? new VoiceCommand { Kind = VoiceCommandKind.Create, Phrase = Phrase, Target = target } : null;
            }

            // Sans verbe, seul « devis pour … » crée quelque chose ; « factures en retard » est une navigation.
            if (!_words.For.Contains(tokens[i].Key) || target is not (VoiceTarget.Quotes or VoiceTarget.Invoices))
            {
                return null;
            }

            (string? customer, int afterCustomer) = ReadCustomer(i + 1);
            (List<VoiceLine> lines, decimal? discount) = ReadDraftLines(afterCustomer);
            bool draft = target == VoiceTarget.Quotes && (lines.Count > 0 || discount is not null);
            if (!verb && !draft && customer is null)
            {
                return null;
            }

            return new VoiceCommand
            {
                Kind = draft ? VoiceCommandKind.DraftQuote : VoiceCommandKind.Create,
                Phrase = Phrase,
                Target = target,
                Customer = customer,
                Lines = lines,
                DiscountPercent = discount,
            };
        }

        /// <summary>« chercher le client Dupont », « trouve la facture 2026-0042 », « rechercher Martin ».</summary>
        public VoiceCommand? ParseSearch()
        {
            if (MatchAny(_words.SearchVerbs, _start, _end) is not int i)
            {
                return null;
            }

            i = Skip(_words.Articles, i);
            VoiceTarget? target = null;
            if (MatchPhrase(_words.Targets, i, _end) is (VoiceTarget named, int afterTarget))
            {
                target = named;
                i = afterTarget;
            }

            string term = Slice(i, _end);
            if (term.Length == 0)
            {
                return target is null ? null : new VoiceCommand { Kind = VoiceCommandKind.Navigate, Phrase = Phrase, Target = target };
            }

            return new VoiceCommand { Kind = VoiceCommandKind.Search, Phrase = Phrase, Target = target, Term = term };
        }

        /// <summary>« ouvrir les factures en retard », « affiche le catalogue », « devis acceptés », « ouvre les factures de Dupont ».</summary>
        public VoiceCommand? ParseNavigate()
        {
            int i = MatchAny(_words.NavigateVerbs, _start, _end) ?? _start;
            i = Skip(_words.NavigateLinks, i);

            // En anglais, le filtre précède souvent le nom : « open overdue invoices », « show draft quotes ».
            VoiceFilter? filter = null;
            if (MatchPhrase(_words.Filters, i, _end) is (VoiceFilter leading, int afterLeading)
                && MatchPhrase(_words.Targets, afterLeading, _end) is (VoiceTarget, int))
            {
                filter = leading;
                i = afterLeading;
            }

            if (MatchPhrase(_words.Targets, i, _end) is not (VoiceTarget target, int afterTarget))
            {
                return null;
            }

            i = afterTarget;
            for (int filterAt = i; filter is null && filterAt < _end; filterAt++)
            {
                // « factures en retard », « factures qui sont en retard » : « en » est à la fois un lien et le début d'un filtre.
                if (MatchPhrase(_words.Filters, filterAt, _end) is (VoiceFilter named, int afterFilter))
                {
                    filter = named;
                    i = afterFilter;
                    break;
                }

                if (tokens[filterAt].Kind != TokenKind.Word || !IsLinkWord(tokens[filterAt].Key))
                {
                    break;
                }
            }

            string? term = i < _end ? Slice(Skip(_words.Of, i), _end) : null;
            return new VoiceCommand
            {
                Kind = VoiceCommandKind.Navigate,
                Phrase = Phrase,
                Target = target,
                Filter = filter,
                Term = string.IsNullOrEmpty(term) ? null : term,
            };
        }

        private static bool IsLinkWord(string key) => key is "qui" or "sont" or "en" or "that" or "are" or "which" or "is";

        private VoiceCommand Discount(decimal percent) =>
            new() { Kind = VoiceCommandKind.ApplyDiscount, Phrase = Phrase, DiscountPercent = percent };

        /// <summary>Le client nommé après « pour » : jusqu'à la première virgule, ou jusqu'à ce qu'une ligne commence.</summary>
        private (string? Customer, int Next) ReadCustomer(int index)
        {
            int segmentEnd = NextSeparator(index);
            int k = index;
            while (k < segmentEnd && !(k > index && StartsLine(k, segmentEnd)))
            {
                k++;
            }

            int nameStart = MatchAny(_words.CustomerPrefixes, index, k) ?? index;
            string name = Slice(nameStart, k);
            return (name.Length == 0 ? null : name, k);
        }

        /// <summary>
        /// Les lignes dites après le client, séparées par des virgules ou par « et », « puis », « plus » devant une
        /// quantité. Une partie qui n'est pas lisible devient une ligne de quantité 1 portant le texte dit : rien de ce
        /// que l'utilisateur a dit ne disparaît, il le relit dans le brouillon.
        /// </summary>
        private (List<VoiceLine> Lines, decimal? Discount) ReadDraftLines(int index)
        {
            List<VoiceLine> lines = [];
            decimal? discount = null;
            int segment = index;
            while (segment < _end)
            {
                if (IsSeparator(tokens[segment]))
                {
                    segment++;
                    continue;
                }

                int segmentEnd = NextSeparator(segment);
                Reader part = new(source, tokens, language) { _start = segment, _end = segmentEnd };
                if (part.ParseDiscount() is { DiscountPercent: decimal percent })
                {
                    discount = percent;
                }
                else
                {
                    lines.AddRange(ReadLines(segment, segmentEnd));
                }

                segment = segmentEnd;
            }

            return (lines, discount);
        }

        /// <summary>
        /// Une ligne commence ici : une quantité dite (« deux jours de formation… »), suivie d'une ligne lisible jusqu'à la
        /// fin du groupe ou jusqu'à « et », « puis », « plus ».
        /// </summary>
        private bool StartsLine(int index, int end)
        {
            if (!SpokenNumbers.StartsNumber(tokens, index, language))
            {
                return false;
            }

            for (int m = end; m > index; m--)
            {
                bool boundary = m == end || (tokens[m].Kind == TokenKind.Word && _words.LineSeparators.Contains(tokens[m].Key));
                if (boundary && ParseLine(index, m) is { Strong: true } line && line.Line.Quantity > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<VoiceLine> ReadLines(int start, int end)
        {
            if (ParseLine(start, end) is { } whole)
            {
                yield return whole.Line;
                yield break;
            }

            // « trois heures de conseil à 120 € et deux jours de formation à 600 € »
            for (int k = start + 1; k < end - 1; k++)
            {
                if (_words.LineSeparators.Contains(tokens[k].Key) && tokens[k].Kind == TokenKind.Word
                    && ParseLine(start, k) is { } first && ParseLine(k + 1, end) is { Strong: true } second)
                {
                    yield return first.Line;
                    yield return second.Line;
                    yield break;
                }
            }

            string text = Slice(start, end);
            if (text.Length > 0)
            {
                yield return new VoiceLine(1, null, text, null, null);
            }
        }

        /// <summary>
        /// [quantité] [unité [et demie]] [de] désignation [à|pour|au prix de prix [€]] [HT|TTC] [de l'heure|par jour…] [HT|TTC]
        /// </summary>
        private ParsedLine? ParseLine(int start, int end)
        {
            int i = start;
            decimal quantity = 1;
            bool explicitQuantity = false;
            if (i < end && SpokenNumbers.TryRead(tokens, i, language, out decimal read, out int afterQuantity) && afterQuantity <= end
                && !SpokenAmounts.TryReadEuros(tokens, i, language, out _, out _))
            {
                quantity = read;
                explicitQuantity = true;
                i = afterQuantity;
            }
            else if (i < end && language == SpokenLanguage.English && tokens[i].Key is "a" or "an")
            {
                explicitQuantity = true;
                i++;
            }

            string? unit = null;
            if (MatchPhrase(_words.Units, i, end) is (string code, int afterUnit))
            {
                unit = code;
                i = afterUnit;
                if (SpokenNumbers.ReadTrailingHalf(tokens, i, language) is int afterHalf && afterHalf <= end)
                {
                    quantity += 0.5m;
                    i = afterHalf;
                }
            }

            if (explicitQuantity || unit is not null)
            {
                i = Skip(_words.Of, i);
            }

            int designationStart = i;
            int designationEnd = end;
            decimal? price = null;
            for (int p = designationStart + 1; p < end && price is null; p++)
            {
                // « à 120 € », « pour 80 euros », « au prix de 120 » : après une préposition, un nombre seul est un prix.
                if (MatchAny(_words.PricePrepositions, p, end) is int q)
                {
                    if (SpokenAmounts.TryReadEuros(tokens, q, language, out decimal euros, out int afterEuros) && afterEuros <= end)
                    {
                        (price, designationEnd, i) = (euros, p, afterEuros);
                    }
                    else if (SpokenNumbers.TryRead(tokens, q, language, out decimal bare, out int afterBare) && afterBare <= end
                             && MatchPhrase(_words.Units, afterBare, end) is null)
                    {
                        (price, designationEnd, i) = (Math.Round(bare, 2), p, afterBare);
                    }
                }

                // Sans préposition, seul un montant explicite (« déplacement 45 euros ») est un prix.
                if (price is null && SpokenAmounts.TryReadEuros(tokens, p, language, out decimal named, out int afterNamed) && afterNamed <= end)
                {
                    (price, designationEnd, i) = (named, p, afterNamed);
                }
            }

            if (price is null)
            {
                i = end;
            }

            bool? includesVat = SpokenAmounts.TryReadTaxMarker(tokens, i, out int afterTax);
            i = afterTax;
            if (MatchAny(_words.PerUnit, i, end) is int afterPerUnit)
            {
                i = afterPerUnit;
            }

            if (includesVat is null)
            {
                includesVat = SpokenAmounts.TryReadTaxMarker(tokens, i, out int afterLateTax);
                i = afterLateTax;
            }

            string designation = Slice(designationStart, designationEnd);
            if (i != end || designation.Length == 0)
            {
                return null;
            }

            VoiceLine line = new(quantity, unit, designation, price, includesVat);
            return new ParsedLine(line, explicitQuantity || unit is not null || price is not null);
        }

        private int NextSeparator(int index)
        {
            int k = index;
            while (k < _end && !IsSeparator(tokens[k]))
            {
                k++;
            }

            return k;
        }

        private string Slice(int start, int end) =>
            end <= start ? string.Empty : SpokenText.Slice(source, tokens, start, end - 1).TrimEnd('.', '!', '?', ',', ';').Trim();

        private int Skip(string[] keys, int index)
        {
            int i = index;
            while (i < _end && tokens[i].Kind == TokenKind.Word && keys.Contains(tokens[i].Key))
            {
                i++;
            }

            return i;
        }

        private bool Matches(string[] keys, int index, int end)
        {
            if (index < 0 || index + keys.Length > end)
            {
                return false;
            }

            for (int k = 0; k < keys.Length; k++)
            {
                if (tokens[index + k].Kind != TokenKind.Word || !SoundsLike(tokens[index + k].Key, keys[k]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Le mot entendu est celui du vocabulaire, à une lettre près pour un mot d'au moins six lettres : Whisper écrit
        /// parfois une lettre de trop ou de moins (« paramètre », « fournisseurss »). Le vocabulaire est fermé, une lettre
        /// ne fait pas passer un mot pour un autre.
        /// </summary>
        private static bool SoundsLike(string heard, string key) =>
            string.Equals(heard, key, StringComparison.Ordinal)
            || (key.Length >= 6 && heard.Length >= 6 && Math.Abs(heard.Length - key.Length) <= 1 && WithinOneEdit(heard, key));

        private static bool WithinOneEdit(string a, string b)
        {
            int i = 0;
            int j = 0;
            bool edited = false;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j])
                {
                    i++;
                    j++;
                    continue;
                }

                if (edited)
                {
                    return false;
                }

                edited = true;
                if (a.Length > b.Length)
                {
                    i++;
                }
                else if (a.Length < b.Length)
                {
                    j++;
                }
                else
                {
                    i++;
                    j++;
                }
            }

            return !edited || (i == a.Length && j == b.Length);
        }

        private int? MatchAny(string[][] phrases, int index, int end)
        {
            int? best = null;
            foreach (string[] phrase in phrases)
            {
                if (Matches(phrase, index, end) && (best is null || index + phrase.Length > best))
                {
                    best = index + phrase.Length;
                }
            }

            return best;
        }

        private (T Value, int Next)? MatchPhrase<T>(Phrase<T>[] phrases, int index, int end)
        {
            (T Value, int Next)? best = null;
            foreach (Phrase<T> phrase in phrases)
            {
                if (Matches(phrase.Keys, index, end) && (best is null || index + phrase.Keys.Length > best.Value.Next))
                {
                    best = (phrase.Value, index + phrase.Keys.Length);
                }
            }

            return best;
        }
    }
}
