using System.Globalization;
using System.Text;
namespace LoyOrder.Application;

// Documento para impresoras térmicas de 80 mm (diseño 5.4): 48 columnas en tamaño normal y 24 en doble tamaño.
// La capa de aplicación arma las líneas; el módulo de impresión (Infrastructure.Impresion) las convierte en ESC/POS.
// Lo usan el ticket de venta (7.2.11, CU-19) y las comandas por estación (7.2.20, CU-18).

public enum Alineacion { Izquierda, Centro, Derecha }

/// <param name="Texto">Sin saltos de línea. Ya viene partido al ancho que le corresponde.</param>
/// <param name="Doble">Doble alto y doble ancho (24 columnas).</param>
/// <param name="Invertido">Blanco sobre negro; el texto se rellena a todo el ancho para que la banda quede completa.</param>
public sealed record LineaImpresion(string Texto, Alineacion Alineacion = Alineacion.Izquierda, bool Negrita = false,
    bool Doble = false, bool Invertido = false);

public sealed record DocumentoImpresion(IReadOnlyList<LineaImpresion> Lineas)
{
    /// <summary>Vista previa en texto plano de 48 columnas, con la alineación ya aplicada.</summary>
    public string ComoTexto()
    {
        var texto = new StringBuilder();
        // En texto plano no hay doble tamaño: todo se alinea dentro de 48 columnas.
        foreach (var l in Lineas)
        {
            var linea = l.Alineacion switch
            {
                Alineacion.Centro => Papel.Centrar(l.Texto.Trim(), Papel.Ancho),
                Alineacion.Derecha => l.Texto.Trim().PadLeft(Papel.Ancho),
                _ => l.Texto,
            };
            texto.Append(linea.TrimEnd()).Append('\n');
        }
        return texto.ToString();
    }
}

/// <summary>Utilidades de texto para papel de 80 mm.</summary>
public static class Papel
{
    public const int Ancho = 48;
    public const int AnchoDoble = 24;
    private static readonly CultureInfo Mexico = CultureInfo.GetCultureInfo("es-MX");

    public static string Dinero(decimal cantidad) => "$" + cantidad.ToString("#,##0.00", Mexico);

    public static string Linea(char c) => new(c, Ancho);

    public static string Centrar(string texto, int ancho)
    {
        if (texto.Length >= ancho) return texto;
        var izquierda = (ancho - texto.Length) / 2;
        return new string(' ', izquierda) + texto;
    }

    /// <summary>Rellena a todo el ancho con el texto centrado (para bandas en blanco sobre negro).</summary>
    public static string Banda(string texto, int ancho = Ancho) => Centrar(texto, ancho).PadRight(ancho);

    /// <summary>Texto a la izquierda y a la derecha en la misma línea. Si no caben, la izquierda se parte en varias líneas.</summary>
    public static IEnumerable<string> DosColumnas(string izquierda, string derecha, int ancho = Ancho, int sangria = 4)
    {
        var disponible = ancho - derecha.Length - 1;
        var partes = Partir(izquierda, disponible, sangria).ToList();
        yield return partes[0].PadRight(ancho - derecha.Length) + derecha;
        foreach (var resto in partes.Skip(1)) yield return resto;
    }

    /// <summary>Parte por palabras sin pasar del ancho; las líneas siguientes llevan sangría. Las palabras muy largas se cortan.</summary>
    public static IEnumerable<string> Partir(string texto, int ancho, int sangria = 0)
    {
        var prefijo = new string(' ', sangria);
        var actual = new StringBuilder();
        var primera = true;
        foreach (var palabraOriginal in Limpiar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var palabra = palabraOriginal;
            while (true)
            {
                var limite = primera ? ancho : ancho - sangria;
                var separador = actual.Length == 0 ? "" : " ";
                if (actual.Length + separador.Length + palabra.Length <= limite)
                {
                    actual.Append(separador).Append(palabra);
                    break;
                }
                if (actual.Length > 0)
                {
                    yield return (primera ? "" : prefijo) + actual;
                    actual.Clear();
                    primera = false;
                    continue;
                }
                // Palabra más larga que la línea: se corta.
                yield return (primera ? "" : prefijo) + palabra[..limite];
                primera = false;
                palabra = palabra[limite..];
                if (palabra.Length == 0) break;
            }
        }
        if (actual.Length > 0 || primera) yield return (primera ? "" : prefijo) + actual;
    }

    /// <summary>Quita saltos de línea, tabuladores y caracteres de control que romperían el formato o los comandos ESC/POS.</summary>
    public static string Limpiar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var limpio = new StringBuilder(texto.Length);
        foreach (var c in texto) limpio.Append(char.IsControl(c) ? ' ' : c);
        return string.Join(' ', limpio.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
