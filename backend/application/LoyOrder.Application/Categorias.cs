using LoyOrder.Domain;
namespace LoyOrder.Application;
public interface ICategoriaRepository
{
    Task<IReadOnlyList<Categoria>> ListarAsync(long restaurante, int pagina, int tamano, CancellationToken ct);
    Task<bool> DisponibleAsync(CancellationToken ct);
}
public sealed class SolicitudInvalidaException(string mensaje) : Exception(mensaje);
public sealed class DependenciaNoDisponibleException : Exception;
public sealed class ConsultarCategorias(ICategoriaRepository repository)
{
    public Task<IReadOnlyList<Categoria>> EjecutarAsync(long restaurante, int pagina, int tamano, CancellationToken ct)
    {
        if (restaurante <= 0 || pagina < 1 || pagina > 1000000 || tamano < 1 || tamano > 100)
            throw new SolicitudInvalidaException("idRestaurante debe ser positivo; pagina entre 1 y 1000000; tamano entre 1 y 100.");
        return repository.ListarAsync(restaurante, pagina, tamano, ct);
    }
}
