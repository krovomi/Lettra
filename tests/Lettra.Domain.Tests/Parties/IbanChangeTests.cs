using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Parties;

public sealed class IbanChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly SupplierId Supplier = SupplierId.New();
    private static readonly Guid Changer = Guid.CreateVersion7();
    private static readonly Guid Checker = Guid.CreateVersion7();
    private static readonly Iban Usual = Iban.Create("FR76 3000 6000 0112 3456 7890 189").Value;
    private static readonly Iban OtherFrench = Iban.Create("FR14 2004 1010 0505 0001 3M02 606").Value;
    private static readonly Iban German = Iban.Create("DE89370400440532013000").Value;

    private static IbanChange Pending(Iban? previous = null, Iban? next = null) =>
        IbanChange.Detect(Supplier, previous ?? Usual, next ?? OtherFrench, Changer, Now)!;

    [Fact]
    public void Un_nouvel_iban_est_a_verifier_avec_les_formes_masquees()
    {
        IbanChange change = Pending();

        change.Status.Should().Be(IbanChangeStatus.Pending);
        change.IsPending.Should().BeTrue();
        change.PreviousIban.Should().Be("FR76 •••• 0189");
        change.NewIban.Should().Be("FR14 •••• 2606");
        change.CountryChanged.Should().BeFalse();
        change.ChangedBy.Should().Be(Changer);
        change.ChangedAt.Should().Be(Now);
        change.SupplierId.Should().Be(Supplier);
    }

    [Fact]
    public void Un_compte_qui_change_de_pays_est_signale() =>
        Pending(next: German).CountryChanged.Should().BeTrue();

    [Fact]
    public void Un_premier_iban_ajoute_apres_la_creation_est_aussi_a_verifier()
    {
        IbanChange change = IbanChange.Detect(Supplier, null, German, Changer, Now)!;

        change.PreviousIban.Should().BeNull();
        change.CountryChanged.Should().BeFalse("rien à comparer");
    }

    [Fact]
    public void Le_meme_iban_ou_sa_suppression_ne_sont_pas_des_changements()
    {
        IbanChange.Detect(Supplier, Usual, Iban.Create("fr7630006000011234567890189").Value, Changer, Now).Should().BeNull();
        IbanChange.Detect(Supplier, Usual, null, Changer, Now).Should().BeNull();
        IbanChange.Detect(Supplier, null, null, Changer, Now).Should().BeNull();
    }

    [Fact]
    public void Une_confirmation_dit_par_quel_canal()
    {
        IbanChange change = Pending();

        Result confirmed = change.Confirm(IbanVerificationMethod.KnownPhoneNumber, "  Appel à Mme Dupuis  ", Checker, Now.AddHours(1));

        confirmed.IsSuccess.Should().BeTrue();
        change.Status.Should().Be(IbanChangeStatus.Confirmed);
        change.Method.Should().Be(IbanVerificationMethod.KnownPhoneNumber);
        change.Note.Should().Be("Appel à Mme Dupuis");
        change.DecidedBy.Should().Be(Checker);
        change.DecidedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Un_canal_inconnu_ou_autre_sans_note_est_refuse()
    {
        Pending().Confirm((IbanVerificationMethod)9, null, Checker, Now).Errors.Should().ContainSingle().Which.Should().Be(IbanChangeErrors.MethodRequired);
        Pending().Confirm(IbanVerificationMethod.Other, "  ", Checker, Now).Errors.Should().ContainSingle().Which.Should().Be(IbanChangeErrors.NoteRequired);
        Pending().Confirm(IbanVerificationMethod.Other, "Visio avec le gérant", Checker, Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_refus_porte_une_note()
    {
        IbanChange change = Pending();

        change.Reject(null, Checker, Now).Errors.Should().ContainSingle().Which.Should().Be(IbanChangeErrors.NoteRequired);
        change.Reject("Le fournisseur n'a rien demandé", Checker, Now).IsSuccess.Should().BeTrue();

        change.Status.Should().Be(IbanChangeStatus.Rejected);
        change.Method.Should().BeNull();
    }

    [Fact]
    public void Une_note_trop_longue_est_refusee() =>
        Pending().Reject(new string('x', IbanChange.MaxNoteLength + 1), Checker, Now)
            .Errors.Should().ContainSingle().Which.Should().Be(IbanChangeErrors.NoteTooLong);

    [Fact]
    public void Un_changement_traite_ne_se_decide_plus()
    {
        IbanChange change = Pending();
        change.Confirm(IbanVerificationMethod.InPerson, null, Checker, Now);

        change.Reject("Trop tard", Checker, Now).Errors.Should().ContainSingle().Which.Should().Be(IbanChangeErrors.AlreadyDecided);
        change.Confirm(IbanVerificationMethod.InPerson, null, Checker, Now).Errors.Should().ContainSingle().Which.Kind.Should().Be(ErrorKind.Conflict);
    }

    [Fact]
    public void Seul_un_changement_a_verifier_est_remplace()
    {
        IbanChange pending = Pending();
        IbanChange confirmed = Pending();
        confirmed.Confirm(IbanVerificationMethod.SignedLetter, null, Checker, Now);

        pending.Supersede(Now.AddDays(1));
        confirmed.Supersede(Now.AddDays(1));

        pending.Status.Should().Be(IbanChangeStatus.Superseded);
        pending.DecidedAt.Should().Be(Now.AddDays(1));
        confirmed.Status.Should().Be(IbanChangeStatus.Confirmed);
    }
}
