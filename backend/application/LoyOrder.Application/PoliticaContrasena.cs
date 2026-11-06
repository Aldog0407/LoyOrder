using System.Text;
namespace LoyOrder.Application;
public static class PoliticaContrasena
{
    public const string Mensaje = "La contraseña debe tener entre 8 y 128 caracteres e incluir al menos un símbolo, como !, # o $.";
    public static bool Cumple(string? clave) => clave is { Length: >= 8 and <= 128 }
        && clave.EnumerateRunes().Any(runa => Rune.IsPunctuation(runa) || Rune.IsSymbol(runa));
}
