using System.Data;
using LoyOrder.Application;
using Npgsql;
namespace LoyOrder.Infrastructure.Persistence;

/// <summary>Datos del ticket de venta (7.2.11, CU-19) e impresora de caja. Solo lee.</summary>
public sealed class TicketRepository(NpgsqlDataSource source) : ITicketRepository
{
    public async Task<DatosTicket?> ObtenerAsync(long idOperador, string folio, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var transaccion = await conexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            // Igual que OperacionPedidoRepository: el restaurante del pedido sale de su método de pago y solo lo ve
            // un empleado activo con rol OPERADOR de ese restaurante.
            await using var cmd = new NpgsqlCommand("""
                SELECT p.id_pedido, r.id_restaurante, r.nombre, p.folio, p.estado, p.tipo_entrega, u.nombre, c.telefono,
                       d.calle, d.numero, d.colonia, d.referencias, mp.nombre, p.subtotal, p.costo_envio, p.total
                  FROM public.pedido p
                  JOIN public.cliente c ON c.id_cliente = p.id_cliente
                  JOIN public.usuario u ON u.id_usuario = c.id_cliente
                  JOIN public.metodo_pago mp ON mp.id_metodo_pago = p.id_metodo_pago
                  JOIN public.restaurante r ON r.id_restaurante = mp.id_restaurante
                  JOIN public.empleado e ON e.id_restaurante = mp.id_restaurante
                  JOIN public.usuario eu ON eu.id_usuario = e.id_empleado
                  LEFT JOIN public.direccion d ON d.id_direccion = p.id_direccion
                 WHERE p.folio = $1 AND e.id_empleado = $2 AND e.rol = 'OPERADOR' AND eu.activo
                """, conexion, transaccion);
            cmd.Parameters.AddWithValue(folio);
            cmd.Parameters.AddWithValue(idOperador);
            long idPedido;
            var articulos = new List<ArticuloTicket>();
            DatosTicket datos;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) return null;
                idPedido = r.GetInt64(0);
                datos = new DatosTicket(r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    r.GetString(6), Texto(r, 7), Texto(r, 8), Texto(r, 9), Texto(r, 10), Texto(r, 11), r.GetString(12),
                    r.GetDecimal(13), r.GetDecimal(14), r.GetDecimal(15), articulos);
            }
            await using var detalle = new NpgsqlCommand("""
                SELECT d.nombre_producto, v.nombre, d.cantidad, d.precio_unitario, d.subtotal, d.indicaciones
                  FROM public.detalle_pedido d JOIN public.variante_producto v ON v.id_variante = d.id_variante
                 WHERE d.id_pedido = $1 ORDER BY d.id_detalle
                """, conexion, transaccion);
            detalle.Parameters.AddWithValue(idPedido);
            await using (var r = await detalle.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    articulos.Add(new(r.GetString(0), Texto(r, 1), r.GetInt32(2), r.GetDecimal(3), r.GetDecimal(4), Texto(r, 5)));
            await transaccion.CommitAsync(ct);
            return datos;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<(long IdRestaurante, string Nombre)?> RestauranteDeAdministradorAsync(long idAdministrador, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                SELECT r.id_restaurante, r.nombre FROM public.empleado e
                  JOIN public.usuario u ON u.id_usuario = e.id_empleado
                  JOIN public.restaurante r ON r.id_restaurante = e.id_restaurante
                 WHERE e.id_empleado = $1 AND e.rol = 'ADMINISTRADOR' AND u.activo
                """);
            cmd.Parameters.AddWithValue(idAdministrador);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? (r.GetInt64(0), r.GetString(1)) : null;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<ImpresoraDestino?> ImpresoraCajaAsync(long idRestaurante, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand(
                "SELECT id_impresora, nombre, direccion_ip FROM public.impresora " +
                "WHERE id_restaurante = $1 AND tipo = 'Caja' AND activa ORDER BY id_impresora LIMIT 1");
            cmd.Parameters.AddWithValue(idRestaurante);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? new ImpresoraDestino(r.GetInt64(0), r.GetString(1), r.GetString(2).Trim()) : null;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    private static string? Texto(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
