using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class HistorialPedidoRepository(NpgsqlDataSource source) : IHistorialPedidoRepository
{
    public async Task<IReadOnlyList<ResumenPedidoCliente>> ListarAsync(long idUsuario, int pagina, int tamano, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                WITH ultimos AS (
                    SELECT * FROM public.pedido WHERE id_cliente=$1
                    ORDER BY fecha_creacion DESC,id_pedido DESC LIMIT $2 OFFSET $3
                )
                SELECT p.id_pedido,p.folio,p.estado,p.tipo_entrega,p.fecha_creacion,p.subtotal,p.costo_envio,p.total,p.calificacion,
                       d.nombre_producto,d.cantidad,d.precio_unitario,d.subtotal,d.indicaciones,d.id_variante,v.id_producto,v.nombre
                  FROM ultimos p LEFT JOIN public.detalle_pedido d ON d.id_pedido=p.id_pedido
                  LEFT JOIN public.variante_producto v ON v.id_variante=d.id_variante
                  ORDER BY p.fecha_creacion DESC,p.id_pedido DESC,d.id_detalle
                """);
            cmd.Parameters.AddWithValue(idUsuario);
            cmd.Parameters.AddWithValue(tamano);
            cmd.Parameters.AddWithValue((pagina - 1) * tamano);
            var pedidos = new List<ResumenPedidoCliente>();
            var porId = new Dictionary<long, List<ArticuloPedido>>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var id = r.GetInt64(0);
                if (!porId.TryGetValue(id, out var articulos))
                {
                    articulos = [];
                    porId.Add(id, articulos);
                    pedidos.Add(new(r.GetString(1),r.GetString(2),r.GetString(3),r.GetDateTime(4),r.GetDecimal(5),r.GetDecimal(6),
                        r.GetDecimal(7),r.IsDBNull(8) ? null : r.GetInt16(8),articulos));
                }
                if (!r.IsDBNull(9)) articulos.Add(new(r.GetString(9),r.GetInt32(10),r.GetDecimal(11),r.GetDecimal(12),
                    r.IsDBNull(13) ? null : r.GetString(13),r.GetInt64(14),r.GetInt64(15),r.IsDBNull(16) ? null : r.GetString(16)));
            }
            return pedidos;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
