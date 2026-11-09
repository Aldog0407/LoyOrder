using System.Threading.Channels;
using LoyOrder.Application;
using LoyOrder.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LoyOrder.Api;

internal sealed class OpcionesSmtp
{
    public string Host { get; init; } = "";
    public int Puerto { get; init; } = 587;
    public string Seguridad { get; init; } = "StartTls";
    public string Remitente { get; init; } = "";
    public string? Usuario { get; init; }
    public string? Contrasena { get; init; }
}

internal sealed class CorreoSmtp(OpcionesSmtp opciones, SecureSocketOptions seguridad)
{
    public async Task EnviarAsync(MensajeCorreo correo, CancellationToken ct)
    {
        using var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(opciones.Remitente));
        mensaje.To.Add(MailboxAddress.Parse(correo.Destino));
        mensaje.Subject = correo.Asunto;
        mensaje.Body = new TextPart("plain") { Text = correo.Texto };

        using var cliente = new SmtpClient();
        await cliente.ConnectAsync(opciones.Host, opciones.Puerto, seguridad, ct);
        if (!string.IsNullOrWhiteSpace(opciones.Usuario))
            await cliente.AuthenticateAsync(opciones.Usuario, opciones.Contrasena!, ct);
        await cliente.SendAsync(mensaje, ct);
        await cliente.DisconnectAsync(true, ct);
    }
}

internal sealed class ColaCorreo(ILogger<ColaCorreo> logger) : IColaCorreo
{
    private readonly Channel<TrabajoCorreo> canal = Channel.CreateBounded<TrabajoCorreo>(new BoundedChannelOptions(128)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public bool IntentarEncolar(TrabajoCorreo trabajo)
    {
        if (canal.Writer.TryWrite(trabajo)) return true;
        logger.LogWarning("La cola de autenticación por correo está llena.");
        return false;
    }

    public IAsyncEnumerable<TrabajoCorreo> LeerAsync(CancellationToken ct) => canal.Reader.ReadAllAsync(ct);
}

internal sealed class ProcesarCorreo(ColaCorreo cola, CorreoSmtp smtp, IServiceScopeFactory scopes,
    ILogger<ProcesarCorreo> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var trabajo in cola.LeerAsync(stoppingToken))
        {
            try
            {
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                limite.CancelAfter(TimeSpan.FromSeconds(20));
                var mensaje = trabajo as MensajeCorreo;
                if (trabajo is SolicitudRecuperacion solicitud)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var acceso = scope.ServiceProvider.GetRequiredService<CorreoAcceso>();
                    mensaje = await acceso.PrepararRecuperacionAsync(solicitud.Correo, limite.Token);
                }
                if (mensaje is not null) await smtp.EnviarAsync(mensaje, limite.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                // El detalle de SMTP puede contener el destinatario o el mensaje.
                logger.LogWarning("No se pudo procesar el correo de autenticación ({Tipo}).", e.GetType().Name);
            }
        }
    }
}

internal static class ConfigurarCorreo
{
    public static void AgregarCorreoAutenticacion(this WebApplicationBuilder builder)
    {
        var seccion = builder.Configuration.GetSection("CorreoAutenticacion");
        if (!seccion.GetValue<bool>("Habilitado")) return;

        byte[] clave;
        try { clave = Convert.FromBase64String(seccion["ClaveCodigo"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("CorreoAutenticacion:ClaveCodigo debe ser Base64."); }
        if (clave.Length < 32) throw new InvalidOperationException("CorreoAutenticacion:ClaveCodigo requiere al menos 32 bytes.");

        if (!Uri.TryCreate(seccion["UrlFrontend"], UriKind.Absolute, out var frontend) ||
            !string.IsNullOrEmpty(frontend.UserInfo) || frontend.PathAndQuery != "/" || frontend.Fragment != "" ||
            (frontend.Scheme != "https" && !(builder.Environment.IsDevelopment() && frontend.IsLoopback && frontend.Scheme == "http")))
            throw new InvalidOperationException("Configure CorreoAutenticacion:UrlFrontend con el origen HTTPS del frontend.");

        var opciones = seccion.GetSection("Smtp").Get<OpcionesSmtp>() ?? new();
        if (string.IsNullOrWhiteSpace(opciones.Host) || opciones.Puerto is < 1 or > 65535 ||
            !MailboxAddress.TryParse(opciones.Remitente, out _) ||
            string.IsNullOrWhiteSpace(opciones.Usuario) != string.IsNullOrWhiteSpace(opciones.Contrasena))
            throw new InvalidOperationException("Revise la configuración SMTP de CorreoAutenticacion.");

        var seguridad = opciones.Seguridad switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" when builder.Environment.IsDevelopment() &&
                (opciones.Host is "localhost" or "127.0.0.1" or "::1") &&
                string.IsNullOrWhiteSpace(opciones.Usuario) => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("Configure SMTP con StartTls o SslOnConnect.")
        };

        builder.Services.AddSingleton(new ConfiguracionDesafios(clave, new Uri(frontend, "/restablecer-contrasena")));
        builder.Services.AddScoped<IDesafioCorreoRepository, DesafioCorreoRepository>();
        builder.Services.AddScoped<CorreoAcceso>();
        builder.Services.AddSingleton(new CorreoSmtp(opciones, seguridad));
        builder.Services.AddSingleton<ColaCorreo>();
        builder.Services.AddSingleton<IColaCorreo>(sp => sp.GetRequiredService<ColaCorreo>());
        builder.Services.AddHostedService<ProcesarCorreo>();
        builder.Services.AddCors(o => o.AddPolicy("Frontend", p =>
            p.WithOrigins(frontend.GetLeftPart(UriPartial.Authority)).AllowAnyHeader().WithMethods("GET", "POST")));
    }
}
