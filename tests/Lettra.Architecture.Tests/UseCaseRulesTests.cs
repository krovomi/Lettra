using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Domain.Common;

namespace Lettra.Architecture.Tests;

/// <summary>Règles sur les cas d'usage (annexe F §4) : chaque commande et requête déclare sa permission.</summary>
public sealed class UseCaseRulesTests
{
    private static IEnumerable<Type> UseCases() =>
        Assemblies.Application.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && t.GetInterfaces().Any(IsUseCase));

    private static bool IsUseCase(Type i) =>
        i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>));

    [Fact]
    public void Il_existe_des_cas_d_usage() => UseCases().Should().NotBeEmpty();

    [Fact]
    public void Chaque_cas_d_usage_declare_une_permission_ou_est_explicitement_public() =>
        UseCases()
            .Where(t => !typeof(ISecuredUseCase).IsAssignableFrom(t) && !Attribute.IsDefined(t, typeof(PublicUseCaseAttribute)))
            .Select(t => t.Name)
            .Should().BeEmpty();

    [Fact]
    public void Chaque_cas_d_usage_renvoie_un_Result() =>
        UseCases()
            .SelectMany(t => t.GetInterfaces().Where(IsUseCase))
            .Select(i => i.GetGenericArguments()[0])
            .Where(r => r != typeof(Result) && !(r.IsGenericType && r.GetGenericTypeDefinition() == typeof(Result<>)))
            .Should().BeEmpty();

    [Fact]
    public void Les_cas_d_usage_sont_immuables() =>
        UseCases()
            .SelectMany(t => t.GetProperties().Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p.SetMethod)).Select(p => $"{t.Name}.{p.Name}"))
            .Should().BeEmpty();

    private static bool IsInitOnly(System.Reflection.MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
