using LoyOrder.Application;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class ReporteRepository(NpgsqlDataSource source) : IReporteRepository
{
    private const string Ventas = """
        SELECT v.folio, v.fecha_entrega, v.tipo_entrega, COALESCE(v.estado_sincronizacion, 'Sin registro'),
            p.subtotal, p.costo_envio, v.total, v.numero_recibo
        FROM public.vw_ventas_entregadas v JOIN public.pedido p ON p.id_pedido=v.id_pedido
        JOIN public.metodo_pago m ON m.id_metodo_pago=p.id_metodo_pago
        WHERE m.id_restaurante=$1 AND v.fecha_entrega >= $2 AND v.fecha_entrega < $3
        ORDER BY v.fecha_entrega DESC, p.id_pedido DESC
        LIMIT 10001
        """;

    private const string Pedidos = """
        SELECT p.folio, p.fecha_creacion, p.tipo_entrega, p.estado, p.subtotal, p.costo_envio, p.total, NULL::text
        FROM public.pedido p JOIN public.metodo_pago m ON m.id_metodo_pago=p.id_metodo_pago
        WHERE m.id_restaurante=$1 AND p.fecha_creacion >= $2 AND p.fecha_creacion < $3
        ORDER BY p.fecha_creacion DESC, p.id_pedido DESC
        LIMIT 10001
        """;

    public async Task<IReadOnlyList<RegistroReporte>> ConsultarAsync(long administrador, TipoReporte tipo,
        DateTimeOffset inicio, DateTimeOffset fin, CancellationToken ct)
    {
        try
        {
            await using var con = await source.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
            await using var cuenta = new NpgsqlCommand("""
                SELECT e.id_restaurante FROM public.empleado e JOIN public.usuario u ON u.id_usuario=e.id_empleado
                WHERE e.id_empleado=$1 AND e.rol='ADMINISTRADOR' AND u.activo
                """, con, tx);
            cuenta.Parameters.AddWithValue(administrador);
            if (await cuenta.ExecuteScalarAsync(ct) is not long restaurante) throw new AccesoReporteException();

            await using var cmd = new NpgsqlCommand(tipo == TipoReporte.Ventas ? Ventas : Pedidos, con, tx);
            cmd.Parameters.AddWithValue(restaurante);
            cmd.Parameters.AddWithValue(inicio);
            cmd.Parameters.AddWithValue(fin);
            var filas = new List<RegistroReporte>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (filas.Count == 10000)
                        throw new SolicitudInvalidaException("El rango contiene más de 10 000 registros. Selecciona menos días.");
                    filas.Add(new(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2),
                        reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)));
                }
            }
            await tx.CommitAsync(ct);
            return filas;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
