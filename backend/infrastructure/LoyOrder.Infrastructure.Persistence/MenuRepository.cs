using LoyOrder.Application;
using Npgsql;
namespace LoyOrder.Infrastructure.Persistence;

/// <summary>Catálogo vendible para el cliente (7.2.4). Solo lee: las tablas las llena la sincronización (7.2.3).</summary>
public sealed class MenuRepository(NpgsqlDataSource source) : IMenuRepository
{
    // "Sin categoría" la crea la sincronización con id_loyverse 'loyorder-sin-categoria-{id}' y se muestra al final.
    private const string Consulta = """
        SELECT c.id_categoria, c.nombre, p.id_producto,
               COALESCE(p.nombre_menu, p.nombre), COALESCE(p.descripcion_menu, p.descripcion),
               COALESCE(p.imagen_url_menu, p.imagen_url),
               v.id_variante, v.nombre, v.precio, v.controla_inventario, v.existencias
        FROM categoria c
        JOIN producto p ON p.id_categoria = c.id_categoria
        JOIN variante_producto v ON v.id_producto = p.id_producto
        WHERE c.id_restaurante = $1 AND c.activo AND p.disponible AND v.disponible AND v.precio > 0
        ORDER BY (c.id_loyverse LIKE 'loyorder-sin-categoria-%') NULLS FIRST, lower(c.nombre), c.id_categoria,
                 lower(COALESCE(p.nombre_menu, p.nombre)), p.id_producto, v.id_variante
        """;

    public async Task<IReadOnlyList<FilaMenu>> ListarAsync(long idRestaurante, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand(Consulta);
            cmd.Parameters.AddWithValue(idRestaurante);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var filas = new List<FilaMenu>();
            while (await r.ReadAsync(ct))
                filas.Add(new FilaMenu(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetDecimal(8), r.GetBoolean(9),
                    r.IsDBNull(10) ? null : r.GetDecimal(10)));
            return filas;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
}
