using LoyOrder.Domain;
namespace LoyOrder.Application;
public interface IUsuarioRepository
{
    Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct);
    Task<Usuario?> PorIdAsync(long id, CancellationToken ct);
    Task<Usuario> RegistrarClienteAsync(string nombre, string correo, string hash, string? telefono, CancellationToken ct,
        NuevoDesafio? desafio = null);
}
public interface IContrasenas
{
    string Proteger(string clave);
    bool Verificar(string hash, string clave);
}
public sealed class CorreoDuplicadoException : Exception;
public sealed class CredencialesInvalidasException : Exception;
public sealed class CuentaInactivaException : Exception;
public sealed class Acceso(IUsuarioRepository usuarios, IContrasenas claves)
{
    public async Task<Usuario> IniciarAsync(string correo, string clave, CancellationToken ct)
    {
        var ficticio = HashFicticio;
        var usuario = await usuarios.PorCorreoAsync(Normalizar(correo), ct);
        // Se verifica también un hash ficticio al no existir cuenta para reducir diferencias de tiempo.
        var valido = claves.Verificar(usuario?.ContrasenaHash ?? ficticio, clave);
        if (usuario is null || !valido) throw new CredencialesInvalidasException();
        if (!usuario.Activo || usuario.Rol is not (Roles.Cliente or Roles.Operador or Roles.Administrador)) throw new CuentaInactivaException();
        return usuario;
    }
    private string HashFicticio => _ficticio ??= claves.Proteger("Cuenta-ficticia-no-utilizable");
    private string? _ficticio;
    public Task<Usuario> RegistrarAsync(string nombre, string correo, string clave, string? telefono, CancellationToken ct,
        NuevoDesafio? desafio = null)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 100)
            throw new SolicitudInvalidaException("Nombre requerido (máximo 100 caracteres).");
        if (!PoliticaContrasena.Cumple(clave))
            throw new SolicitudInvalidaException(PoliticaContrasena.Mensaje);
        return usuarios.RegistrarClienteAsync(nombre.Trim(), Normalizar(correo), claves.Proteger(clave), telefono, ct, desafio);
    }
    public static string Normalizar(string correo) => correo.Trim().ToLowerInvariant();
}
