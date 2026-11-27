using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using LoyOrder.Application;
using LoyOrder.Infrastructure.Impresion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Pruebas del módulo de impresión ESC/POS (7.2.11). No necesitan impresora: revisan los bytes y los envían por TCP
// a un servidor local que hace de impresora.
var pruebas = typeof(Pruebas).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name.StartsWith("Prueba_")).OrderBy(m => m.MetadataToken).ToList();
var fallidas = 0;
foreach (var p in pruebas)
{
    try { await (Task)p.Invoke(null, null)!; Console.WriteLine($"  OK   {p.Name}"); }
    catch (Exception e)
    {
        fallidas++;
        var real = e is TargetInvocationException t ? t.InnerException! : e;
        Console.WriteLine($"  FALLA {p.Name}: {real.GetType().Name}: {real.Message}");
    }
}
Console.WriteLine($"\n{pruebas.Count - fallidas} de {pruebas.Count} pruebas aprobadas.");
return fallidas == 0 ? 0 : 1;

static class Afirmar
{
    public static void Cierto(bool c, string que) { if (!c) throw new Exception(que); }
    public static bool Contiene(byte[] datos, params byte[] secuencia)
    {
        for (var i = 0; i + secuencia.Length <= datos.Length; i++)
            if (datos.AsSpan(i, secuencia.Length).SequenceEqual(secuencia)) return true;
        return false;
    }
    public static async Task<T> Lanza<T>(Func<Task> accion) where T : Exception
    {
        try { await accion(); }
        catch (T e) { return e; }
        throw new Exception($"Se esperaba {typeof(T).Name}");
    }
}

static class Pruebas
{
    private static readonly DocumentoImpresion Ejemplo = new([
        new("Alitas Don Carlos", Alineacion.Centro, true, true),
        new(Papel.Banda("*** REIMPRESIÓN ***"), Negrita: true, Invertido: true),
        new("Artículo: Piña ñoña ¿sí? ¡Í!", Alineacion.Derecha),
    ]);

    private static ImpresoraTcp Impresora(int puerto, int segundos = 2, string pagina = "PC858") =>
        new(Options.Create(new OpcionesImpresion { Puerto = puerto, TiempoEsperaSegundos = segundos, PaginaCodigos = pagina }),
            NullLogger<ImpresoraTcp>.Instance);

    public static Task Prueba_InicializaYEligePaginaDeCodigos()
    {
        var b = EscPos.Codificar(Ejemplo, PaginaCodigos.Pc858);
        Afirmar.Cierto(b.AsSpan(0, 5).SequenceEqual(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 19 }), "ESC @ y ESC t 19 al inicio");
        Afirmar.Cierto(EscPos.Codificar(Ejemplo, PaginaCodigos.Wpc1252).AsSpan(2, 3).SequenceEqual(new byte[] { 0x1B, 0x74, 16 }), "ESC t 16 para WPC1252");
        return Task.CompletedTask;
    }

    public static Task Prueba_FormatoNegritaDobleInvertidoAlineacionYCorte()
    {
        var b = EscPos.Codificar(Ejemplo, PaginaCodigos.Pc858);
        Afirmar.Cierto(Afirmar.Contiene(b, 0x1B, 0x61, 1) && Afirmar.Contiene(b, 0x1B, 0x61, 2), "centro y derecha");
        Afirmar.Cierto(Afirmar.Contiene(b, 0x1B, 0x45, 1) && Afirmar.Contiene(b, 0x1B, 0x45, 0), "negrita y regreso");
        Afirmar.Cierto(Afirmar.Contiene(b, 0x1D, 0x21, 0x11) && Afirmar.Contiene(b, 0x1D, 0x21, 0), "doble tamaño y regreso");
        Afirmar.Cierto(Afirmar.Contiene(b, 0x1D, 0x42, 1) && Afirmar.Contiene(b, 0x1D, 0x42, 0), "invertido y regreso");
        Afirmar.Cierto(b.AsSpan(b.Length - 7).SequenceEqual(new byte[] { 0x1B, 0x64, 4, 0x1D, 0x56, 66, 0 }), "avance y corte parcial al final");
        return Task.CompletedTask;
    }

    public static Task Prueba_AcentosYEnieEnCadaPagina()
    {
        var pc858 = EscPos.Codificar(new([new("áñÍ¿¡")]), PaginaCodigos.Pc858);
        Afirmar.Cierto(Afirmar.Contiene(pc858, 0xA0, 0xA4, 0xD6, 0xA8, 0xAD), "PC858: á ñ Í ¿ ¡");
        var w1252 = EscPos.Codificar(new([new("áñÍ¿¡")]), PaginaCodigos.Wpc1252);
        Afirmar.Cierto(Afirmar.Contiene(w1252, 0xE1, 0xF1, 0xCD, 0xBF, 0xA1), "WPC1252: á ñ Í ¿ ¡");
        var desconocido = EscPos.Codificar(new([new("😀")]), PaginaCodigos.Pc850);
        Afirmar.Cierto(Afirmar.Contiene(desconocido, (byte)'?'), "lo que no existe sale como ?");
        return Task.CompletedTask;
    }

    public static Task Prueba_TextoNoPuedeMeterComandos()
    {
        // ESC p abre el cajón de dinero; GS V corta el papel. Si vinieran en un nombre, se imprimen como espacios.
        var b = EscPos.Codificar(new([new("Ana\u001Bp\u0000\u0019 Ruiz\u001DV")]), PaginaCodigos.Pc858);
        Afirmar.Cierto(!Afirmar.Contiene(b, 0x1B, (byte)'p'), "sin ESC p");
        Afirmar.Cierto(!Afirmar.Contiene(b, 0x1D, (byte)'V', (byte)'\n'), "sin GS V del texto");
        Afirmar.Cierto(Afirmar.Contiene(b, (byte)'A', (byte)'n', (byte)'a', (byte)' ', (byte)'p'), "el texto queda con espacios");
        return Task.CompletedTask;
    }

    public static Task Prueba_HojaDePruebaMuestraLasTresPaginas()
    {
        var b = EscPos.HojaDePrueba(new([new("HOJA DE PRUEBA")]), PaginaCodigos.Pc858);
        foreach (var n in new byte[] { 2, 19, 16 }) Afirmar.Cierto(Afirmar.Contiene(b, 0x1B, 0x74, n), $"ESC t {n}");
        Afirmar.Cierto(Afirmar.Contiene(b, System.Text.Encoding.ASCII.GetBytes("Configurada: PC858")), "indica la configurada");
        return Task.CompletedTask;
    }

    public static async Task Prueba_PaginaDeCodigosInvalidaSeRechaza()
    {
        await Afirmar.Lanza<InvalidOperationException>(() => { Impresora(9100, pagina: "UTF8"); return Task.CompletedTask; });
        await Afirmar.Lanza<InvalidOperationException>(() => { Impresora(0); return Task.CompletedTask; });
    }

    public static async Task Prueba_EnviaLosBytesPorTcp()
    {
        var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var puerto = ((IPEndPoint)servidor.LocalEndpoint).Port;
        var recibido = Task.Run(async () =>
        {
            using var conexion = await servidor.AcceptTcpClientAsync();
            using var memoria = new MemoryStream();
            await conexion.GetStream().CopyToAsync(memoria);
            return memoria.ToArray();
        });
        await Impresora(puerto).ImprimirAsync(new(1, "Caja", "127.0.0.1"), Ejemplo, default);
        var bytes = await recibido.WaitAsync(TimeSpan.FromSeconds(5));
        servidor.Stop();
        Afirmar.Cierto(bytes.SequenceEqual(EscPos.Codificar(Ejemplo, PaginaCodigos.Pc858)), "la impresora recibe exactamente el ESC/POS");
    }

    public static async Task Prueba_ImpresoraApagadaDaNoDisponible()
    {
        var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var puerto = ((IPEndPoint)servidor.LocalEndpoint).Port;
        servidor.Stop(); // nadie escucha en ese puerto: conexión rechazada
        var e = await Afirmar.Lanza<ImpresoraNoDisponibleException>(() => Impresora(puerto).ImprimirAsync(new(1, "Caja", "127.0.0.1"), Ejemplo, default));
        Afirmar.Cierto(e.Impresora == "Caja", "nombre de la impresora en el error");
    }

    public static async Task Prueba_ImpresoraQueNoRespondeRespetaElTiempoLimite()
    {
        var reloj = Stopwatch.StartNew();
        // 192.0.2.1 es de documentación (TEST-NET-1): no hay nadie, así que vence el tiempo o la red la rechaza.
        await Afirmar.Lanza<ImpresoraNoDisponibleException>(() => Impresora(9100, 1).ImprimirAsync(new(1, "Caja", "192.0.2.1"), Ejemplo, default));
        Afirmar.Cierto(reloj.Elapsed < TimeSpan.FromSeconds(4), $"respeta el tiempo límite ({reloj.Elapsed.TotalSeconds:0.0} s)");
    }
}
