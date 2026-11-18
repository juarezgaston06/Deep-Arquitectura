using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// En las pantallas interactivas la sesión se valida una sola vez, al abrir el circuito de SignalR.
/// Esto la vuelve a comprobar cada minuto contra el sello de la contraseña vigente: si cambió,
/// el panel abierto pasa a «no autorizado» y redirige al login.
/// </summary>
public sealed class RevalidarSesionAdmin(ILoggerFactory loggerFactory, AdminCredentials credenciales)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        var sello = await credenciales.SelloAsync(ct);
        return sello is not null && state.User.FindFirst(AdminCredentials.ClaimSello)?.Value == sello;
    }
}
