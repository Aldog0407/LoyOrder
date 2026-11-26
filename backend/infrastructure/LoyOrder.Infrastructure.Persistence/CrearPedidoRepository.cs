using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class CrearPedidoRepository(NpgsqlDataSource source) : ICrearPedidoRepository
{
    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("SELECT p.id_operacion,p.solicitud_hash,n.id_historial,n.intentos,n.proximo_intento,n.ultimo_error FROM public.pedido p CROSS JOIN public.notificacion n LIMIT 0");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return true;
        }
        catch (NpgsqlException) { return false; }
        catch (TimeoutException) { return false; }
    }

    public async Task<ConfiguracionPedido> ConfiguracionAsync(long? idRestaurante, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            return await LeerConfiguracion(conexion, null, idRestaurante, false, ct);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<IReadOnlyList<VariantePedido>> VariantesAsync(long idRestaurante, long[] ids, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            return await LeerVariantes(conexion, null, idRestaurante, ids, false, ct);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<PedidoCreado?> RecuperarAsync(long idUsuario, Guid operacion, byte[] hash, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            return await LeerAnterior(conexion, null, idUsuario, operacion, hash, ct);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<PedidoCreado> GuardarAsync(long idUsuario, SolicitudPedido solicitud, byte[] hash,
        ConfiguracionPedido configuracion, IReadOnlyList<VariantePedido> variantes, DateTimeOffset ahora,
        TimeZoneInfo zona, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            var llave = SHA256.HashData(Encoding.UTF8.GetBytes($"pedido:{idUsuario}:{solicitud.IdOperacion:N}"));
            await using (var candado = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", conexion, tx))
            {
                candado.Parameters.AddWithValue(BitConverter.ToInt64(llave));
                await candado.ExecuteNonQueryAsync(ct);
            }
            var anterior = await LeerAnterior(conexion, tx, idUsuario, solicitud.IdOperacion, hash, ct);
            if (anterior is not null) return anterior;
            await using (var usuario = new NpgsqlCommand("""
                SELECT u.id_usuario FROM public.usuario u JOIN public.cliente c ON c.id_cliente=u.id_usuario
                 WHERE u.id_usuario=$1 AND u.activo AND NOT EXISTS
                    (SELECT 1 FROM public.empleado e WHERE e.id_empleado=u.id_usuario) FOR SHARE OF u,c
                """, conexion, tx))
            {
                usuario.Parameters.AddWithValue(idUsuario);
                if (await usuario.ExecuteScalarAsync(ct) is null) throw new CredencialesInvalidasException();
            }
            var actual = await LeerConfiguracion(conexion, tx, configuracion.IdRestaurante, true, ct);
            CrearPedido.ValidarConfiguracion(actual, solicitud, ahora, zona);
            if (actual.IdTiendaLoyverse != configuracion.IdTiendaLoyverse)
                throw new CheckoutCambioException("La tienda del restaurante cambió. Vuelve a confirmar el pedido.");
            var actuales = await LeerVariantes(conexion, tx, actual.IdRestaurante,
                solicitud.Articulos.Select(a => a.IdVariante).ToArray(), true, ct);
            var subtotal = CrearPedido.ValidarPrecios(solicitud, actuales, actual.CostoEnvio);
            if (actuales.Any(v => !variantes.Any(anterior => anterior.Id == v.Id &&
                    anterior.IdLoyverse == v.IdLoyverse && anterior.ControlaInventario == v.ControlaInventario)))
                throw new CheckoutCambioException("El inventario del catálogo cambió. Revisa tu carrito.");
            long? direccion = null;
            if (solicitud.Direccion is { } d)
            {
                await using var guardarDireccion = new NpgsqlCommand("""
                    INSERT INTO public.direccion(id_cliente,calle,numero,referencias,latitud,longitud)
                    VALUES($1,$2,$3,$4,$5,$6) RETURNING id_direccion
                    """, conexion, tx);
                guardarDireccion.Parameters.AddWithValue(idUsuario);
                guardarDireccion.Parameters.AddWithValue(d.Calle);
                guardarDireccion.Parameters.AddWithValue(d.Numero);
                guardarDireccion.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)d.Referencias ?? DBNull.Value });
                guardarDireccion.Parameters.AddWithValue(d.Latitud!.Value);
                guardarDireccion.Parameters.AddWithValue(d.Longitud!.Value);
                direccion = (long)(await guardarDireccion.ExecuteScalarAsync(ct))!;
            }
            await using var secuencia = new NpgsqlCommand("SELECT nextval(pg_get_serial_sequence('public.pedido','id_pedido'))", conexion, tx);
            var id = (long)(await secuencia.ExecuteScalarAsync(ct))!;
            var folio = "LY-" + id.ToString("D8", CultureInfo.InvariantCulture);
            var costoEnvio = solicitud.TipoEntrega == "Domicilio" ? actual.CostoEnvio : 0;
            await using var insertar = new NpgsqlCommand("""
                INSERT INTO public.pedido(id_pedido,folio,id_cliente,id_direccion,id_metodo_pago,
                    tipo_entrega,subtotal,costo_envio,total,id_operacion,solicitud_hash)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING fecha_creacion
                """, conexion, tx);
            insertar.Parameters.AddWithValue(id);
            insertar.Parameters.AddWithValue(folio);
            insertar.Parameters.AddWithValue(idUsuario);
            insertar.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)direccion ?? DBNull.Value });
            insertar.Parameters.AddWithValue(solicitud.IdMetodoPago);
            insertar.Parameters.AddWithValue(solicitud.TipoEntrega);
            insertar.Parameters.AddWithValue(subtotal);
            insertar.Parameters.AddWithValue(costoEnvio);
            insertar.Parameters.AddWithValue(subtotal + costoEnvio);
            insertar.Parameters.AddWithValue(solicitud.IdOperacion);
            insertar.Parameters.AddWithValue(hash);
            var fecha = (DateTime)(await insertar.ExecuteScalarAsync(ct))!;
            foreach (var linea in solicitud.Articulos)
            {
                var v = actuales.Single(v => v.Id == linea.IdVariante);
                await using var detalle = new NpgsqlCommand("""
                    INSERT INTO public.detalle_pedido(id_pedido,id_variante,nombre_producto,cantidad,precio_unitario,subtotal)
                    VALUES($1,$2,$3,$4,$5,$6)
                    """, conexion, tx);
                detalle.Parameters.AddWithValue(id);
                detalle.Parameters.AddWithValue(v.Id);
                detalle.Parameters.AddWithValue(v.Nombre);
                detalle.Parameters.AddWithValue(linea.Cantidad);
                detalle.Parameters.AddWithValue(v.Precio);
                detalle.Parameters.AddWithValue(v.Precio * linea.Cantidad);
                await detalle.ExecuteNonQueryAsync(ct);
            }
            await using var evento = new NpgsqlCommand("INSERT INTO public.historial_estado(id_pedido,estado_nuevo) VALUES($1,'Recibido') RETURNING id_historial", conexion, tx);
            evento.Parameters.AddWithValue(id);
            var idHistorial = (long)(await evento.ExecuteScalarAsync(ct))!;
            await NotificacionPedidoRepository.EncolarAsync(conexion,tx,id,idHistorial,folio,"Recibido",solicitud.TipoEntrega,subtotal+costoEnvio,ct);
            await tx.CommitAsync(ct);
            return new(id, folio, fecha, "Recibido", subtotal, costoEnvio, subtotal + costoEnvio);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    private static async Task<PedidoCreado?> LeerAnterior(NpgsqlConnection conexion, NpgsqlTransaction? tx,
        long usuario, Guid operacion, byte[] hash, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id_pedido,folio,fecha_creacion,estado,subtotal,costo_envio,total,solicitud_hash
              FROM public.pedido WHERE id_cliente=$1 AND id_operacion=$2
            """, conexion, tx);
        cmd.Parameters.AddWithValue(usuario);
        cmd.Parameters.AddWithValue(operacion);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        if (r.IsDBNull(7) || !CryptographicOperations.FixedTimeEquals(r.GetFieldValue<byte[]>(7), hash))
            throw new CheckoutCambioException("Esta operación ya se utilizó para un pedido diferente.");
        return new(r.GetInt64(0),r.GetString(1),r.GetDateTime(2),r.GetString(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6));
    }

    private static async Task<ConfiguracionPedido> LeerConfiguracion(NpgsqlConnection conexion, NpgsqlTransaction? tx,
        long? idRestaurante, bool bloquear, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id_restaurante,nombre,id_tienda_loyverse,horario_apertura,horario_cierre,costo_envio
              FROM public.restaurante WHERE ($1::bigint IS NULL OR id_restaurante=$1)
              ORDER BY id_restaurante LIMIT 1
            """ + (bloquear ? " FOR SHARE" : ""), conexion, tx);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)idRestaurante ?? DBNull.Value });
        ConfiguracionPedido configuracion;
        var pagos = new List<MetodoPagoPedido>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new RestauranteNoConfiguradoException("No hay un restaurante registrado.");
            configuracion = new(r.GetInt64(0),r.GetString(1),r.IsDBNull(2) ? null : r.GetString(2),
                r.GetFieldValue<TimeOnly>(3),r.GetFieldValue<TimeOnly>(4),r.GetDecimal(5),pagos);
        }
        await using var metodos = new NpgsqlCommand("""
            SELECT id_metodo_pago,nombre FROM public.metodo_pago
             WHERE id_restaurante=$1 AND activo AND btrim(id_loyverse)<>'' ORDER BY id_metodo_pago
            """ + (bloquear ? " FOR SHARE" : ""), conexion, tx);
        metodos.Parameters.AddWithValue(configuracion.IdRestaurante);
        await using (var r = await metodos.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) pagos.Add(new(r.GetInt64(0),r.GetString(1)));
        return configuracion;
    }

    private static async Task<IReadOnlyList<VariantePedido>> LeerVariantes(NpgsqlConnection conexion, NpgsqlTransaction? tx,
        long restaurante, long[] ids, bool bloquear, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT v.id_variante,v.id_loyverse,p.nombre,v.precio,v.controla_inventario
              FROM public.variante_producto v JOIN public.producto p ON p.id_producto=v.id_producto
              JOIN public.categoria c ON c.id_categoria=p.id_categoria
             WHERE c.id_restaurante=$1 AND v.id_variante=ANY($2) AND c.activo AND p.disponible AND v.disponible AND v.precio>0
             ORDER BY v.id_variante
            """ + (bloquear ? " FOR SHARE OF v,p,c" : ""), conexion, tx);
        cmd.Parameters.AddWithValue(restaurante);
        cmd.Parameters.AddWithValue(ids);
        var variantes = new List<VariantePedido>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            variantes.Add(new(r.GetInt64(0),r.IsDBNull(1) ? null : r.GetString(1),r.GetString(2),r.GetDecimal(3),r.GetBoolean(4)));
        return variantes;
    }
}
