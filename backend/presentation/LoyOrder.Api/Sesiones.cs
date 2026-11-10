using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using LoyOrder.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace LoyOrder.Api;

public sealed record PlazosSesion(int AccesoMinutos, int InactividadMinutos, int MaximoMinutos);

public sealed record EstadoSesion(Guid Id, long IdUsuario, string Rol, int VersionSesion,
    DateTimeOffset VenceSesion, DateTimeOffset VenceInactividad);

public sealed record CredencialSesion(EstadoSesion Estado, string RefreshToken);

public sealed class Sesiones(IMemoryCache cache, TimeProvider reloj, PlazosSesion plazos)
{
    public CredencialSesion Crear(Usuario usuario)
    {
        var ahora = reloj.GetUtcNow();
        var estado = new EstadoSesion(Guid.NewGuid(), usuario.IdUsuario, usuario.Rol, usuario.VersionSesion,
            ahora.AddMinutes(plazos.MaximoMinutos), ahora.AddMinutes(plazos.InactividadMinutos));
        return Rotar(new Registro(estado), estado);
    }

    public EstadoSesion? Consultar(Guid id)
    {
        if (!cache.TryGetValue(id, out Registro? sesion) || sesion is null) return null;
        lock (sesion)
            return Vigente(sesion, reloj.GetUtcNow()) ? sesion.Estado : null;
    }

    public EstadoSesion? ConsultarRenovacion(string token)
    {
        if (!BuscarRenovacion(token, out var sesion, out var hash)) return null;
        lock (sesion)
            return Vigente(sesion, reloj.GetUtcNow()) && CryptographicOperations.FixedTimeEquals(sesion.Hash, hash)
                ? sesion.Estado : null;
    }

    public CredencialSesion? Renovar(string token)
    {
        if (!BuscarRenovacion(token, out var sesion, out var hash)) return null;
        lock (sesion)
        {
            var ahora = reloj.GetUtcNow();
            if (!Vigente(sesion, ahora) || !CryptographicOperations.FixedTimeEquals(sesion.Hash, hash))
                return null;
            var inactividad = ahora.AddMinutes(plazos.InactividadMinutos);
            var estado = sesion.Estado with
            {
                VenceInactividad = inactividad < sesion.Estado.VenceSesion ? inactividad : sesion.Estado.VenceSesion
            };
            return Rotar(sesion, estado);
        }
    }

    public void Cerrar(Guid id)
    {
        if (!cache.TryGetValue(id, out Registro? sesion) || sesion is null) return;
        lock (sesion)
        {
            sesion.Cerrada = true;
            cache.Remove(id);
        }
    }

    public bool CerrarRenovacion(string token)
    {
        if (!BuscarRenovacion(token, out var sesion, out var hash)) return false;
        lock (sesion)
        {
            if (!Vigente(sesion, reloj.GetUtcNow()) || !CryptographicOperations.FixedTimeEquals(sesion.Hash, hash))
                return false;
            sesion.Cerrada = true;
            cache.Remove(sesion.Estado.Id);
            return true;
        }
    }

    private CredencialSesion Rotar(Registro sesion, EstadoSesion estado)
    {
        var secreto = RandomNumberGenerator.GetBytes(32);
        sesion.Estado = estado;
        sesion.Hash = SHA256.HashData(secreto);
        cache.Set(estado.Id, sesion, estado.VenceInactividad);
        return new(estado, $"{estado.Id:N}.{Convert.ToHexString(secreto)}");
    }

    private bool BuscarRenovacion(string token, out Registro sesion, out byte[] hash)
    {
        sesion = null!;
        hash = [];
        if (token.Length != 97 || token[32] != '.' || !Guid.TryParseExact(token[..32], "N", out var id) ||
            !cache.TryGetValue(id, out Registro? encontrado) || encontrado is null)
            return false;
        try { hash = SHA256.HashData(Convert.FromHexString(token[33..])); }
        catch (FormatException) { return false; }
        sesion = encontrado;
        return true;
    }

    private static bool Vigente(Registro sesion, DateTimeOffset ahora) =>
        !sesion.Cerrada && ahora < sesion.Estado.VenceSesion && ahora < sesion.Estado.VenceInactividad;

    // El secreto de renovación queda como hash y la sesión vive en memoria de esta instancia.
    private sealed class Registro(EstadoSesion estado)
    {
        public EstadoSesion Estado = estado;
        public byte[] Hash = [];
        public bool Cerrada;
    }
}

public sealed class Tokens(SymmetricSecurityKey key, Sesiones sesiones, TimeProvider reloj, PlazosSesion plazos)
{
    public object Emitir(Usuario usuario, CredencialSesion? credencial = null)
    {
        credencial ??= sesiones.Crear(usuario);
        var estado = credencial.Estado;
        var vence = reloj.GetUtcNow().AddMinutes(plazos.AccesoMinutos);
        if (estado.VenceInactividad < vence) vence = estado.VenceInactividad;
        var jwt = new JwtSecurityToken("LoyOrder", "LoyOrder.Web",
            [new("sub", usuario.IdUsuario.ToString(CultureInfo.InvariantCulture)), new("sid", estado.Id.ToString("N")),
                new("jti", Guid.NewGuid().ToString("N")), new("role", usuario.Rol),
                new("sv", usuario.VersionSesion.ToString(CultureInfo.InvariantCulture))],
            expires: vence.UtcDateTime, signingCredentials: new(key, SecurityAlgorithms.HmacSha256));
        return new
        {
            accessToken = new JwtSecurityTokenHandler().WriteToken(jwt),
            tokenType = "Bearer",
            expiresAt = vence,
            refreshToken = credencial.RefreshToken,
            idleExpiresAt = estado.VenceInactividad,
            sessionExpiresAt = estado.VenceSesion,
            usuario = new { usuario.IdUsuario, usuario.Nombre, usuario.Correo, usuario.Rol }
        };
    }

    internal static bool CuentaValida(Usuario? usuario, EstadoSesion estado, bool exigeCorreo) =>
        usuario is { Activo: true } && usuario.IdUsuario == estado.IdUsuario &&
        usuario.Rol == estado.Rol && usuario.VersionSesion == estado.VersionSesion &&
        (!exigeCorreo || usuario.CorreoVerificadoEn is not null);
}
