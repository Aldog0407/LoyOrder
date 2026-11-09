namespace LoyOrder.Domain;
public static class Roles
{
    public const string Cliente = "CLIENTE", Operador = "OPERADOR", Administrador = "ADMINISTRADOR";
}
public sealed record Usuario(long IdUsuario, string Nombre, string Correo, string ContrasenaHash, bool Activo, string Rol,
    DateTimeOffset? CorreoVerificadoEn = null, int VersionSesion = 0);
