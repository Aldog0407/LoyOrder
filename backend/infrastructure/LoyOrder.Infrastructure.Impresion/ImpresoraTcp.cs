using System.Net.Sockets;
using LoyOrder.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace LoyOrder.Infrastructure.Impresion;

/// <summary>Sección "Impresion" de la configuración.</summary>
public sealed class OpcionesImpresion
{
    /// <summary>Puerto RAW de las impresoras de red (la tabla impresora solo guarda la IP).</summary>
    public int Puerto { get; set; } = 9100;
    /// <summary>PC850, PC858 o WPC1252. Se elige con la hoja de prueba.</summary>
    public string PaginaCodigos { get; set; } = "PC858";
    /// <summary>Tiempo máximo para conectar y enviar. Si la impresora no responde, la API responde 503.</summary>
    public int TiempoEsperaSegundos { get; set; } = 5;
}

/// <summary>Envía ESC/POS por TCP (impresoras térmicas por red, normalmente el puerto 9100).</summary>
public sealed class ImpresoraTcp(IOptions<OpcionesImpresion> opciones, ILogger<ImpresoraTcp> logger) : IImpresoraTermica
{
    private readonly OpcionesImpresion o = Validar(opciones.Value);
    private readonly PaginaCodigos pagina = PaginaCodigos.Obtener(opciones.Value.PaginaCodigos);

    public Task ImprimirAsync(ImpresoraDestino destino, DocumentoImpresion documento, CancellationToken ct) =>
        EnviarAsync(destino, EscPos.Codificar(documento, pagina), ct);

    public Task ImprimirPruebaAsync(ImpresoraDestino destino, DocumentoImpresion encabezado, CancellationToken ct) =>
        EnviarAsync(destino, EscPos.HojaDePrueba(encabezado, pagina), ct);

    internal async Task EnviarAsync(ImpresoraDestino destino, byte[] datos, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(o.TiempoEsperaSegundos));
        try
        {
            using var cliente = new TcpClient { NoDelay = true };
            await cliente.ConnectAsync(destino.Direccion, o.Puerto, limite.Token);
            var flujo = cliente.GetStream();
            await flujo.WriteAsync(datos, limite.Token);
            await flujo.FlushAsync(limite.Token);
            cliente.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception e) when (e is SocketException or IOException ||
                                  (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Solo el tipo de error: la IP de la impresora ya la conoce el administrador.
            logger.LogWarning("No se pudo imprimir en {Impresora}: {Tipo}", destino.Nombre, e.GetType().Name);
            throw new ImpresoraNoDisponibleException(destino.Nombre);
        }
        logger.LogInformation("Impresión enviada a {Impresora} ({Bytes} bytes)", destino.Nombre, datos.Length);
    }

    private static OpcionesImpresion Validar(OpcionesImpresion o)
    {
        if (o.Puerto is < 1 or > 65535) throw new InvalidOperationException("Impresion:Puerto debe estar entre 1 y 65535.");
        if (o.TiempoEsperaSegundos is < 1 or > 60) throw new InvalidOperationException("Impresion:TiempoEsperaSegundos debe estar entre 1 y 60.");
        return o;
    }
}

public static class RegistroImpresion
{
    public static IServiceCollection AddImpresion(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesImpresion>(configuracion.GetSection("Impresion"));
        services.AddSingleton<IImpresoraTermica, ImpresoraTcp>();
        return services;
    }
}
