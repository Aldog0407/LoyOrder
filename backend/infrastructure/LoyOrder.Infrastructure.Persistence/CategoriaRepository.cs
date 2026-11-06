using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;
namespace LoyOrder.Infrastructure.Persistence;
public sealed class CategoriaRepository(NpgsqlDataSource source) : ICategoriaRepository
{
    public async Task<IReadOnlyList<Categoria>> ListarAsync(long restaurante, int pagina, int tamano, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("SELECT id_categoria, id_restaurante, id_loyverse, nombre, activo FROM categoria WHERE id_restaurante = $1 AND activo = TRUE ORDER BY id_categoria LIMIT $2 OFFSET $3");
            cmd.Parameters.AddWithValue(restaurante);
            cmd.Parameters.AddWithValue(tamano);
            cmd.Parameters.AddWithValue((pagina - 1) * tamano);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var result = new List<Categoria>();
            while (await reader.ReadAsync(ct))
                result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
            return result;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }
    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("SELECT id_categoria, id_restaurante, id_loyverse, nombre, activo FROM categoria LIMIT 0");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return true;
        }
        catch (NpgsqlException) { return false; }
        catch (TimeoutException) { return false; }
    }
}
