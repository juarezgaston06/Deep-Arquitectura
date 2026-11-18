using DeepArquitectura.Web.Data.Entities;

namespace DeepArquitectura.Web.Presupuestos;

/// <summary>Combinaciones típicas de tareas: marcan las casillas de un saque y después se ajustan.</summary>
public static class PlantillasPresupuesto
{
    public sealed record Plantilla(string Nombre, TipoServicio Servicio, Action<PresupuestoEntrada> Aplicar);

    public static readonly IReadOnlyList<Plantilla> Todas =
    [
        new("Obra nueva (planos + dirección)", TipoServicio.PlanosMunicipales, e => Reset(e, x =>
        {
            x.Proyecto = NivelProyecto.ProyectoConDetalles; x.DireccionTecnica = true;
            x.Diligenciamiento = true; x.Carpeta = true; x.RegistroDiferenciado = true;
        })),
        new("Relevamiento / regularización", TipoServicio.Relevamiento, e => Reset(e, x =>
        {
            x.Relevamiento = true; x.Diligenciamiento = true; x.Carpeta = true; x.RegistroDiferenciado = true;
        })),
        new("Solo proyecto", TipoServicio.DisenoDeObra, e => Reset(e, x =>
        {
            x.Proyecto = NivelProyecto.ProyectoConDetalles; x.Carpeta = true;
        })),
        new("Instalación eléctrica", TipoServicio.Electricidad, e => Reset(e, x =>
        {
            x.IncluirInstalacion = true; x.Carpeta = true;
        })),
        new("Instalación de gas", TipoServicio.Gas, e => Reset(e, x =>
        {
            x.IncluirInstalacion = true; x.Carpeta = true;
        })),
    ];

    /// <summary>Apaga todas las tareas (conserva superficies, montos y extras) y aplica la plantilla.</summary>
    private static void Reset(PresupuestoEntrada e, Action<PresupuestoEntrada> aplicar)
    {
        e.Proyecto = NivelProyecto.Ninguno; e.DireccionTecnica = false; e.PrimeraCategoria = false;
        e.IncluirInstalacion = false; e.Relevamiento = false; e.RepresentacionTecnica = false;
        e.ConduccionTecnica = false; e.InformeTecnico = false; e.Diligenciamiento = false;
        e.Carpeta = false; e.ArancelAdministrativo = false; e.RegistroDiferenciado = false;
        aplicar(e);
    }
}
