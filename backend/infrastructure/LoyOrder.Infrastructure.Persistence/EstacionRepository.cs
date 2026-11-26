using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class EstacionRepository(NpgsqlDataSource source) : IEstacionRepository
{
    private static NpgsqlCommand Comando(NpgsqlConnection con, NpgsqlTransaction tx, string sql, params object[] valores)
    {
        var cmd = new NpgsqlCommand(sql, con, tx);
        foreach (var valor in valores) cmd.Parameters.AddWithValue(valor);
        return cmd;
    }

    private async Task<T> Ejecutar<T>(long admin, bool escritura, Func<NpgsqlConnection, NpgsqlTransaction, long, Task<T>> accion, CancellationToken ct)
    {
        try
        {
            await using var con = await source.OpenConnectionAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);
            await using var buscar = Comando(con, tx, """
                SELECT e.id_restaurante FROM public.empleado e JOIN public.usuario u ON u.id_usuario=e.id_empleado
                WHERE e.id_empleado=$1 AND e.rol='ADMINISTRADOR' AND u.activo
                """, admin);
            if (await buscar.ExecuteScalarAsync(ct) is not long restaurante) throw new AdministradorEstacionException();
            if (escritura)
            {
                await using var bloqueo = Comando(con, tx, "SELECT pg_advisory_xact_lock(hashtext('loyorder.estaciones'), hashtext($1::text))", restaurante);
                await bloqueo.ExecuteNonQueryAsync(ct);
            }
            var resultado = await accion(con, tx, restaurante);
            await tx.CommitAsync(ct);
            return resultado;
        }
        catch (PostgresException e) when (e.SqlState is "23505" or "23503" or "23514")
        { throw new ConflictoEstacionException("La configuración entra en conflicto con una asignación existente. Actualiza la lista."); }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    private static async Task<List<T>> Listar<T>(NpgsqlConnection con, NpgsqlTransaction tx, string sql, long restaurante, Func<NpgsqlDataReader,T> mapear, CancellationToken ct)
    {
        await using var cmd = Comando(con, tx, sql, restaurante);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<T>();
        while (await r.ReadAsync(ct)) lista.Add(mapear(r));
        return lista;
    }

    public Task<ConfiguracionEstaciones> ConsultarAsync(long administrador, CancellationToken ct) => Ejecutar(administrador, false, async (con, tx, restaurante) =>
    {
        var impresoras = await Listar(con, tx, "SELECT id_impresora,nombre,direccion_ip,activa FROM public.impresora WHERE id_restaurante=$1 AND tipo='Cocina' ORDER BY nombre,id_impresora", restaurante,
            r => new ImpresoraCocina(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetBoolean(3)), ct);
        var estaciones = await Listar(con, tx, "SELECT id_estacion,nombre,id_impresora,activa FROM public.estacion_preparacion WHERE id_restaurante=$1 ORDER BY nombre,id_estacion", restaurante,
            r => new EstacionPreparacion(r.GetInt64(0),r.GetString(1),r.GetInt64(2),r.GetBoolean(3)), ct);
        var categorias = await Listar(con, tx, "SELECT id_categoria,nombre,activo FROM public.categoria WHERE id_restaurante=$1 ORDER BY nombre,id_categoria", restaurante,
            r => new CategoriaEstacion(r.GetInt64(0),r.GetString(1),r.GetBoolean(2)), ct);
        var variantes = await Listar(con, tx, """
            SELECT v.id_variante,c.id_categoria,p.nombre,v.nombre,(c.activo AND p.disponible AND v.disponible)
            FROM public.variante_producto v JOIN public.producto p USING(id_producto) JOIN public.categoria c USING(id_categoria)
            WHERE c.id_restaurante=$1 ORDER BY p.nombre,v.nombre,v.id_variante
            """, restaurante, r => new VarianteEstacion(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetBoolean(4)), ct);
        var asignaciones = await Listar(con, tx, """
            SELECT a.id_asignacion,a.id_estacion,a.id_categoria,a.id_variante FROM public.asignacion_estacion a
            JOIN public.estacion_preparacion e USING(id_estacion) WHERE e.id_restaurante=$1 ORDER BY a.id_asignacion
            """, restaurante, r => new AsignacionEstacion(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3)), ct);
        return new ConfiguracionEstaciones(impresoras,estaciones,categorias,variantes,asignaciones);
    }, ct);

    public Task<long> GuardarImpresoraAsync(long administrador, long? id, GuardarImpresora datos, CancellationToken ct) => Ejecutar(administrador,true,async (con,tx,restaurante) =>
    {
        await using var duplicada = Comando(con,tx,"SELECT 1 FROM public.impresora WHERE id_restaurante=$1 AND (lower(btrim(nombre))=lower($2) OR direccion_ip=$3) AND id_impresora<>$4",restaurante,datos.Nombre,datos.DireccionIp,id ?? 0L);
        if (await duplicada.ExecuteScalarAsync(ct) is not null) throw new ConflictoEstacionException("Ya existe una impresora con ese nombre o dirección IP.");
        if (id.HasValue && !datos.Activa)
        {
            await using var comprobar = Comando(con,tx,"SELECT 1 FROM public.estacion_preparacion WHERE id_restaurante=$1 AND id_impresora=$2 AND activa",restaurante,id.Value);
            if (await comprobar.ExecuteScalarAsync(ct) is not null) throw new ConflictoEstacionException("Desactiva primero la estación asociada.");
        }
        await using var cmd = id.HasValue
            ? Comando(con,tx,"UPDATE public.impresora SET nombre=$2,direccion_ip=$3,activa=$4 WHERE id_restaurante=$1 AND id_impresora=$5 AND tipo='Cocina' RETURNING id_impresora",restaurante,datos.Nombre,datos.DireccionIp,datos.Activa,id.Value)
            : Comando(con,tx,"INSERT INTO public.impresora(id_restaurante,nombre,direccion_ip,tipo,activa) VALUES($1,$2,$3,'Cocina',$4) RETURNING id_impresora",restaurante,datos.Nombre,datos.DireccionIp,datos.Activa);
        return await cmd.ExecuteScalarAsync(ct) is long resultado ? resultado : throw new EstacionNoEncontradaException();
    },ct);

    public Task<long> GuardarEstacionAsync(long administrador,long? id,GuardarEstacion datos,CancellationToken ct) => Ejecutar(administrador,true,async (con,tx,restaurante) =>
    {
        await using var duplicada = Comando(con,tx,"SELECT 1 FROM public.estacion_preparacion WHERE id_restaurante=$1 AND (lower(btrim(nombre))=lower($2) OR id_impresora=$3) AND id_estacion<>$4",restaurante,datos.Nombre,datos.IdImpresora,id ?? 0L);
        if (await duplicada.ExecuteScalarAsync(ct) is not null) throw new ConflictoEstacionException("Ya existe una estación con ese nombre o impresora.");
        await using var impresora = Comando(con,tx,"SELECT activa FROM public.impresora WHERE id_restaurante=$1 AND id_impresora=$2 AND tipo='Cocina'",restaurante,datos.IdImpresora);
        if (await impresora.ExecuteScalarAsync(ct) is not bool activa) throw new SolicitudInvalidaException("La impresora de cocina no pertenece al restaurante o no existe.");
        if (datos.Activa && !activa) throw new ConflictoEstacionException("Activa primero la impresora de cocina.");
        await using var cmd = id.HasValue
            ? Comando(con,tx,"UPDATE public.estacion_preparacion SET nombre=$2,id_impresora=$3,activa=$4 WHERE id_restaurante=$1 AND id_estacion=$5 RETURNING id_estacion",restaurante,datos.Nombre,datos.IdImpresora,datos.Activa,id.Value)
            : Comando(con,tx,"INSERT INTO public.estacion_preparacion(id_restaurante,nombre,id_impresora,activa) VALUES($1,$2,$3,$4) RETURNING id_estacion",restaurante,datos.Nombre,datos.IdImpresora,datos.Activa);
        return await cmd.ExecuteScalarAsync(ct) is long resultado ? resultado : throw new EstacionNoEncontradaException();
    },ct);

    public Task<long> AsignarAsync(long administrador,long estacion,GuardarAsignacion datos,CancellationToken ct) => Ejecutar(administrador,true,async (con,tx,restaurante) =>
    {
        await using var buscar = Comando(con,tx,"SELECT 1 FROM public.estacion_preparacion WHERE id_estacion=$1 AND id_restaurante=$2",estacion,restaurante);
        if (await buscar.ExecuteScalarAsync(ct) is null) throw new EstacionNoEncontradaException();
        await using var catalogo = datos.IdCategoria.HasValue
            ? Comando(con,tx,"SELECT 1 FROM public.categoria WHERE id_categoria=$1 AND id_restaurante=$2 AND activo",datos.IdCategoria.Value,restaurante)
            : Comando(con,tx,"SELECT 1 FROM public.variante_producto v JOIN public.producto p USING(id_producto) JOIN public.categoria c USING(id_categoria) WHERE v.id_variante=$1 AND c.id_restaurante=$2 AND c.activo AND p.disponible AND v.disponible",datos.IdVariante!.Value,restaurante);
        if (await catalogo.ExecuteScalarAsync(ct) is null) throw new SolicitudInvalidaException("El elemento del catálogo no está disponible en este restaurante.");
        await using var previa = datos.IdCategoria.HasValue
            ? Comando(con,tx,"SELECT a.id_asignacion FROM public.asignacion_estacion a JOIN public.estacion_preparacion e USING(id_estacion) WHERE e.id_restaurante=$1 AND a.id_categoria=$2",restaurante,datos.IdCategoria.Value)
            : Comando(con,tx,"SELECT a.id_asignacion FROM public.asignacion_estacion a JOIN public.estacion_preparacion e USING(id_estacion) WHERE e.id_restaurante=$1 AND a.id_variante=$2",restaurante,datos.IdVariante!.Value);
        if (await previa.ExecuteScalarAsync(ct) is long existente)
        {
            if (!datos.Reasignar) throw new ConflictoEstacionException("El elemento ya tiene una estación. Confirma la reasignación.");
            await using var mover = Comando(con,tx,"UPDATE public.asignacion_estacion SET id_estacion=$1 WHERE id_asignacion=$2 RETURNING id_asignacion",estacion,existente);
            return (long)(await mover.ExecuteScalarAsync(ct))!;
        }
        await using var cmd = Comando(con,tx,"INSERT INTO public.asignacion_estacion(id_estacion,id_categoria,id_variante) VALUES($1,$2,$3) RETURNING id_asignacion",estacion);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Bigint, Value=(object?)datos.IdCategoria ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlDbType.Bigint, Value=(object?)datos.IdVariante ?? DBNull.Value });
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    },ct);

    public async Task RetirarAsync(long administrador,long estacion,long asignacion,CancellationToken ct) => await Ejecutar(administrador,true,async (con,tx,restaurante) =>
    {
        await using var cmd = Comando(con,tx,"DELETE FROM public.asignacion_estacion a USING public.estacion_preparacion e WHERE a.id_estacion=e.id_estacion AND e.id_restaurante=$1 AND e.id_estacion=$2 AND a.id_asignacion=$3",restaurante,estacion,asignacion);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw new EstacionNoEncontradaException();
        return true;
    },ct);
}
