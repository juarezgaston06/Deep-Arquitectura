using DeepArquitectura.Web.Data.Entities;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Etapas sugeridas al crear una obra según el servicio. Son un punto de partida:
/// desde el panel se pueden renombrar, omitir o agregar etapas por obra.
/// </summary>
public static class PlantillasEtapas
{
    private static readonly Dictionary<TipoServicio, string[]> Plantillas = new()
    {
        [TipoServicio.PlanosMunicipales] =
        [
            "Visita y relevamiento inicial",
            "Confección de planos",
            "Firma y visado profesional",
            "Presentación municipal",
            "Corrección de observaciones",
            "Aprobación de planos",
            "Final de obra",
        ],
        [TipoServicio.Relevamiento] =
        [
            "Visita y medición",
            "Dibujo del relevamiento",
            "Documentación conforme a obra",
            "Presentación municipal",
            "Aprobación / regularización",
        ],
        [TipoServicio.DisenoDeObra] =
        [
            "Reunión inicial y programa de necesidades",
            "Anteproyecto",
            "Proyecto ejecutivo",
            "Documentación técnica",
            "Dirección de obra",
        ],
        [TipoServicio.Electricidad] =
        [
            "Visita y diagnóstico",
            "Presupuesto aprobado",
            "Canalización y cableado",
            "Tablero y protecciones",
            "Pruebas y mediciones",
            "Documentación y entrega",
        ],
        [TipoServicio.Gas] =
        [
            "Visita y relevamiento",
            "Planos y trámite ante la distribuidora",
            "Ejecución de cañerías",
            "Prueba de hermeticidad",
            "Inspección",
            "Habilitación",
        ],
    };

    public static IReadOnlyList<string> Para(TipoServicio servicio) =>
        Plantillas.TryGetValue(servicio, out var etapas) ? etapas : [];

    public static List<EtapaObra> Crear(TipoServicio servicio) =>
        Para(servicio).Select((nombre, i) => new EtapaObra { Nombre = nombre, Orden = i + 1 }).ToList();
}
