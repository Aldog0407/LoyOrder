using LoyOrder.Domain;

namespace LoyOrder.Application;

public sealed record PedidoOperativo(string Folio, string Estado, string TipoEntrega, DateTime FechaCreacion,
    decimal Total, string Cliente, IReadOnlyList<ArticuloPedido> Articulos);
public sealed record EstadoPedidoActualizado(string Folio, string Estado);
public interface IOperacionPedidoRepository
{
    Task<IReadOnlyList<PedidoOperativo>> ListarAsync(long idOperador, CancellationToken ct);
    Task<EstadoPedidoActualizado> CambiarAsync(long idOperador, string folio, string estado, string? motivo, CancellationToken ct);
}
public sealed class OperacionPedidos(IOperacionPedidoRepository pedidos)
{
    public Task<IReadOnlyList<PedidoOperativo>> ConsultarAsync(long operador, CancellationToken ct) => pedidos.ListarAsync(operador,ct);
    public Task<EstadoPedidoActualizado> CambiarAsync(long operador, string folio, string estado, string? motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 30 ||
            estado is not ("Recibido" or "En preparación" or "Enviado" or "Listo para recoger" or "Entregado" or "Cancelado") ||
            motivo?.Length > 1000 || estado == "Cancelado" && string.IsNullOrWhiteSpace(motivo))
            throw new SolicitudInvalidaException("Revisa el folio, estado y motivo de cancelación (hasta 1000 caracteres).");
        return pedidos.CambiarAsync(operador,folio,estado,string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(),ct);
    }
}

public interface IEnvioCorreo
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken ct);
}
public interface INotificacionPedidoRepository
{
    Task<bool> ProcesarUnaAsync(Func<MensajeCorreo,CancellationToken,Task> enviar, CancellationToken ct);
}
