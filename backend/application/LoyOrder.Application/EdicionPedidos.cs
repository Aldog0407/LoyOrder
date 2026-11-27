using System.Text.Json.Serialization;

namespace LoyOrder.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgregarArticuloPedido(long Revision, long IdVariante, int Cantidad, decimal PrecioEsperado);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnularArticuloPedido(long Revision, long IdDetalle, string Motivo);

public sealed record LineaEdicionPedido(long Id, long IdVariante, string Nombre, string? Variante, int Cantidad,
    decimal PrecioUnitario, decimal Subtotal, string? Indicaciones = null);
public sealed record CambioArticuloPedido(long Id, DateTime Fecha, string Operador, string Descripcion);
public sealed record DetalleEdicionPedido(string Folio, string Estado, bool Editable, long Revision,
    decimal Subtotal, decimal CostoEnvio, decimal Total, IReadOnlyList<LineaEdicionPedido> Articulos,
    IReadOnlyList<CambioArticuloPedido> Cambios);
public sealed record DatosEdicionPedido(long IdRestaurante, string? IdTiendaLoyverse, DetalleEdicionPedido Pedido);
public sealed record EditorPedido(DetalleEdicionPedido Pedido, IReadOnlyList<CategoriaMenu> Catalogo);

public interface IEdicionPedidoRepository
{
    Task<DatosEdicionPedido> ConsultarAsync(long operador, string folio, CancellationToken ct);
    Task AgregarAsync(long operador, string folio, AgregarArticuloPedido datos, VariantePedido variante,
        string? tienda, CancellationToken ct);
    Task AnularAsync(long operador, string folio, AnularArticuloPedido datos, CancellationToken ct);
}

public sealed class EdicionPedidos(IEdicionPedidoRepository pedidos, IMenuRepository menu,
    ICrearPedidoRepository catalogo, IOrigenCatalogo origen)
{
    public async Task<EditorPedido> ConsultarAsync(long operador, string folio, CancellationToken ct)
    {
        ValidarFolio(folio);
        var datos = await pedidos.ConsultarAsync(operador, folio, ct);
        var categorias = datos.Pedido.Editable ? ConsultarMenu.Agrupar(await menu.ListarAsync(datos.IdRestaurante, ct)) : [];
        return new(datos.Pedido, categorias);
    }

    public async Task AgregarAsync(long operador, string folio, AgregarArticuloPedido datos, CancellationToken ct)
    {
        ValidarFolio(folio);
        if (datos.Revision < 0 || datos.IdVariante <= 0 || datos.Cantidad is < 1 or > 20 ||
            datos.PrecioEsperado is <= 0 or > 9_999_999_999.99m || decimal.Round(datos.PrecioEsperado, 2) != datos.PrecioEsperado)
            throw new SolicitudInvalidaException("Revisa el artículo, su precio y la cantidad (de 1 a 20).");

        var editor = await pedidos.ConsultarAsync(operador, folio, ct);
        ValidarEdicion(editor.Pedido.Editable, editor.Pedido.Revision, datos.Revision);
        var variantes = await catalogo.VariantesAsync(editor.IdRestaurante, [datos.IdVariante], ct);
        var variante = variantes.SingleOrDefault();
        if (variante is null || variante.Precio != datos.PrecioEsperado)
            throw new CheckoutCambioException("El precio o la disponibilidad cambió. Actualiza el pedido.");
        var cantidad = editor.Pedido.Articulos.Where(a => a.IdVariante == variante.Id).Sum(a => a.Cantidad) + datos.Cantidad;
        if (cantidad > 20)
            throw new CheckoutCambioException("Un pedido admite hasta 20 unidades por variante.");

        if (variante.ControlaInventario)
        {
            var existencias = await CrearPedido.ConsultarExistenciasAsync(origen, editor.IdTiendaLoyverse, [variante], ct);
            if (!existencias.TryGetValue(variante.IdLoyverse!, out var disponible) || disponible < cantidad)
                throw new CheckoutCambioException($"No hay existencias suficientes de {variante.Nombre}.");
        }
        await pedidos.AgregarAsync(operador, folio, datos, variante, editor.IdTiendaLoyverse, ct);
    }

    public Task AnularAsync(long operador, string folio, AnularArticuloPedido datos, CancellationToken ct)
    {
        ValidarFolio(folio);
        if (datos.Revision < 0 || datos.IdDetalle <= 0 || string.IsNullOrWhiteSpace(datos.Motivo) || datos.Motivo.Length > 1000)
            throw new SolicitudInvalidaException("Selecciona el artículo e indica un motivo de hasta 1000 caracteres.");
        return pedidos.AnularAsync(operador, folio, datos with { Motivo = datos.Motivo.Trim() }, ct);
    }

    public static void ValidarEdicion(bool editable, long revisionActual, long revisionEsperada)
    {
        if (!editable)
            throw new CheckoutCambioException("El pedido solo se puede modificar mientras esté recibido o en preparación.");
        if (revisionActual != revisionEsperada)
            throw new CheckoutCambioException("El pedido ya cambió. Actualiza el detalle antes de continuar.");
    }

    private static void ValidarFolio(string folio)
    {
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 30)
            throw new SolicitudInvalidaException("Escribe un folio de hasta 30 caracteres.");
    }
}
