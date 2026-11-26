namespace LoyOrder.Application;

public sealed record LineaVenta(string VarianteId, decimal Cantidad, decimal Precio, string? Nota);
public sealed record DatosVenta(long IdPedido,string Folio,string TiendaId,string PagoId,string TipoEntrega,
    decimal Total,decimal CostoEnvio,DateTime FechaEntrega,IReadOnlyList<LineaVenta> Lineas,Guid Correlacion=default);
public sealed record TrabajoVenta(long IdRegistro,Guid Proceso,bool SoloVerificar,DatosVenta Datos);
public sealed record ResultadoVenta(string Estado,string? Recibo,string? Error);
public interface IVentaRepository
{
    Task ReintentarAsync(long operador,string folio,CancellationToken ct);
    Task<bool> DisponibleAsync(CancellationToken ct);
    Task<TrabajoVenta?> TomarAsync(CancellationToken ct);
    Task FinalizarAsync(TrabajoVenta trabajo,ResultadoVenta resultado,CancellationToken ct);
}
public interface IDestinoVenta
{
    Task<ResultadoVenta> RegistrarAsync(DatosVenta datos,bool soloVerificar,CancellationToken ct);
}
public sealed class RegistrarVentas(IVentaRepository repositorio,IDestinoVenta destino)
{
    public async Task<bool> ProcesarUnaAsync(CancellationToken ct)
    {
        var trabajo=await repositorio.TomarAsync(ct);
        if(trabajo is null) return false;
        // Ante una interrupción queda En proceso; la siguiente ejecución solo reconcilia.
        var resultado=await destino.RegistrarAsync(trabajo.Datos,trabajo.SoloVerificar,ct);
        await repositorio.FinalizarAsync(trabajo,resultado,ct);
        return true;
    }
}
