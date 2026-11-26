using System.Globalization;
using LoyOrder.Application;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class NotificacionPedidoRepository(NpgsqlDataSource source) : INotificacionPedidoRepository
{
    internal static async Task EncolarAsync(NpgsqlConnection conexion,NpgsqlTransaction tx,long pedido,long historial,
        string folio,string estado,string tipoEntrega,decimal total,CancellationToken ct,string? motivo=null)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO public.notificacion(id_pedido,id_usuario,id_historial,asunto,mensaje)
            SELECT p.id_pedido,p.id_cliente,$2,$3,$4 FROM public.pedido p WHERE p.id_pedido=$1
            ON CONFLICT (id_historial) WHERE id_historial IS NOT NULL DO NOTHING
            """,conexion,tx);
        cmd.Parameters.AddWithValue(pedido);
        cmd.Parameters.AddWithValue(historial);
        cmd.Parameters.AddWithValue($"LoyOrder · Pedido {folio} · {estado}");
        cmd.Parameters.AddWithValue($"Tu pedido {folio} está en estado {estado}."+(estado=="Cancelado"&&!string.IsNullOrWhiteSpace(motivo)?$"\nMotivo: {motivo}":"")+$"\nModalidad: {tipoEntrega}.\nTotal: {total.ToString("0.00",CultureInfo.InvariantCulture)} MXN.\nConsulta los detalles y el historial en LoyOrder.");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ProcesarUnaAsync(Func<MensajeCorreo,CancellationToken,Task> enviar,CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            await using var buscar = new NpgsqlCommand("""
                SELECT n.id_notificacion,u.correo,n.asunto,n.mensaje,n.intentos
                  FROM public.notificacion n JOIN public.usuario u ON u.id_usuario=n.id_usuario
                 WHERE n.id_historial IS NOT NULL AND n.estado_envio IN ('Pendiente','Fallida') AND n.intentos<5
                   AND n.proximo_intento<=statement_timestamp() AND u.activo AND u.correo_verificado_en IS NOT NULL
                 ORDER BY n.proximo_intento,n.id_notificacion LIMIT 1 FOR UPDATE OF n SKIP LOCKED
                """,conexion,tx);
            long id; short intentos; MensajeCorreo mensaje;
            await using (var r = await buscar.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct)) return false;
                id = r.GetInt64(0); intentos = r.GetInt16(4);
                mensaje = new(r.GetString(1),r.GetString(2),r.GetString(3),$"notificacion-{id}@loyorder.local");
            }
            string? error = null;
            try { await enviar(mensaje,ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { error = e.GetType().Name; }
            await using var guardar = new NpgsqlCommand("""
                UPDATE public.notificacion SET estado_envio=$2,intentos=intentos+1,
                    fecha_envio=CASE WHEN $3 THEN statement_timestamp() ELSE NULL END,
                    proximo_intento=statement_timestamp()+$4,ultimo_error=$5 WHERE id_notificacion=$1
                """,conexion,tx);
            guardar.Parameters.AddWithValue(id);
            guardar.Parameters.AddWithValue(error is null ? "Enviada" : "Fallida");
            guardar.Parameters.AddWithValue(error is null);
            guardar.Parameters.AddWithValue(TimeSpan.FromMinutes(Math.Min(30,Math.Pow(2,intentos))));
            guardar.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Text,Value=(object?)error ?? DBNull.Value });
            await guardar.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
