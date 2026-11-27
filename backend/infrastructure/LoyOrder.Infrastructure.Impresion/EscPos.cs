using System.Text;
using LoyOrder.Application;
namespace LoyOrder.Infrastructure.Impresion;

/// <summary>
/// Página de códigos de la impresora para acentos, ñ, ¿ y ¡. El número que va en ESC t cambia según el fabricante;
/// estos son los de la tabla de Epson, que siguen la mayoría de las impresoras genéricas de 80 mm. La hoja de prueba
/// imprime una muestra con cada una para elegir la que se ve bien (configuración Impresion:PaginaCodigos).
/// </summary>
public sealed record PaginaCodigos(string Nombre, byte NumeroEscPos, int CodigoWindows)
{
    static PaginaCodigos() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static readonly PaginaCodigos Pc850 = new("PC850", 2, 850);
    public static readonly PaginaCodigos Pc858 = new("PC858", 19, 858);
    public static readonly PaginaCodigos Wpc1252 = new("WPC1252", 16, 1252);
    public static IReadOnlyList<PaginaCodigos> Todas { get; } = [Pc850, Pc858, Wpc1252];

    public static PaginaCodigos Obtener(string? nombre) =>
        Todas.FirstOrDefault(p => p.Nombre.Equals(nombre?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Impresion:PaginaCodigos debe ser {string.Join(", ", Todas.Select(p => p.Nombre))}.");

    /// <summary>Lo que no existe en la página de códigos se imprime como '?'.</summary>
    public Encoding Codificacion => Encoding.GetEncoding(CodigoWindows, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
}

/// <summary>Convierte un <see cref="DocumentoImpresion"/> en comandos ESC/POS.</summary>
public static class EscPos
{
    private const byte Esc = 0x1B, Gs = 0x1D, Lf = 0x0A;

    public static byte[] Codificar(DocumentoImpresion documento, PaginaCodigos pagina)
    {
        var b = new List<byte>(2048);
        Inicializar(b, pagina);
        foreach (var linea in documento.Lineas) Linea(b, linea, pagina);
        Cortar(b);
        return [.. b];
    }

    /// <summary>Encabezado con la página configurada y la misma frase en cada página de códigos.</summary>
    public static byte[] HojaDePrueba(DocumentoImpresion encabezado, PaginaCodigos configurada)
    {
        var b = new List<byte>(2048);
        Inicializar(b, configurada);
        foreach (var linea in encabezado.Lineas) Linea(b, linea, configurada);
        foreach (var pagina in PaginaCodigos.Todas)
        {
            b.AddRange([Esc, (byte)'t', pagina.NumeroEscPos]);
            Linea(b, new($"{pagina.Nombre} (ESC t {pagina.NumeroEscPos}):", Negrita: true), pagina);
            Linea(b, new("  áéíóú ÁÉÍÓÚ ñÑ ü ¿Sí? ¡Listo!"), pagina);
        }
        b.AddRange([Esc, (byte)'t', configurada.NumeroEscPos]);
        Linea(b, new(Papel.Linea('-')), configurada);
        Linea(b, new($"Configurada: {configurada.Nombre}", Negrita: true), configurada);
        Cortar(b);
        return [.. b];
    }

    private static void Inicializar(List<byte> b, PaginaCodigos pagina)
    {
        b.AddRange([Esc, (byte)'@']);                       // ESC @ reinicia la impresora
        b.AddRange([Esc, (byte)'t', pagina.NumeroEscPos]);  // ESC t n página de códigos
    }

    private static void Linea(List<byte> b, LineaImpresion l, PaginaCodigos pagina)
    {
        b.AddRange([Esc, (byte)'a', l.Alineacion switch { Alineacion.Centro => 1, Alineacion.Derecha => 2, _ => 0 }]);
        if (l.Negrita) b.AddRange([Esc, (byte)'E', 1]);
        if (l.Doble) b.AddRange([Gs, (byte)'!', 0x11]);     // doble alto y doble ancho
        if (l.Invertido) b.AddRange([Gs, (byte)'B', 1]);    // blanco sobre negro
        b.AddRange(pagina.Codificacion.GetBytes(SinControles(l.Texto)));
        b.Add(Lf);
        if (l.Invertido) b.AddRange([Gs, (byte)'B', 0]);
        if (l.Doble) b.AddRange([Gs, (byte)'!', 0]);
        if (l.Negrita) b.AddRange([Esc, (byte)'E', 0]);
    }

    private static void Cortar(List<byte> b)
    {
        b.AddRange([Esc, (byte)'a', 0]);
        b.AddRange([Esc, (byte)'d', 4]);                    // avanza 4 líneas para que el corte no se coma el texto
        b.AddRange([Gs, (byte)'V', 66, 0]);                 // GS V 66 0: corte parcial
    }

    /// <summary>
    /// Un nombre, dirección o indicación con caracteres de control podría meter comandos a la impresora
    /// (por ejemplo, abrir el cajón). Se cambian por espacios.
    /// </summary>
    internal static string SinControles(string texto) =>
        string.Create(texto.Length, texto, (destino, origen) =>
        {
            for (var i = 0; i < origen.Length; i++) destino[i] = char.IsControl(origen[i]) ? ' ' : origen[i];
        });
}
