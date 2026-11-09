using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LoyOrder.Domain;

namespace LoyOrder.Application;

public static class PropositoCorreo
{
    public const string Verificar = "VERIFICAR_CORREO";
    public const string Restablecer = "RESTABLECER_CONTRASENA";
}

public sealed record NuevoDesafio(Guid Id, string Proposito, byte[] SecretoHash, TimeSpan Vigencia);
public sealed record EmisionDesafio(Guid Id, bool EsNuevo);

public interface IDesafioCorreoRepository
{
    Task<Usuario?> UsuarioParaReenvioAsync(Guid anterior, CancellationToken ct);
    Task<EmisionDesafio> EmitirAsync(Usuario usuario, NuevoDesafio desafio, Guid? anterior, bool reutilizar, CancellationToken ct);
    Task<bool> ConsumirAsync(Guid id, string proposito, byte[] secretoHash, Func<string>? nuevaContrasenaHash, CancellationToken ct);
    Task<bool> DisponibleAsync(CancellationToken ct);
}

public abstract record TrabajoCorreo;
public sealed record SolicitudRecuperacion(string Correo) : TrabajoCorreo;
public sealed record MensajeCorreo(string Destino, string Asunto, string Texto) : TrabajoCorreo;
public interface IColaCorreo
{
    bool IntentarEncolar(TrabajoCorreo trabajo);
}

public sealed class DesafioInvalidoException : Exception;
public sealed class LimiteCorreoException : Exception;
public sealed class CorreoNoDisponibleException : Exception;

public sealed class ConfiguracionDesafios(byte[] claveCodigo, Uri urlRestablecimiento)
{
    public Uri UrlRestablecimiento { get; } = urlRestablecimiento;

    public byte[] ProtegerCodigo(Guid id, string codigo) =>
        HMACSHA256.HashData(claveCodigo, Encoding.UTF8.GetBytes($"{id:N}:{codigo}"));
}

public sealed class CorreoAcceso(IDesafioCorreoRepository desafios, IUsuarioRepository usuarios,
    Acceso acceso, IContrasenas claves, ConfiguracionDesafios configuracion, IColaCorreo cola)
{
    public async Task<(Usuario Usuario, Guid IdDesafio)> RegistrarAsync(string nombre, string correo,
        string clave, string? telefono, CancellationToken ct)
    {
        var (desafio, codigo) = CrearVerificacion();
        var usuario = await acceso.RegistrarAsync(nombre, correo, clave, telefono, ct, desafio);
        EnviarCodigo(usuario.Correo, codigo);
        return (usuario, desafio.Id);
    }

    public async Task<Guid> VerificarAsync(Usuario usuario, CancellationToken ct, Guid? anterior = null)
    {
        var (desafio, codigo) = CrearVerificacion();
        var emision = await desafios.EmitirAsync(usuario, desafio, anterior, reutilizar: anterior is null, ct);
        if (emision.EsNuevo) EnviarCodigo(usuario.Correo, codigo);
        return emision.Id;
    }

    private (NuevoDesafio Desafio, string Codigo) CrearVerificacion()
    {
        var id = Guid.NewGuid();
        var codigo = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        return (new(id, PropositoCorreo.Verificar, configuracion.ProtegerCodigo(id, codigo), TimeSpan.FromMinutes(10)), codigo);
    }

    private void EnviarCodigo(string destino, string codigo)
    {
        // Si se llena la cola, el navegador conserva el identificador para reenviar.
        cola.IntentarEncolar(new MensajeCorreo(destino, "Verifica tu correo en LoyOrder",
            $"Tu código de verificación es {codigo}. Vence en 10 minutos."));
    }

    public async Task<Guid> ReenviarAsync(Guid anterior, CancellationToken ct)
    {
        var usuario = await desafios.UsuarioParaReenvioAsync(anterior, ct) ?? throw new DesafioInvalidoException();
        return await VerificarAsync(usuario, ct, anterior);
    }

    public async Task ConfirmarAsync(Guid id, string codigo, CancellationToken ct)
    {
        if (!await desafios.ConsumirAsync(id, PropositoCorreo.Verificar, configuracion.ProtegerCodigo(id, codigo), null, ct))
            throw new DesafioInvalidoException();
    }

    public async Task<MensajeCorreo?> PrepararRecuperacionAsync(string correo, CancellationToken ct)
    {
        var usuario = await usuarios.PorCorreoAsync(Acceso.Normalizar(correo), ct);
        if (usuario is not { Activo: true, CorreoVerificadoEn: not null }) return null;

        var id = Guid.NewGuid();
        var secreto = RandomNumberGenerator.GetBytes(32);
        var desafio = new NuevoDesafio(id, PropositoCorreo.Restablecer, SHA256.HashData(secreto), TimeSpan.FromMinutes(30));
        try { await desafios.EmitirAsync(usuario, desafio, null, reutilizar: false, ct); }
        catch (Exception e) when (e is LimiteCorreoException or DesafioInvalidoException) { return null; }
        var enlace = new UriBuilder(configuracion.UrlRestablecimiento) { Query = $"token={id:N}.{Convert.ToHexString(secreto)}" };
        return new MensajeCorreo(usuario.Correo, "Restablece tu contraseña de LoyOrder",
            $"Abre este enlace para cambiar tu contraseña. Vence en 30 minutos.\n\n{enlace.Uri}\n\nSi no solicitaste el cambio, ignora este mensaje.");
    }

    public async Task RestablecerAsync(string token, string nuevaContrasena, CancellationToken ct)
    {
        if (token.Length != 97 || token[32] != '.' || !Guid.TryParseExact(token[..32], "N", out var id))
            throw new DesafioInvalidoException();
        byte[] secreto;
        try { secreto = Convert.FromHexString(token[33..]); }
        catch (FormatException) { throw new DesafioInvalidoException(); }
        if (!PoliticaContrasena.Cumple(nuevaContrasena))
            throw new SolicitudInvalidaException(PoliticaContrasena.Mensaje);
        if (!await desafios.ConsumirAsync(id, PropositoCorreo.Restablecer, SHA256.HashData(secreto),
            () => claves.Proteger(nuevaContrasena), ct))
            throw new DesafioInvalidoException();
    }
}
