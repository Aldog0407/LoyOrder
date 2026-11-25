using System.Data;
using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class PedidoClienteRepository(NpgsqlDataSource source) : IPedidoClienteRepository
{
    public async Task<SeguimientoPedido?> ConsultarAsync(long idUsuario, string folio, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var transaccion = await conexion.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT p.id_pedido, p.folio, p.estado, p.tipo_entrega, p.fecha_creacion, p.fecha_entrega,
                       p.subtotal, p.costo_envio, p.total, p.calificacion, p.comentario
                  FROM public.pedido p JOIN public.cliente c ON c.id_cliente = p.id_cliente
                 WHERE c.id_cliente = $1 AND p.folio = $2
                """, conexion, transaccion);
            cmd.Parameters.AddWithValue(idUsuario);
            cmd.Parameters.AddWithValue(folio);
            long idPedido;
            SeguimientoPedido pedido;
            var articulos = new List<ArticuloPedido>();
            var historial = new List<CambioEstadoPedido>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) return null;
                idPedido = reader.GetInt64(0);
                pedido = new(reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4),
                    reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.GetDecimal(6), reader.GetDecimal(7),
                    reader.GetDecimal(8), reader.IsDBNull(9) ? null : reader.GetInt16(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10), articulos, historial);
            }
            await using var detalles = new NpgsqlCommand("""
                SELECT nombre_producto, cantidad, precio_unitario, subtotal, indicaciones
                  FROM public.detalle_pedido WHERE id_pedido = $1 ORDER BY id_detalle
                """, conexion, transaccion);
            detalles.Parameters.AddWithValue(idPedido);
            await using (var reader = await detalles.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    articulos.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2),
                        reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            await using var cambios = new NpgsqlCommand("""
                SELECT estado_anterior, estado_nuevo, fecha_cambio, motivo FROM public.historial_estado
                 WHERE id_pedido = $1 ORDER BY fecha_cambio, id_historial
                """, conexion, transaccion);
            cambios.Parameters.AddWithValue(idPedido);
            await using (var reader = await cambios.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    historial.Add(new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1),
                        reader.GetDateTime(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            await transaccion.CommitAsync(ct);
            return pedido;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task CalificarAsync(long idUsuario, string folio, short calificacion, string? comentario, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var transaccion = await conexion.BeginTransactionAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT p.id_pedido, p.estado, p.calificacion, p.comentario
                  FROM public.pedido p JOIN public.cliente c ON c.id_cliente = p.id_cliente
                 WHERE c.id_cliente = $1 AND p.folio = $2 FOR UPDATE OF p
                """, conexion, transaccion);
            cmd.Parameters.AddWithValue(idUsuario);
            cmd.Parameters.AddWithValue(folio);
            long idPedido;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new PedidoNoEncontradoException();
                idPedido = reader.GetInt64(0);
                if (reader.GetString(1) != "Entregado") throw new CalificacionNoPermitidaException();
                if (!reader.IsDBNull(2))
                {
                    if (reader.GetInt16(2) == calificacion && (reader.IsDBNull(3) ? null : reader.GetString(3)) == comentario)
                        return; // Reintentar el mismo envío conserva la calificación ya guardada.
                    throw new CalificacionNoPermitidaException();
                }
            }
            await using var actualizar = new NpgsqlCommand("""
                UPDATE public.pedido SET calificacion = $2, comentario = $3 WHERE id_pedido = $1
                """, conexion, transaccion);
            actualizar.Parameters.AddWithValue(idPedido);
            actualizar.Parameters.AddWithValue(calificacion);
            actualizar.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)comentario ?? DBNull.Value });
            await actualizar.ExecuteNonQueryAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
