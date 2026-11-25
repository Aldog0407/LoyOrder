using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;
namespace LoyOrder.Infrastructure.Persistence;

/// <summary>
/// Catálogo sincronizado desde Loyverse (7.2.3, CU-10). Inserta o actualiza por id_loyverse y nunca borra filas:
/// lo que Loyverse elimina queda como no disponible o inactivo, porque los pedidos anteriores lo siguen referenciando.
/// </summary>
public sealed class CatalogoRepository(NpgsqlDataSource source) : ICatalogoRepository
{
    public Task<RestauranteSincronizable?> RestauranteAsync(long? idRestaurante, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "SELECT id_restaurante, id_tienda_loyverse, intervalo_sincronizacion FROM restaurante " +
            "WHERE ($1::bigint IS NULL OR id_restaurante = $1) ORDER BY id_restaurante LIMIT 1");
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)idRestaurante ?? DBNull.Value });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new RestauranteSincronizable(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt32(2)) : null;
    });

    public Task VincularTiendaAsync(long idRestaurante, string idTienda, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand("UPDATE restaurante SET id_tienda_loyverse = $2 WHERE id_restaurante = $1 AND id_tienda_loyverse IS NULL");
        cmd.Parameters.AddWithValue(idRestaurante);
        cmd.Parameters.AddWithValue(idTienda);
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public Task<DateTimeOffset?> UltimaCorrectaAsync(long idRestaurante, string tipo, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "SELECT max(fecha_inicio) FROM sincronizacion WHERE id_restaurante = $1 AND tipo = $2 AND estado = $3");
        cmd.Parameters.AddWithValue(idRestaurante);
        cmd.Parameters.AddWithValue(tipo);
        cmd.Parameters.AddWithValue(TiposSincronizacion.Correcta);
        var valor = await cmd.ExecuteScalarAsync(ct);
        return valor is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : (DateTimeOffset?)null;
    });

    public Task<long> IniciarRegistroAsync(long idRestaurante, string tipo, DateTimeOffset inicio, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "INSERT INTO sincronizacion (id_restaurante, tipo, fecha_inicio, estado) VALUES ($1, $2, $3, $4) RETURNING id_sincronizacion");
        cmd.Parameters.AddWithValue(idRestaurante);
        cmd.Parameters.AddWithValue(tipo);
        cmd.Parameters.AddWithValue(inicio.UtcDateTime);
        cmd.Parameters.AddWithValue(TiposSincronizacion.EnProceso);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task TerminarRegistroAsync(long idSincronizacion, string estado, DateTimeOffset fin, string? error, string? resumenJson, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "UPDATE sincronizacion SET estado = $2, fecha_fin = $3, detalle_error = $4, resumen = $5 WHERE id_sincronizacion = $1");
        cmd.Parameters.AddWithValue(idSincronizacion);
        cmd.Parameters.AddWithValue(estado);
        cmd.Parameters.AddWithValue(fin.UtcDateTime);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)error ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = (object?)resumenJson ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public Task<ResultadoCatalogo> GuardarCatalogoAsync(long idRestaurante, IReadOnlyList<CategoriaGuardar> categorias,
        IReadOnlyList<ProductoGuardar> productos, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var con = await source.OpenConnectionAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);
        int catNuevas = 0, catAct = 0, prodNuevos = 0, prodAct = 0, prodElim = 0, varNuevas = 0, varAct = 0;

        var idsCategoria = new Dictionary<string, long>();
        foreach (var c in categorias)
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO categoria (id_restaurante, id_loyverse, nombre, activo) VALUES ($1, $2, $3, $4) " +
                "ON CONFLICT (id_loyverse) DO UPDATE SET nombre = EXCLUDED.nombre, activo = EXCLUDED.activo, id_restaurante = EXCLUDED.id_restaurante " +
                "RETURNING id_categoria, (xmax = 0)", con, tx);
            cmd.Parameters.AddWithValue(idRestaurante);
            cmd.Parameters.AddWithValue(c.IdLoyverse);
            cmd.Parameters.AddWithValue(c.Nombre);
            cmd.Parameters.AddWithValue(c.Activo);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            idsCategoria[c.IdLoyverse] = r.GetInt64(0);
            if (r.GetBoolean(1)) catNuevas++; else catAct++;
        }

        foreach (var p in productos)
        {
            if (p.Eliminado)
            {
                // Eliminado en Loyverse: deja de mostrarse (CU-10 paso 4). Si nunca se sincronizó, no hay nada que hacer.
                await using var baja = new NpgsqlCommand(
                    "WITH p AS (UPDATE producto SET disponible = FALSE WHERE id_loyverse = $1 RETURNING id_producto) " +
                    "UPDATE variante_producto v SET disponible = FALSE FROM p WHERE v.id_producto = p.id_producto " +
                    "RETURNING 1", con, tx);
                baja.Parameters.AddWithValue(p.IdLoyverse);
                await baja.ExecuteNonQueryAsync(ct);
                await using var existe = new NpgsqlCommand("SELECT 1 FROM producto WHERE id_loyverse = $1", con, tx);
                existe.Parameters.AddWithValue(p.IdLoyverse);
                if (await existe.ExecuteScalarAsync(ct) is not null) prodElim++;
                continue;
            }
            if (!idsCategoria.TryGetValue(p.IdCategoriaLoyverse, out var idCategoria))
                throw new InvalidOperationException("Producto con una categoría que no viene en la sincronización.");

            long idProducto;
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO producto (id_categoria, id_loyverse, nombre, descripcion, imagen_url, disponible) VALUES ($1, $2, $3, $4, $5, $6) " +
                "ON CONFLICT (id_loyverse) DO UPDATE SET id_categoria = EXCLUDED.id_categoria, nombre = EXCLUDED.nombre, " +
                "descripcion = EXCLUDED.descripcion, imagen_url = EXCLUDED.imagen_url, disponible = EXCLUDED.disponible " +
                "RETURNING id_producto, (xmax = 0)", con, tx))
            {
                cmd.Parameters.AddWithValue(idCategoria);
                cmd.Parameters.AddWithValue(p.IdLoyverse);
                cmd.Parameters.AddWithValue(p.Nombre);
                cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)p.Descripcion ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)p.ImagenUrl ?? DBNull.Value });
                cmd.Parameters.AddWithValue(p.Disponible);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                idProducto = r.GetInt64(0);
                if (r.GetBoolean(1)) prodNuevos++; else prodAct++;
            }

            foreach (var v in p.Variantes)
            {
                await using var cmd = new NpgsqlCommand(
                    "INSERT INTO variante_producto (id_producto, id_loyverse, nombre, precio, controla_inventario, disponible) VALUES ($1, $2, $3, $4, $5, $6) " +
                    "ON CONFLICT (id_loyverse) DO UPDATE SET id_producto = EXCLUDED.id_producto, nombre = EXCLUDED.nombre, precio = EXCLUDED.precio, " +
                    "controla_inventario = EXCLUDED.controla_inventario, disponible = EXCLUDED.disponible " +
                    "RETURNING (xmax = 0)", con, tx);
                cmd.Parameters.AddWithValue(idProducto);
                cmd.Parameters.AddWithValue(v.IdLoyverse);
                cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = (object?)v.Nombre ?? DBNull.Value });
                cmd.Parameters.AddWithValue(v.Precio);
                cmd.Parameters.AddWithValue(v.ControlaInventario);
                cmd.Parameters.AddWithValue(v.Disponible);
                if ((bool)(await cmd.ExecuteScalarAsync(ct))!) varNuevas++; else varAct++;
            }

            // Variantes que ya no vienen en el artículo: se quitaron en Loyverse.
            await using var quitar = new NpgsqlCommand(
                "UPDATE variante_producto SET disponible = FALSE WHERE id_producto = $1 AND id_loyverse IS NOT NULL AND NOT (id_loyverse = ANY($2))", con, tx);
            quitar.Parameters.AddWithValue(idProducto);
            quitar.Parameters.AddWithValue(p.Variantes.Select(v => v.IdLoyverse).ToArray());
            await quitar.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new ResultadoCatalogo(catNuevas, catAct, prodNuevos, prodAct, prodElim, varNuevas, varAct);
    });

    public Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaOrigen> existencias, CancellationToken ct) => Ejecutar(async () =>
    {
        if (existencias.Count == 0) return 0;
        await using var cmd = source.CreateCommand(
            "UPDATE variante_producto v SET existencias = e.cantidad " +
            "FROM unnest($1::text[], $2::numeric[]) AS e(id, cantidad) " +
            "WHERE v.id_loyverse = e.id AND v.existencias IS DISTINCT FROM e.cantidad");
        cmd.Parameters.AddWithValue(existencias.Select(e => e.VarianteId).ToArray());
        cmd.Parameters.AddWithValue(existencias.Select(e => e.Existencias).ToArray());
        return await cmd.ExecuteNonQueryAsync(ct);
    });

    public Task<int> CategoriasSinEstacionAsync(long idRestaurante, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "SELECT count(*) FROM categoria c WHERE c.id_restaurante = $1 AND c.activo AND c.id_loyverse IS NOT NULL " +
            "AND NOT EXISTS (SELECT 1 FROM asignacion_estacion a WHERE a.id_categoria = c.id_categoria)");
        cmd.Parameters.AddWithValue(idRestaurante);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    });

    public Task<IReadOnlyList<RegistroSincronizacion>> HistorialAsync(long idRestaurante, int cantidad, CancellationToken ct) => Ejecutar(async () =>
    {
        await using var cmd = source.CreateCommand(
            "SELECT id_sincronizacion, tipo, fecha_inicio, fecha_fin, estado, detalle_error, resumen::text FROM sincronizacion " +
            "WHERE id_restaurante = $1 ORDER BY fecha_inicio DESC, id_sincronizacion DESC LIMIT $2");
        cmd.Parameters.AddWithValue(idRestaurante);
        cmd.Parameters.AddWithValue(cantidad);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<RegistroSincronizacion>();
        while (await r.ReadAsync(ct))
            lista.Add(new(r.GetInt64(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2),
                r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return (IReadOnlyList<RegistroSincronizacion>)lista;
    });

    private static async Task<T> Ejecutar<T>(Func<Task<T>> accion)
    {
        try { return await accion(); }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
