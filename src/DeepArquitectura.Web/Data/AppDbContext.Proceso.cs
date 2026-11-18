using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Data;

/// <summary>Tablas de «Datos del proceso»: comercial, costos reales, trámites, entrega, garantía, encuestas y pagos informados.</summary>
public partial class AppDbContext
{
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<Relevamiento> Relevamientos => Set<Relevamiento>();
    public DbSet<GastoObra> Gastos => Set<GastoObra>();
    public DbSet<MaterialSobrante> Sobrantes => Set<MaterialSobrante>();
    public DbSet<TramiteObra> Tramites => Set<TramiteObra>();
    public DbSet<MovimientoTramite> MovimientosTramite => Set<MovimientoTramite>();
    public DbSet<ActaEntrega> Actas => Set<ActaEntrega>();
    public DbSet<ObservacionEntrega> ObservacionesEntrega => Set<ObservacionEntrega>();
    public DbSet<ReclamoGarantia> Reclamos => Set<ReclamoGarantia>();
    public DbSet<EncuestaCliente> Encuestas => Set<EncuestaCliente>();
    public DbSet<PermisoDifusion> Permisos => Set<PermisoDifusion>();
    public DbSet<PagoInformado> PagosInformados => Set<PagoInformado>();

    // Regla de siempre: solo la obra (y el dueño directo de un adjunto) borran en cascada; el resto queda en
    // ClientSetNull para que SQL Server no se queje de caminos de cascada múltiples.
    private static void ConfigurarDatosProceso(ModelBuilder m)
    {
        m.Entity<Consulta>(e =>
        {
            e.Property(x => x.UtmFuente).HasMaxLength(100);
            e.Property(x => x.UtmMedio).HasMaxLength(100);
            e.Property(x => x.UtmCampania).HasMaxLength(150);
            e.Property(x => x.Referente).HasMaxLength(200);
            e.Property(x => x.PaginaEntrada).HasMaxLength(300);
            e.HasIndex(x => new { x.Canal, x.CreadoEn });
            e.HasOne(x => x.Publicacion).WithMany().HasForeignKey(x => x.PublicacionId).OnDelete(DeleteBehavior.SetNull);
        });

        m.Entity<Presupuesto>(e => e.Property(x => x.DetallePerdida).HasMaxLength(1000));
        m.Entity<RecepcionMaterial>(e => e.Property(x => x.NumeroFactura).HasMaxLength(60));
        m.Entity<RecepcionItem>(e => e.Property(x => x.PrecioUnitario).HasPrecision(18, 2));
        m.Entity<TareaObra>(e => e.Property(x => x.DetalleAtraso).HasMaxLength(500));

        m.Entity<Adjunto>(e =>
        {
            e.ToTable("Adjuntos");
            e.Property(x => x.Ruta).HasMaxLength(300).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.NombreOriginal).HasMaxLength(260);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Relevamiento).WithMany(r => r.Adjuntos).HasForeignKey(x => x.RelevamientoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Gasto).WithMany(g => g.Adjuntos).HasForeignKey(x => x.GastoObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Recepcion).WithMany().HasForeignKey(x => x.RecepcionMaterialId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Observacion).WithMany(o => o.Adjuntos).HasForeignKey(x => x.ObservacionEntregaId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Reclamo).WithMany(r => r.Adjuntos).HasForeignKey(x => x.ReclamoGarantiaId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.PagoInformado).WithMany(p => p.Adjuntos).HasForeignKey(x => x.PagoInformadoId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<Relevamiento>(e =>
        {
            e.ToTable("Relevamientos");
            e.Property(x => x.Contacto).HasMaxLength(200);
            e.Property(x => x.Direccion).HasMaxLength(300);
            e.Property(x => x.Localidad).HasMaxLength(120);
            e.Property(x => x.EstadoGeneral).HasMaxLength(40);
            e.Property(x => x.Hallazgos).HasMaxLength(1000);
            e.Property(x => x.Pedido).HasMaxLength(4000);
            e.Property(x => x.PlazoDeseado).HasMaxLength(200);
            e.Property(x => x.Notas).HasMaxLength(4000);
            e.Property(x => x.FrenteM).HasPrecision(10, 2);
            e.Property(x => x.FondoM).HasPrecision(10, 2);
            e.Property(x => x.SuperficieTerrenoM2).HasPrecision(12, 2);
            e.Property(x => x.SuperficieCubiertaM2).HasPrecision(12, 2);
            e.HasIndex(x => x.Fecha);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Consulta).WithMany().HasForeignKey(x => x.ConsultaId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<GastoObra>(e =>
        {
            e.ToTable("GastosObra");
            e.Property(x => x.Descripcion).HasMaxLength(300).IsRequired();
            e.Property(x => x.Proveedor).HasMaxLength(150);
            e.Property(x => x.NumeroComprobante).HasMaxLength(60);
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasIndex(x => x.Ref).IsUnique().HasFilter("[Ref] IS NOT NULL");
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<MaterialSobrante>(e =>
        {
            e.ToTable("MaterialesSobrantes");
            e.Property(x => x.Material).HasMaxLength(150).IsRequired();
            e.Property(x => x.Unidad).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nota).HasMaxLength(500);
            e.Property(x => x.Cantidad).HasPrecision(12, 2);
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasIndex(x => x.Ref).IsUnique().HasFilter("[Ref] IS NOT NULL");
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ObraDestino).WithMany().HasForeignKey(x => x.ObraDestinoId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<TramiteObra>(e =>
        {
            e.ToTable("TramitesObra");
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Organismo).HasMaxLength(150);
            e.Property(x => x.Expediente).HasMaxLength(80);
            e.HasIndex(x => new { x.ObraId, x.Orden });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<MovimientoTramite>(e =>
        {
            e.ToTable("MovimientosTramite");
            e.Property(x => x.Nota).HasMaxLength(2000);
            e.HasOne(x => x.Tramite).WithMany(t => t.Movimientos).HasForeignKey(x => x.TramiteObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<ActaEntrega>(e =>
        {
            e.ToTable("ActasEntrega");
            e.Property(x => x.FirmadaPor).HasMaxLength(120);
            e.Property(x => x.FirmadaDesdeIp).HasMaxLength(60);
            e.Property(x => x.ComentarioCliente).HasMaxLength(2000);
            e.HasIndex(x => x.ObraId).IsUnique();
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ObservacionEntrega>(e =>
        {
            e.ToTable("ObservacionesEntrega");
            e.Property(x => x.Descripcion).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Ubicacion).HasMaxLength(150);
            e.Property(x => x.Rubro).HasMaxLength(80);
            e.Property(x => x.Responsable).HasMaxLength(150);
            e.Property(x => x.NotaResolucion).HasMaxLength(1000);
            e.HasOne(x => x.Acta).WithMany(a => a.Observaciones).HasForeignKey(x => x.ActaEntregaId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ReclamoGarantia>(e =>
        {
            e.ToTable("ReclamosGarantia");
            e.Property(x => x.Rubro).HasMaxLength(80).IsRequired();
            e.Property(x => x.Ubicacion).HasMaxLength(150);
            e.Property(x => x.Descripcion).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ReportadoPor).HasMaxLength(120);
            e.Property(x => x.Respuesta).HasMaxLength(2000);
            e.Property(x => x.Responsable).HasMaxLength(150);
            e.Property(x => x.NotaInterna).HasMaxLength(2000);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.Estado, x.Fecha });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<EncuestaCliente>(e =>
        {
            e.ToTable("EncuestasCliente");
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Comentario).HasMaxLength(2000);
            // Sin filtro: una sola encuesta de entrega por obra (EtapaObraId null) y una por etapa.
            e.HasIndex(x => new { x.ObraId, x.Momento, x.EtapaObraId }).IsUnique().HasFilter(null);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<PermisoDifusion>(e =>
        {
            e.ToTable("PermisosDifusion");
            e.Property(x => x.Testimonio).HasMaxLength(2000);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.DesdeIp).HasMaxLength(60);
            e.HasIndex(x => x.ObraId).IsUnique();
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<PagoInformado>(e =>
        {
            e.ToTable("PagosInformados");
            e.Property(x => x.Nota).HasMaxLength(1000);
            e.Property(x => x.InformadoPor).HasMaxLength(120);
            e.Property(x => x.MotivoRechazo).HasMaxLength(500);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.ObraId, x.Estado });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Certificado).WithMany().HasForeignKey(x => x.CertificadoId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Pago).WithMany().HasForeignKey(x => x.PagoId).OnDelete(DeleteBehavior.ClientSetNull);
        });
    }
}
