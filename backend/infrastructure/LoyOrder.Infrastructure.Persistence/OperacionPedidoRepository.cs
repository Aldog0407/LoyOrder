using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class OperacionPedidoRepository(NpgsqlDataSource source) : IOperacionPedidoRepository
{
    public async Task<IReadOnlyList<PedidoOperativo>> ListarAsync(long operador,CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                WITH visibles AS (
                    SELECT p.*,u.nombre AS cliente,rv.estado_sincronizacion AS estado_venta,rv.numero_recibo,rv.ultimo_error AS error_venta FROM public.pedido p
                    LEFT JOIN public.registro_venta rv ON rv.id_pedido=p.id_pedido
                    JOIN public.metodo_pago mp ON mp.id_metodo_pago=p.id_metodo_pago
                    JOIN public.empleado e ON e.id_restaurante=mp.id_restaurante
                    JOIN public.usuario eu ON eu.id_usuario=e.id_empleado
                    JOIN public.usuario u ON u.id_usuario=p.id_cliente
                    WHERE e.id_empleado=$1 AND e.rol='OPERADOR' AND eu.activo
                    ORDER BY (p.estado IN ('Entregado','Cancelado')),p.fecha_creacion DESC,p.id_pedido DESC LIMIT 50
                )
                SELECT p.id_pedido,p.folio,p.estado,p.tipo_entrega,p.fecha_creacion,p.total,p.cliente,
                    d.nombre_producto,d.cantidad,d.precio_unitario,d.subtotal,d.indicaciones,d.id_variante,v.id_producto,v.nombre,p.estado_venta,p.numero_recibo,p.error_venta
                FROM visibles p LEFT JOIN public.detalle_pedido d ON d.id_pedido=p.id_pedido
                LEFT JOIN public.variante_producto v ON v.id_variante=d.id_variante
                ORDER BY (p.estado IN ('Entregado','Cancelado')),p.fecha_creacion DESC,p.id_pedido DESC,d.id_detalle
                """);
            cmd.Parameters.AddWithValue(operador);
            var pedidos = new List<PedidoOperativo>();
            var porId = new Dictionary<long,List<ArticuloPedido>>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt64(0);
                if (!porId.TryGetValue(id,out var articulos))
                {
                    articulos=[]; porId.Add(id,articulos);
                    pedidos.Add(new(r.GetString(1),r.GetString(2),r.GetString(3),r.GetDateTime(4),r.GetDecimal(5),r.GetString(6),articulos,r.IsDBNull(15)?null:r.GetString(15),r.IsDBNull(16)?null:r.GetString(16),r.IsDBNull(17)?null:r.GetString(17)));
                }
                if (!r.IsDBNull(7)) articulos.Add(new(r.GetString(7),r.GetInt32(8),r.GetDecimal(9),r.GetDecimal(10),
                    r.IsDBNull(11) ? null : r.GetString(11),r.GetInt64(12),r.GetInt64(13),r.IsDBNull(14) ? null : r.GetString(14)));
            }
            return pedidos;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<EstadoPedidoActualizado> CambiarAsync(long operador,string folio,string estado,string? motivo,CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            await using var buscar = new NpgsqlCommand("""
                SELECT p.id_pedido,p.estado,p.tipo_entrega,p.total FROM public.pedido p
                JOIN public.metodo_pago mp ON mp.id_metodo_pago=p.id_metodo_pago
                JOIN public.empleado e ON e.id_restaurante=mp.id_restaurante
                JOIN public.usuario u ON u.id_usuario=e.id_empleado
                WHERE p.folio=$1 AND e.id_empleado=$2 AND e.rol='OPERADOR' AND u.activo FOR UPDATE OF p
                """,conexion,tx);
            buscar.Parameters.AddWithValue(folio); buscar.Parameters.AddWithValue(operador);
            long id; string tipoEntrega; decimal total;
            await using (var r = await buscar.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) throw new PedidoNoEncontradoException();
                id=r.GetInt64(0); tipoEntrega=r.GetString(2); total=r.GetDecimal(3);
                if (r.GetString(1) == estado) return new(folio,estado);
            }
            await using var cambiar = new NpgsqlCommand("CALL public.sp_cambiar_estado_pedido($1,$2::varchar,$3,$4)",conexion,tx);
            cambiar.Parameters.AddWithValue(id); cambiar.Parameters.AddWithValue(estado); cambiar.Parameters.AddWithValue(operador);
            cambiar.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Text,Value=(object?)motivo ?? DBNull.Value });
            await cambiar.ExecuteNonQueryAsync(ct);
            await using var evento = new NpgsqlCommand("SELECT id_historial FROM public.historial_estado WHERE id_pedido=$1 ORDER BY id_historial DESC LIMIT 1",conexion,tx);
            evento.Parameters.AddWithValue(id);
            var historial = (long)(await evento.ExecuteScalarAsync(ct))!;
            await NotificacionPedidoRepository.EncolarAsync(conexion,tx,id,historial,folio,estado,tipoEntrega,total,ct,motivo);
            await tx.CommitAsync(ct);
            return new(folio,estado);
        }
        catch (PostgresException e) when (e.SqlState == "P0001") { throw new CheckoutCambioException("El pedido cambió o la transición no está permitida. Actualiza la lista."); }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
