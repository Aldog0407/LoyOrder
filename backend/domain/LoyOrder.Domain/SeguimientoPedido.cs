namespace LoyOrder.Domain;

public sealed record ArticuloPedido(string NombreProducto, int Cantidad, decimal PrecioUnitario,
    decimal Subtotal, string? Indicaciones, long? IdVariante = null, long? IdProducto = null, string? Variante = null);
public sealed record CambioEstadoPedido(string? EstadoAnterior, string EstadoNuevo,
    DateTime FechaCambio, string? Motivo);
public sealed record SeguimientoPedido(string Folio, string Estado, string TipoEntrega,
    DateTime FechaCreacion, DateTime? FechaEntrega, decimal Subtotal, decimal CostoEnvio,
    decimal Total, short? Calificacion, string? Comentario,
    IReadOnlyList<ArticuloPedido> Articulos, IReadOnlyList<CambioEstadoPedido> Historial);
