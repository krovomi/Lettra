using Lettra.UI.Shared.Services;

namespace Lettra.UI.Tests.Components;

public sealed class ToastServiceTests
{
    [Fact]
    public async Task Presser_l_action_ferme_le_message_puis_la_lance()
    {
        ToastService toasts = new();
        bool undone = false;
        Toast toast = toasts.Show(ToastKind.Success, "Rôle retiré", new ToastAction("Annuler", () =>
        {
            undone = true;
            return Task.CompletedTask;
        }));

        await toasts.RunActionAsync(toast);

        undone.Should().BeTrue();
        toasts.Visible.Should().BeEmpty();
    }

    [Fact]
    public void Deux_gestes_a_defaire_identiques_gardent_chacun_leur_message()
    {
        ToastService toasts = new();
        ToastAction undo = new("Annuler", () => Task.CompletedTask);

        toasts.Show(ToastKind.Success, "Membre retiré", undo);
        toasts.Show(ToastKind.Success, "Membre retiré", undo);
        toasts.Show(ToastKind.Error, "Refusé");
        toasts.Show(ToastKind.Error, "Refusé");

        toasts.Visible.Select(t => t.Message).Should().Equal("Membre retiré", "Membre retiré", "Refusé");
    }
}
