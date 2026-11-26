using LoyOrder.Application;
using Npgsql;
using NpgsqlTypes;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class EdicionMenuRepository(NpgsqlDataSource source) : IEdicionMenuRepository
{
    private const string AdministradorDelRestaurante = """
        AND EXISTS (
            SELECT 1 FROM public.empleado e JOIN public.usuario u ON u.id_usuario = e.id_empleado
            WHERE e.id_empleado = $1 AND e.id_restaurante = c.id_restaurante
              AND e.rol = 'ADMINISTRADOR' AND u.activo
        )
        """;

    public async Task<IReadOnlyList<ProductoEditable>> ListarAsync(long administrador, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                SELECT p.id_producto, c.nombre, p.nombre, p.descripcion, p.imagen_url,
                       p.nombre_menu, p.descripcion_menu, p.imagen_url_menu
                FROM public.producto p JOIN public.categoria c USING (id_categoria)
                WHERE c.activo AND p.disponible
                """ + " " + AdministradorDelRestaurante + " ORDER BY c.nombre, p.nombre, p.id_producto");
            cmd.Parameters.AddWithValue(administrador);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var productos = new List<ProductoEditable>();
            while (await reader.ReadAsync(ct))
                productos.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    Texto(reader, 3), Texto(reader, 4), new(Texto(reader, 5), Texto(reader, 6), Texto(reader, 7))));
            return productos;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<bool> GuardarAsync(long administrador, long producto, DatosMenuLocal datos, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                UPDATE public.producto AS p
                SET nombre_menu = $3, descripcion_menu = $4, imagen_url_menu = $5
                FROM public.categoria AS c
                WHERE p.id_producto = $2 AND p.id_categoria = c.id_categoria
                  AND c.activo AND p.disponible
                """ + " " + AdministradorDelRestaurante);
            cmd.Parameters.AddWithValue(administrador);
            cmd.Parameters.AddWithValue(producto);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Varchar, (object?)datos.Nombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Varchar, (object?)datos.Descripcion ?? DBNull.Value);
            cmd.Parameters.AddWithValue(NpgsqlDbType.Varchar, (object?)datos.ImagenUrl ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    private static string? Texto(NpgsqlDataReader reader, int columna) =>
        reader.IsDBNull(columna) ? null : reader.GetString(columna);
}
