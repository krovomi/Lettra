using Lettra.Application.Abstractions.Mail;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

public sealed class EmailPortTests
{
    [Fact]
    public async Task Sans_adaptateur_branche_aucun_e_mail_ne_part_et_l_adresse_publique_est_inconnue()
    {
        ServiceCollection services = new();
        services.AddApplication();
        await using ServiceProvider provider = services.BuildServiceProvider();

        IEmailSender sender = provider.GetRequiredService<IEmailSender>();
        sender.IsConfigured.Should().BeFalse();
        (await sender.SendAsync(new EmailMessage(Email.Create("a@b.fr").Value, "Objet", "Corps"), default))
            .Errors.Single().Code.Should().Be(ErrorCodes.Email.NotConfigured);
        provider.GetRequiredService<LettraPublicAddress>().Url.Should().BeNull();
    }
}
