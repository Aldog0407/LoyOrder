namespace LoyOrder.Application;

public enum TipoReporte { Ventas, Pedidos }
public sealed record RegistroReporte(string Folio, DateTimeOffset Fecha, string Modalidad, string Estado,
    decimal Subtotal, decimal Envio, decimal Total, string? Recibo);
public sealed record EstadoReporte(string Estado, int Cantidad, decimal Importe);
public sealed record ReporteRestaurante(TipoReporte Tipo, DateOnly Desde, DateOnly Hasta,
    IReadOnlyList<RegistroReporte> Registros, IReadOnlyList<EstadoReporte> Estados, decimal Importe);
public sealed class AccesoReporteException : Exception;

public interface IReporteRepository
{
    Task<IReadOnlyList<RegistroReporte>> ConsultarAsync(long administrador, TipoReporte tipo,
        DateTimeOffset inicio, DateTimeOffset fin, CancellationToken ct);
}

public sealed class ConsultarReportes(IReporteRepository repositorio, TimeZoneInfo zona)
{
    public async Task<ReporteRestaurante> EjecutarAsync(long administrador, string tipo, DateOnly desde,
        DateOnly hasta, CancellationToken ct)
    {
        var clase = tipo switch
        {
            "ventas" => TipoReporte.Ventas,
            "pedidos" => TipoReporte.Pedidos,
            _ => throw new SolicitudInvalidaException("Selecciona un reporte de ventas o pedidos.")
        };
        if (desde == DateOnly.MinValue || hasta == DateOnly.MaxValue || hasta < desde ||
            hasta.DayNumber - desde.DayNumber >= 366)
            throw new SolicitudInvalidaException("Indica un rango de fechas válido de hasta 366 días.");

        DateTimeOffset Limite(DateOnly dia)
        {
            var local = dia.ToDateTime(TimeOnly.MinValue);
            // Algunas zonas adelantan el reloj a medianoche.
            while (zona.IsInvalidTime(local)) local = local.AddMinutes(1);
            var desplazamiento = zona.IsAmbiguousTime(local) ? zona.GetAmbiguousTimeOffsets(local).Max() : zona.GetUtcOffset(local);
            return new DateTimeOffset(local, desplazamiento).ToUniversalTime();
        }

        var filas = await repositorio.ConsultarAsync(administrador, clase, Limite(desde), Limite(hasta.AddDays(1)), ct);
        var registros = filas.Select(r => r with { Fecha = TimeZoneInfo.ConvertTime(r.Fecha, zona) }).ToArray();
        var estados = registros.GroupBy(r => r.Estado).OrderBy(g => g.Key)
            .Select(g => new EstadoReporte(g.Key, g.Count(), g.Sum(r => r.Total))).ToArray();
        return new(clase, desde, hasta, registros, estados, registros.Sum(r => r.Total));
    }
}
