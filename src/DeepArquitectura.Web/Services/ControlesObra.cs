using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Controles por etapa: lo que hay que verificar (con foto) antes de seguir o de tapar. Se cargan desde una plantilla
/// y se ajustan por obra; la cuadrilla los responde desde la app cuando la etapa está en curso.
/// </summary>
public class ControlesObra(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Plantillas: nombre → (control, requiere foto). Son un punto de partida, editable por obra.</summary>
    public static readonly IReadOnlyDictionary<string, (string Texto, bool Foto)[]> Plantillas = new Dictionary<string, (string, bool)[]>
    {
        ["Replanteo y excavación"] =
        [
            ("Replanteo verificado contra plano (ejes y niveles)", true),
            ("Excavación a la cota y ancho del plano", true),
            ("Fondo de excavación limpio y compactado", true),
        ],
        ["Hormigón armado (bases, columnas, vigas, losas)"] =
        [
            ("Armadura según plano: diámetros, cantidad y separación", true),
            ("Separadores colocados (recubrimiento)", true),
            ("Encofrado limpio, estanco, nivelado y apuntalado", true),
            ("Pases e instalaciones embutidas colocados antes de hormigonar", true),
            ("Hormigón: remito con resistencia pedida", true),
            ("Curado los primeros días (regado / cubierto)", false),
        ],
        ["Mampostería"] =
        [
            ("Capa aisladora horizontal colocada", true),
            ("Plomo y nivel de los muros", true),
            ("Vanos según plano (medidas y ubicación)", true),
            ("Dinteles y encadenados en su lugar", true),
        ],
        ["Instalación sanitaria"] =
        [
            ("Pendientes de desagüe verificadas", true),
            ("Prueba hidráulica de agua (presión) antes de tapar", true),
            ("Prueba de desagües (llenado) antes de tapar", true),
            ("Cámaras y bocas de acceso en su lugar", true),
        ],
        ["Instalación eléctrica"] =
        [
            ("Cañerías y cajas según plano antes de revocar", true),
            ("Puesta a tierra (jabalina) instalada", true),
            ("Tablero con protecciones (térmicas y disyuntor)", true),
        ],
        ["Instalación de gas"] =
        [
            ("Cañería según plano aprobado, antes de tapar", true),
            ("Prueba de hermeticidad realizada", true),
            ("Ventilaciones de artefactos en su lugar", true),
        ],
        ["Revoques y contrapisos"] =
        [
            ("Instalaciones embutidas probadas antes de revocar", true),
            ("Fajas / niveles de referencia colocados", true),
            ("Contrapiso con pendiente hacia desagües", true),
        ],
        ["Impermeabilización y cubierta"] =
        [
            ("Superficie limpia y seca antes de impermeabilizar", true),
            ("Solapes y babetas terminados", true),
            ("Prueba de estanqueidad (inundación 24 h)", true),
            ("Pendientes y bajadas pluviales despejadas", true),
        ],
    };

    /// <summary>Plantilla que corresponde por el nombre de la etapa (null si no hay una clara).</summary>
    public static string? Sugerida(string nombreEtapa)
    {
        var n = nombreEtapa.ToLowerInvariant();
        return n switch
        {
            _ when n.Contains("excav") || n.Contains("replanteo") || n.Contains("fundac") => "Replanteo y excavación",
            _ when n.Contains("hormig") || n.Contains("losa") || n.Contains("estructura") || n.Contains("columna") || n.Contains("viga") => "Hormigón armado (bases, columnas, vigas, losas)",
            _ when n.Contains("mampost") || n.Contains("muro") || n.Contains("ladrill") => "Mampostería",
            _ when n.Contains("sanitar") || n.Contains("agua") || n.Contains("desag") || n.Contains("cloaca") => "Instalación sanitaria",
            _ when n.Contains("gas") || n.Contains("hermetic") => "Instalación de gas",
            _ when n.Contains("eléctr") || n.Contains("electr") || n.Contains("cablead") || n.Contains("tablero") => "Instalación eléctrica",
            _ when n.Contains("revoque") || n.Contains("contrapiso") || n.Contains("carpeta") => "Revoques y contrapisos",
            _ when n.Contains("impermeab") || n.Contains("cubierta") || n.Contains("techo") => "Impermeabilización y cubierta",
            _ => null,
        };
    }

    public async Task<string?> AgregarPlantillaAsync(int etapaId, string plantilla, CancellationToken ct = default)
    {
        if (!Plantillas.TryGetValue(plantilla, out var items)) return "Plantilla inexistente.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existentes = await db.Controles.Where(c => c.EtapaObraId == etapaId).Select(c => new { c.Texto, c.Orden }).ToListAsync(ct);
        var orden = existentes.Count == 0 ? 0 : existentes.Max(e => e.Orden);
        foreach (var (texto, foto) in items.Where(i => existentes.All(e => e.Texto != i.Texto)))
            db.Controles.Add(new ControlEtapa { EtapaObraId = etapaId, Texto = texto, RequiereFoto = foto, Orden = ++orden });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> AgregarAsync(int etapaId, string texto, bool requiereFoto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length > 300) return "Escribí el control (hasta 300 caracteres).";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var orden = await db.Controles.Where(c => c.EtapaObraId == etapaId).MaxAsync(c => (int?)c.Orden, ct) ?? 0;
        db.Controles.Add(new ControlEtapa { EtapaObraId = etapaId, Texto = texto.Trim(), RequiereFoto = requiereFoto, Orden = orden + 1 });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Solo se borran controles sin respuestas (los respondidos quedan como historia de la obra).</summary>
    public async Task<string?> BorrarAsync(int controlId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.RespuestasControl.AnyAsync(r => r.ControlEtapaId == controlId, ct)) return "Ese control ya tiene respuestas: no se puede borrar.";
        await db.Controles.Where(c => c.Id == controlId).ExecuteDeleteAsync(ct);
        return null;
    }

    public sealed record EstadoControl(int Id, int EtapaId, string Texto, bool RequiereFoto, ResultadoControl? Resultado, DateTime? Fecha, string? Nota, string? Quien, int? RegistroId);

    /// <summary>Controles de la obra con su último resultado.</summary>
    public async Task<List<EstadoControl>> DeObraAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lista = await db.Controles.AsNoTracking().Where(c => c.Etapa.ObraId == obraId)
            .OrderBy(c => c.Etapa.Orden).ThenBy(c => c.Orden)
            .Select(c => new
            {
                c.Id, c.EtapaObraId, c.Texto, c.RequiereFoto,
                U = c.Respuestas.OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id)
                    .Select(r => new { r.Resultado, r.Fecha, r.Nota, Quien = r.Albanil != null ? r.Albanil.Nombre : r.Capataz != null ? r.Capataz.Nombre : null, r.RegistroAvanceId })
                    .FirstOrDefault(),
            }).ToListAsync(ct);
        return lista.Select(c => new EstadoControl(c.Id, c.EtapaObraId, c.Texto, c.RequiereFoto, c.U?.Resultado, c.U?.Fecha, c.U?.Nota, c.U?.Quien, c.U?.RegistroAvanceId)).ToList();
    }
}
