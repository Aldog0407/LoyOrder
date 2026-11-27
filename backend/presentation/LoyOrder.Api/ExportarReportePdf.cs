using System.Globalization;
using System.Text;
using LoyOrder.Application;

namespace LoyOrder.Api;

public static class ExportarReportePdf
{
    private const int FilasPorPagina = 29;
    private static readonly CultureInfo Moneda = CultureInfo.GetCultureInfo("es-MX");

    public static byte[] Generar(ReporteRestaurante reporte)
    {
        var paginas = reporte.Registros.Chunk(FilasPorPagina).ToList();
        if (paginas.Count == 0) paginas.Add([]);

        using var salida = new MemoryStream();
        var posiciones = new List<long>();
        void Escribir(string texto) => salida.Write(Encoding.ASCII.GetBytes(texto));
        void Objeto(string cuerpo)
        {
            posiciones.Add(salida.Position);
            Escribir(FormattableString.Invariant($"{posiciones.Count} 0 obj\n{cuerpo}\nendobj\n"));
        }

        Escribir("%PDF-1.4\n");
        Objeto("<< /Type /Catalog /Pages 2 0 R >>");
        var referencias = string.Join(" ", paginas.Select((_, n) => FormattableString.Invariant($"{4 + n * 2} 0 R")));
        Objeto(FormattableString.Invariant($"<< /Type /Pages /Kids [{referencias}] /Count {paginas.Count} >>"));
        Objeto("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (var n = 0; n < paginas.Count; n++)
        {
            Objeto(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + n * 2} 0 R >>"));
            var contenido = Pagina(reporte, paginas[n], n + 1, paginas.Count);
            Objeto(FormattableString.Invariant($"<< /Length {contenido.Length} >>\nstream\n{contenido}endstream"));
        }

        var inicio = salida.Position;
        Escribir(FormattableString.Invariant($"xref\n0 {posiciones.Count + 1}\n0000000000 65535 f \n"));
        foreach (var posicion in posiciones)
            Escribir(FormattableString.Invariant($"{posicion:0000000000} 00000 n \n"));
        Escribir(FormattableString.Invariant($"trailer\n<< /Size {posiciones.Count + 1} /Root 1 0 R >>\nstartxref\n{inicio}\n%%EOF\n"));
        return salida.ToArray();
    }

    private static string Pagina(ReporteRestaurante reporte, RegistroReporte[] registros, int numero, int paginas)
    {
        var ventas = reporte.Tipo == TipoReporte.Ventas;
        var contenido = new StringBuilder();
        void Texto(int x, int y, int tamano, string valor) => contenido.Append(CultureInfo.InvariantCulture,
            $"BT /F1 {tamano} Tf {x} {y} Td ({Escapar(valor)}) Tj ET\n");
        Texto(32, 558, 19, ventas ? "LoyOrder | Ventas entregadas" : "LoyOrder | Pedidos");
        Texto(32, 536, 10, FormattableString.Invariant($"Del {reporte.Desde:dd/MM/yyyy} al {reporte.Hasta:dd/MM/yyyy}"));
        Texto(32, 518, 10, FormattableString.Invariant($"Registros: {reporte.Registros.Count}   Importe total: {Importe(reporte.Importe)} MXN"));
        contenido.Append("0.97 0.94 0.89 rg 26 481 790 22 re f 0.12 0.12 0.12 rg\n");
        Texto(32, 489, 8, "Folio");
        Texto(185, 489, 8, ventas ? "Fecha de entrega" : "Fecha de creación");
        Texto(283, 489, 8, "Modalidad");
        Texto(360, 489, 8, ventas ? "Estado en Loyverse" : "Estado");
        if (ventas) Texto(488, 489, 8, "Recibo");
        Texto(583, 489, 8, "Subtotal");
        Texto(662, 489, 8, "Envío");
        Texto(741, 489, 8, "Total");

        var y = 468;
        foreach (var fila in registros)
        {
            Texto(32, y, 8, fila.Folio);
            Texto(185, y, 8, fila.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
            Texto(283, y, 8, fila.Modalidad);
            Texto(360, y, 8, fila.Estado);
            if (ventas) Texto(488, y, 8, fila.Recibo is { Length: > 18 } recibo ? recibo[..15] + "..." : fila.Recibo ?? "—");
            Texto(583, y, 8, Importe(fila.Subtotal));
            Texto(662, y, 8, Importe(fila.Envio));
            Texto(741, y, 8, Importe(fila.Total));
            y -= 15;
        }
        if (registros.Length == 0) Texto(32, 463, 10, "No hay registros en este periodo.");
        Texto(32, 24, 9, FormattableString.Invariant($"Página {numero} de {paginas}"));
        return contenido.ToString();
    }

    private static string Importe(decimal valor) => valor.ToString("N2", Moneda);

    private static string Escapar(string texto)
    {
        var limpio = new StringBuilder();
        foreach (var letra in texto)
        {
            switch (letra)
            {
                case '\\': case '(': case ')': limpio.Append('\\').Append(letra); break;
                case >= ' ' and <= '~': limpio.Append(letra); break;
                case '—': limpio.Append("\\227"); break;
                case >= '\u00a0' and <= '\u00ff':
                    limpio.Append('\\').Append(Convert.ToString(letra, 8).PadLeft(3, '0')); break;
                default: limpio.Append('?'); break;
            }
        }
        return limpio.ToString();
    }
}
