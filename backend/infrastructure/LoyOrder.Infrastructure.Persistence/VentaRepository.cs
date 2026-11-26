using System.Text.Json;
using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class VentaRepository(NpgsqlDataSource fuente) : IVentaRepository
{
    public async Task ReintentarAsync(long operador,string folio,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(folio)||folio.Length>30) throw new SolicitudInvalidaException("Folio inválido.");
        try {
            await using var con=await fuente.OpenConnectionAsync(ct);
            await using var tx=await con.BeginTransactionAsync(ct);
            await using var buscar=new NpgsqlCommand("""
                SELECT rv.id_registro,rv.estado_sincronizacion FROM public.registro_venta rv
                JOIN public.pedido p ON p.id_pedido=rv.id_pedido JOIN public.metodo_pago m ON m.id_metodo_pago=p.id_metodo_pago
                JOIN public.empleado e ON e.id_restaurante=m.id_restaurante JOIN public.usuario u ON u.id_usuario=e.id_empleado
                WHERE p.folio=$1 AND p.estado='Entregado' AND e.id_empleado=$2 AND e.rol='OPERADOR' AND u.activo FOR UPDATE OF rv
                """,con,tx);
            buscar.Parameters.AddWithValue(folio);buscar.Parameters.AddWithValue(operador);
            long id;string estado;
            await using(var r=await buscar.ExecuteReaderAsync(ct)) {
                if(!await r.ReadAsync(ct)) throw new PedidoNoEncontradoException();
                id=r.GetInt64(0);estado=r.GetString(1);
            }
            if(estado=="Confirmado") return;
            if(estado=="En proceso") throw new CheckoutCambioException("La venta se está procesando. Actualiza la lista en unos momentos.");
            await using var actualizar=new NpgsqlCommand("""
                UPDATE public.registro_venta SET intentos=0,proximo_intento=statement_timestamp()
                WHERE id_registro=$1
                """,con,tx);
            actualizar.Parameters.AddWithValue(id);await actualizar.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
            // Por verificar conserva su estado: el botón solicita otra consulta, nunca un POST incierto.
        }
        catch(NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch(TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        try { await using var c=fuente.CreateCommand("SELECT iniciado_en,proximo_intento,proceso,solicitud FROM public.registro_venta LIMIT 0");
            await using var r=await c.ExecuteReaderAsync(ct); return true; }
        catch(NpgsqlException) { return false; }
        catch(TimeoutException) { return false; }
    }
    public async Task<TrabajoVenta?> TomarAsync(CancellationToken ct)
    {
        try {
            await using var con=await fuente.OpenConnectionAsync(ct);
            await using var tx=await con.BeginTransactionAsync(ct);
            await using var buscar=new NpgsqlCommand("""
                SELECT rv.id_registro,rv.id_pedido,rv.estado_sincronizacion,rv.solicitud::text
                FROM public.registro_venta rv JOIN public.pedido p ON p.id_pedido=rv.id_pedido
                WHERE p.estado='Entregado' AND rv.proximo_intento<=statement_timestamp() AND (
                    (rv.estado_sincronizacion IN ('Pendiente','Fallido') AND rv.intentos<5) OR
                    (rv.estado_sincronizacion='Por verificar' AND rv.intentos<20) OR
                    (rv.estado_sincronizacion='En proceso' AND (rv.iniciado_en IS NULL OR rv.iniciado_en<statement_timestamp()-interval '3 minutes')))
                ORDER BY rv.proximo_intento,rv.id_registro LIMIT 1 FOR UPDATE OF rv SKIP LOCKED
                """,con,tx);
            long registro,pedido;bool verificar;string? json;
            await using(var r=await buscar.ExecuteReaderAsync(ct)) {
                if(!await r.ReadAsync(ct)) return null;
                registro=r.GetInt64(0); pedido=r.GetInt64(1);
                verificar=r.GetString(2) is "Por verificar" or "En proceso";
                json=r.IsDBNull(3)?null:r.GetString(3);
            }
            DatosVenta datos;
            if(json is not null) datos=JsonSerializer.Deserialize<DatosVenta>(json)!;
            else {
                await using var cabecera=new NpgsqlCommand("""
                    SELECT p.folio,r.id_tienda_loyverse,m.id_loyverse,p.tipo_entrega,p.total,p.costo_envio,p.fecha_entrega,p.id_operacion
                    FROM public.pedido p JOIN public.metodo_pago m ON m.id_metodo_pago=p.id_metodo_pago
                    JOIN public.restaurante r ON r.id_restaurante=m.id_restaurante WHERE p.id_pedido=$1
                    """,con,tx);
                cabecera.Parameters.AddWithValue(pedido);
                await using(var r=await cabecera.ExecuteReaderAsync(ct)) {
                    if(!await r.ReadAsync(ct)) throw new DependenciaNoDisponibleException();
                    datos=new(pedido,r.GetString(0),r.IsDBNull(1)?"":r.GetString(1),r.GetString(2),r.GetString(3),r.GetDecimal(4),r.GetDecimal(5),
                        r.IsDBNull(6)?DateTime.UtcNow:r.GetDateTime(6),[],r.IsDBNull(7)?Guid.NewGuid():r.GetGuid(7));
                }
                await using var detalle=new NpgsqlCommand("""
                    SELECT v.id_loyverse,d.cantidad,d.precio_unitario,d.indicaciones
                    FROM public.detalle_pedido d JOIN public.variante_producto v ON v.id_variante=d.id_variante
                    WHERE d.id_pedido=$1 ORDER BY d.id_detalle
                    """,con,tx);
                detalle.Parameters.AddWithValue(pedido);
                var lineas=new List<LineaVenta>();
                await using(var r=await detalle.ExecuteReaderAsync(ct))
                    while(await r.ReadAsync(ct)) lineas.Add(new(r.IsDBNull(0)?"":r.GetString(0),r.GetInt32(1),r.GetDecimal(2),r.IsDBNull(3)?null:r.GetString(3)));
                datos=datos with { Lineas=lineas };
                json=JsonSerializer.Serialize(datos);
            }
            var proceso=Guid.NewGuid();
            await using var tomar=new NpgsqlCommand("""
                UPDATE public.registro_venta SET estado_sincronizacion='En proceso',iniciado_en=statement_timestamp(),
                    proceso=$2,intentos=intentos+1,solicitud=$3::jsonb WHERE id_registro=$1
                """,con,tx);
            tomar.Parameters.AddWithValue(registro); tomar.Parameters.AddWithValue(proceso); tomar.Parameters.AddWithValue(json);
            await tomar.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
            return new(registro,proceso,verificar,datos);
        }
        catch(NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch(TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
    public async Task FinalizarAsync(TrabajoVenta trabajo,ResultadoVenta resultado,CancellationToken ct)
    {
        if(resultado.Estado is not ("Confirmado" or "Fallido" or "Por verificar")) throw new ArgumentException("Estado de venta inválido");
        try {
            await using var cmd=fuente.CreateCommand("""
                UPDATE public.registro_venta SET estado_sincronizacion=$3,id_recibo_loyverse=$4,numero_recibo=$4,
                    fecha_registro=CASE WHEN $3='Confirmado' THEN statement_timestamp() ELSE NULL END,ultimo_error=$5,
                    proximo_intento=statement_timestamp()+interval '1 minute' * LEAST(30,power(2,LEAST(intentos,5)))
                WHERE id_registro=$1 AND proceso=$2 AND estado_sincronizacion='En proceso'
                """);
            cmd.Parameters.AddWithValue(trabajo.IdRegistro);cmd.Parameters.AddWithValue(trabajo.Proceso);cmd.Parameters.AddWithValue(resultado.Estado);
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Text,Value=(object?)resultado.Recibo??DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Text,Value=(object?)resultado.Error??DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch(NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch(TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
