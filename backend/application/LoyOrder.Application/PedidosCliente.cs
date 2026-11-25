using LoyOrder.Domain;

namespace LoyOrder.Application;

public sealed class PedidoNoEncontradoException : Exception;
public sealed class CalificacionNoPermitidaException : Exception;

public interface IPedidoClienteRepository
{
    Task<SeguimientoPedido?> ConsultarAsync(long idUsuario, string folio, CancellationToken ct);
    Task CalificarAsync(long idUsuario, string folio, short calificacion, string? comentario, CancellationToken ct);
}

public sealed class PedidosCliente(IPedidoClienteRepository pedidos)
{
    public async Task<SeguimientoPedido> ConsultarAsync(long idUsuario, string folio, CancellationToken ct)
    {
        ValidarFolio(folio);
        return await pedidos.ConsultarAsync(idUsuario, folio, ct) ?? throw new PedidoNoEncontradoException();
    }

    public Task CalificarAsync(long idUsuario, string folio, short calificacion, string? comentario, CancellationToken ct)
    {
        ValidarFolio(folio);
        if (calificacion is < 1 or > 5 || comentario?.Length > 2000)
            throw new SolicitudInvalidaException("La calificación debe estar entre 1 y 5 y el comentario admite hasta 2000 caracteres.");
        return pedidos.CalificarAsync(idUsuario, folio, calificacion,
            string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(), ct);
    }

    private static void ValidarFolio(string folio)
    {
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 30)
            throw new SolicitudInvalidaException("Escribe un folio de hasta 30 caracteres.");
    }
}
