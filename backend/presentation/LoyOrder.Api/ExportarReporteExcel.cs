using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using LoyOrder.Application;

namespace LoyOrder.Api;

public static class ExportarReporteExcel
{
    private static readonly XNamespace Hoja = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static byte[] Generar(ReporteRestaurante reporte)
    {
        using var memoria = new MemoryStream();
        using (var archivo = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Agregar(archivo, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Agregar(archivo, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Agregar(archivo, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Reporte" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Agregar(archivo, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            using var destino = archivo.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
            CrearHoja(reporte).Save(destino);
        }
        return memoria.ToArray();
    }

    private static XElement CrearHoja(ReporteRestaurante reporte)
    {
        var ventas = reporte.Tipo == TipoReporte.Ventas;
        var registros = new XElement(Hoja + "sheetData",
            Fila(Texto(ventas ? "Ventas entregadas" : "Pedidos")),
            Fila(Texto("Desde"), Texto(Fecha(reporte.Desde)), Texto("Hasta"), Texto(Fecha(reporte.Hasta))),
            Fila(Texto("Registros"), Numero(reporte.Registros.Count), Texto("Importe total (MXN)"), Numero(reporte.Importe)),
            Fila(Texto("Estado"), Texto("Cantidad"), Texto("Importe (MXN)")),
            reporte.Estados.Select(e => Fila(Texto(e.Estado), Numero(e.Cantidad), Numero(e.Importe))),
            Fila(),
            Fila(Texto("Folio"), Texto(ventas ? "Fecha de entrega" : "Fecha de creación"), Texto("Modalidad"),
                Texto(ventas ? "Estado en Loyverse" : "Estado"), Texto("Subtotal (MXN)"), Texto("Envío (MXN)"),
                Texto("Total (MXN)"), ventas ? Texto("Recibo") : null),
            reporte.Registros.Select(r => Fila(Texto(r.Folio), Texto(r.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
                Texto(r.Modalidad), Texto(r.Estado), Numero(r.Subtotal), Numero(r.Envio), Numero(r.Total),
                ventas ? Texto(r.Recibo ?? "") : null)));
        return new XElement(Hoja + "worksheet",
            new XElement(Hoja + "cols", Columna(1, 2, 24), Columna(3, 4, 21), Columna(5, 8, 18)), registros);
    }

    private static XElement Columna(int inicio, int fin, int ancho) =>
        new(Hoja + "col", new XAttribute("min", inicio), new XAttribute("max", fin),
            new XAttribute("width", ancho), new XAttribute("customWidth", 1));
    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static XElement Fila(params XElement?[] contenido) => new(Hoja + "row", contenido);
    private static XElement Texto(string valor) => new(Hoja + "c", new XAttribute("t", "inlineStr"),
        new XElement(Hoja + "is", new XElement(Hoja + "t", valor)));
    private static XElement Numero(decimal valor) => new(Hoja + "c",
        new XElement(Hoja + "v", valor.ToString(CultureInfo.InvariantCulture)));

    private static void Agregar(ZipArchive archivo, string nombre, string contenido)
    {
        using var escritor = new StreamWriter(archivo.CreateEntry(nombre, CompressionLevel.Fastest).Open());
        escritor.Write(contenido);
    }
}
