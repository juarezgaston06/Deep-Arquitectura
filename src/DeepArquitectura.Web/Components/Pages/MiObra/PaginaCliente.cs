using DeepArquitectura.Web.Endpoints;
using Microsoft.AspNetCore.Components;

namespace DeepArquitectura.Web.Components.Pages.MiObra;

/// <summary>
/// Base de las pantallas de la app del cliente (HTML estático, sin Blazor en el navegador): sin sesión de cliente
/// manda a ingresar; con sesión, <see cref="ObraId"/> es la obra del código y todo se filtra por ella.
/// </summary>
public abstract class PaginaCliente : ComponentBase
{
    [CascadingParameter] protected HttpContext Http { get; set; } = null!;
    [Inject] protected NavigationManager Nav { get; set; } = null!;

    protected int ObraId { get; private set; }

    protected sealed override async Task OnInitializedAsync()
    {
        if (await SesionCliente.ObraAsync(Http) is not int obra)
        {
            Nav.NavigateTo(SesionCliente.Entrar);
            return;
        }
        ObraId = obra;
        await CargarAsync();
    }

    protected abstract Task CargarAsync();

    protected string? Ip => Http.Connection.RemoteIpAddress?.ToString();
}
