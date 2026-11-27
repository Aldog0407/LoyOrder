using System.Text.Json;
using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class EdicionPedidoRepository(NpgsqlDataSource source) : IEdicionPedidoRepository
{
    private sealed record Cabecera(long Id, long Restaurante, string? Tienda, string Estado, bool Editable,
        decimal Subtotal, decimal Envio, decimal Total);

    public async Task<DatosEdicionPedido> ConsultarAsync(long operador, string folio, CancellationToken ct)
    {
        try
        {
            await using var con = await source.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            var cabecera = await LeerCabecera(con, tx, operador, folio, false, ct);
            var lineas = await LeerLineas(con, tx, cabecera.Id, ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT m.id_modificacion, m.fecha, u.nombre, m.descripcion
                FROM public.modificacion_pedido m JOIN public.usuario u ON u.id_usuario=m.id_empleado
                WHERE m.id_pedido=$1 ORDER BY m.id_modificacion DESC
                """, con, tx);
            cmd.Parameters.AddWithValue(cabecera.Id);
            var cambios = new List<CambioArticuloPedido>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    cambios.Add(new(r.GetInt64(0), r.GetDateTime(1), r.GetString(2), r.GetString(3)));
            await tx.CommitAsync(ct);
            return new(cabecera.Restaurante, cabecera.Tienda, new(folio, cabecera.Estado, cabecera.Editable,
                cambios.Count == 0 ? 0 : cambios[0].Id, cabecera.Subtotal, cabecera.Envio, cabecera.Total, lineas, cambios));
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public Task AgregarAsync(long operador, string folio, AgregarArticuloPedido datos, VariantePedido variante,
        string? tienda, CancellationToken ct) =>
        ModificarAsync(operador, folio, datos.Revision, async (con, tx, pedido, lineas) =>
        {
            var actuales = await CrearPedidoRepository.LeerVariantes(con, tx, pedido.Restaurante, [datos.IdVariante], true, ct);
            var actual = actuales.SingleOrDefault();
            if (actual is null || actual.IdLoyverse != variante.IdLoyverse || actual.Precio != variante.Precio ||
                actual.ControlaInventario != variante.ControlaInventario || actual.ControlaInventario && pedido.Tienda != tienda)
                throw new CheckoutCambioException("El precio o la disponibilidad cambió. Actualiza el pedido.");
            if (lineas.Count >= 100 || lineas.Where(l => l.IdVariante == actual.Id).Sum(l => l.Cantidad) + datos.Cantidad > 20)
                throw new CheckoutCambioException("El pedido admite hasta 100 renglones y 20 unidades por variante.");
            if (pedido.Total + actual.Precio * datos.Cantidad > 9_999_999_999.99m)
                throw new CheckoutCambioException("El total supera el importe permitido para un pedido.");

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO public.detalle_pedido(id_pedido,id_variante,nombre_producto,cantidad,precio_unitario,subtotal)
                VALUES ($1,$2,$3,$4,$5,$6)
                RETURNING id_detalle, (SELECT nombre FROM public.variante_producto WHERE id_variante=$2)
                """, con, tx);
            cmd.Parameters.AddWithValue(pedido.Id);
            cmd.Parameters.AddWithValue(actual.Id);
            cmd.Parameters.AddWithValue(actual.Nombre);
            cmd.Parameters.AddWithValue(datos.Cantidad);
            cmd.Parameters.AddWithValue(actual.Precio);
            var subtotal = actual.Precio * datos.Cantidad;
            cmd.Parameters.AddWithValue(subtotal);
            LineaEdicionPedido linea;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                await r.ReadAsync(ct);
                linea = new(r.GetInt64(0), actual.Id, actual.Nombre, r.IsDBNull(1) ? null : r.GetString(1),
                    datos.Cantidad, actual.Precio, subtotal);
            }
            await GuardarHistorial(con, tx, pedido.Id, operador, "Agregar artículo",
                $"Agregó {DescripcionLinea(linea)}.", null, linea, ct);
        }, ct);

    public Task AnularAsync(long operador, string folio, AnularArticuloPedido datos, CancellationToken ct) =>
        ModificarAsync(operador, folio, datos.Revision, async (con, tx, pedido, lineas) =>
        {
            var linea = lineas.SingleOrDefault(l => l.Id == datos.IdDetalle)
                ?? throw new CheckoutCambioException("Ese renglón ya no está en el pedido.");
            if (lineas.Count == 1)
                throw new CheckoutCambioException("El pedido debe conservar al menos un artículo. Usa la acción de rechazar o cancelar el pedido si corresponde.");
            await using var cmd = new NpgsqlCommand("""
                DELETE FROM public.detalle_pedido WHERE id_detalle=$1 AND id_pedido=$2
                """, con, tx);
            cmd.Parameters.AddWithValue(linea.Id);
            cmd.Parameters.AddWithValue(pedido.Id);
            await cmd.ExecuteNonQueryAsync(ct);
            await GuardarHistorial(con, tx, pedido.Id, operador, "Anular artículo",
                $"Anuló {DescripcionLinea(linea)}. Motivo: {datos.Motivo}", linea, null, ct);
        }, ct);

    private async Task ModificarAsync(long operador, string folio, long revision,
        Func<NpgsqlConnection, NpgsqlTransaction, Cabecera, IReadOnlyList<LineaEdicionPedido>, Task> aplicar,
        CancellationToken ct)
    {
        try
        {
            await using var con = await source.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            var pedido = await LeerCabecera(con, tx, operador, folio, true, ct);
            await using var consulta = new NpgsqlCommand("""
                SELECT COALESCE(MAX(id_modificacion),0) FROM public.modificacion_pedido WHERE id_pedido=$1
                """, con, tx);
            consulta.Parameters.AddWithValue(pedido.Id);
            var actual = (long)(await consulta.ExecuteScalarAsync(ct))!;
            EdicionPedidos.ValidarEdicion(pedido.Editable, actual, revision);
            var lineas = await LeerLineas(con, tx, pedido.Id, ct);
            await aplicar(con, tx, pedido, lineas);
            await using var actualizar = new NpgsqlCommand("""
                UPDATE public.pedido SET subtotal=importes.subtotal, total=importes.subtotal+costo_envio
                FROM (SELECT public.fn_subtotal_detalles($1) AS subtotal) AS importes
                WHERE id_pedido=$1
                """, con, tx);
            actualizar.Parameters.AddWithValue(pedido.Id);
            await actualizar.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    private static async Task<Cabecera> LeerCabecera(NpgsqlConnection con, NpgsqlTransaction tx, long operador,
        string folio, bool bloquear, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT p.id_pedido, r.id_restaurante, r.id_tienda_loyverse, p.estado,
                public.fn_pedido_es_modificable(p.estado), p.subtotal, p.costo_envio, p.total
            FROM public.pedido p JOIN public.metodo_pago mp ON mp.id_metodo_pago=p.id_metodo_pago
            JOIN public.restaurante r ON r.id_restaurante=mp.id_restaurante
            JOIN public.empleado e ON e.id_restaurante=r.id_restaurante
            JOIN public.usuario u ON u.id_usuario=e.id_empleado
            WHERE p.folio=$1 AND e.id_empleado=$2 AND e.rol='OPERADOR' AND u.activo
            """ + (bloquear ? " FOR UPDATE OF p" : ""), con, tx);
        cmd.Parameters.AddWithValue(folio);
        cmd.Parameters.AddWithValue(operador);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new PedidoNoEncontradoException();
        return new(r.GetInt64(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3),
            r.GetBoolean(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7));
    }

    private static async Task<IReadOnlyList<LineaEdicionPedido>> LeerLineas(NpgsqlConnection con, NpgsqlTransaction tx,
        long idPedido, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT d.id_detalle,d.id_variante,d.nombre_producto,v.nombre,d.cantidad,d.precio_unitario,d.subtotal,d.indicaciones
            FROM public.detalle_pedido d JOIN public.variante_producto v ON v.id_variante=d.id_variante
            WHERE d.id_pedido=$1 ORDER BY d.id_detalle
            """, con, tx);
        cmd.Parameters.AddWithValue(idPedido);
        var lineas = new List<LineaEdicionPedido>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lineas.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt32(4), r.GetDecimal(5), r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return lineas;
    }

    private static string DescripcionLinea(LineaEdicionPedido linea) => string.IsNullOrWhiteSpace(linea.Variante)
        ? $"{linea.Cantidad} × {linea.Nombre}"
        : $"{linea.Cantidad} × {linea.Nombre} ({linea.Variante})";

    private static async Task GuardarHistorial(NpgsqlConnection con, NpgsqlTransaction tx, long pedido, long operador,
        string tipo, string descripcion, LineaEdicionPedido? antes, LineaEdicionPedido? despues, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO public.modificacion_pedido(id_pedido,id_empleado,tipo_cambio,descripcion,datos_anteriores,datos_nuevos)
            VALUES ($1,$2,$3,$4,$5,$6)
            """, con, tx);
        cmd.Parameters.AddWithValue(pedido);
        cmd.Parameters.AddWithValue(operador);
        cmd.Parameters.AddWithValue(tipo);
        cmd.Parameters.AddWithValue(descripcion);
        cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, antes is null ? (object)DBNull.Value : JsonSerializer.Serialize(antes));
        cmd.Parameters.AddWithValue(NpgsqlDbType.Jsonb, despues is null ? (object)DBNull.Value : JsonSerializer.Serialize(despues));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
