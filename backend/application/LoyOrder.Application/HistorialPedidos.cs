using LoyOrder.Domain;

namespace LoyOrder.Application;

public sealed record ResumenPedidoCliente(string Folio, string Estado, string TipoEntrega, DateTime FechaCreacion,
    decimal Subtotal, decimal CostoEnvio, decimal Total, short? Calificacion, IReadOnlyList<ArticuloPedido> Articulos);
public interface IHistorialPedidoRepository
{
    Task<IReadOnlyList<ResumenPedidoCliente>> ListarAsync(long idUsuario, int pagina, int tamano, CancellationToken ct);
}
public sealed class HistorialPedidos(IHistorialPedidoRepository pedidos)
{
    public Task<IReadOnlyList<ResumenPedidoCliente>> ConsultarAsync(long idUsuario, int pagina, int tamano, CancellationToken ct)
    {
        if (pagina is < 1 or > 1_000_000 || tamano is < 1 or > 50)
            throw new SolicitudInvalidaException("Revisa la página y el tamaño de consulta (1 a 50).");
        return pedidos.ListarAsync(idUsuario, pagina, tamano, ct);
    }
}
