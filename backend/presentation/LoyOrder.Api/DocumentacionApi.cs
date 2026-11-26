using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace LoyOrder.Api;

public sealed record IdentidadApi(long IdUsuario,string Nombre,string Correo,string Rol);
public sealed record RegistroApi(long IdUsuario,string Nombre,string Correo,string Rol,bool? CorreoVerificado,Guid? IdDesafio);
public sealed record SesionApi(string AccessToken,string TokenType,DateTimeOffset ExpiresAt,string RefreshToken,
    DateTimeOffset IdleExpiresAt,DateTimeOffset SessionExpiresAt,IdentidadApi Usuario);
public sealed record DesafioApi(Guid IdDesafio);
public sealed record MensajeApi(string Mensaje);
public sealed record EstadoApi(string Estado);
public sealed class ErrorApi : ProblemDetails
{
    public string? TraceId { get; set; }
    public string? Codigo { get; set; }
    public Guid? IdDesafio { get; set; }
    public Dictionary<string,string[]>? Errors { get; set; }
}

internal sealed class DescribirSeguridad : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document,OpenApiDocumentTransformerContext context,CancellationToken ct)
    {
        document.Info.Title="LoyOrder API";document.Info.Version="v1";
        document.Info.Description="API REST de LoyOrder. Roles exclusivos: CLIENTE, OPERADOR y ADMINISTRADOR. " +
            "Para probar una ruta protegida, inicia sesión con /auth/login y pega su accessToken en Authorize. " +
            "Ejecutar una operación desde aquí llama a la API y modifica los datos cuando corresponde. " +
            "El registro de venta usa el token de Loyverse configurado en el servidor; ese secreto no se introduce en Swagger.";
        document.Components??=new OpenApiComponents();
        document.Components.SecuritySchemes=new Dictionary<string,IOpenApiSecurityScheme> {
            ["Bearer"]=new OpenApiSecurityScheme { Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT",
                Description="Solo accessToken de LoyOrder. No pegar refreshToken ni el token de Loyverse." }
        };
        var descripciones=context.DescriptionGroups.SelectMany(g=>g.Items).ToList();
        foreach(var ruta in document.Paths)
            foreach(var operacion in ruta.Value.Operations??[]) {
                var descripcion=descripciones.FirstOrDefault(d=>"/"+d.RelativePath?.Split('?')[0]==ruta.Key &&
                    string.Equals(d.HttpMethod,operacion.Key.ToString(),StringComparison.OrdinalIgnoreCase));
                var metadata=descripcion?.ActionDescriptor.EndpointMetadata;
                var permisos=metadata?.OfType<IAuthorizeData>().ToList()??[];
                var protegida=permisos.Count>0 && !(metadata?.OfType<IAllowAnonymous>().Any()??false);
                var roles=permisos.SelectMany(a=>(a.Roles??"").Split(',',StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
                operacion.Value.Description=(operacion.Value.Description??"")+"\n\nAcceso: "+
                    (!protegida?"sin JWT de acceso obligatorio.":roles.Length==0?"cualquier sesión válida.":string.Join(", ",roles)+". Sin herencia entre roles.");
                if(protegida) operacion.Value.Security=[new OpenApiSecurityRequirement {
                    [new OpenApiSecuritySchemeReference("Bearer",document)]=[]
                }];
            }
        return Task.CompletedTask;
    }
}

internal static class DocumentacionApi
{
    public static void AgregarDocumentacion(this IServiceCollection servicios)=>
        servicios.AddOpenApi("v1",opciones=>opciones.AddDocumentTransformer<DescribirSeguridad>());
    public static void MapearDocumentacion(this WebApplication app)
    {
        if(!app.Environment.IsDevelopment()) return;
        app.MapOpenApi();
        app.UseSwaggerUI(o=> {
            o.RoutePrefix="swagger";o.DocumentTitle="LoyOrder · API";
            o.SwaggerEndpoint("/openapi/v1.json","LoyOrder API v1");
            o.DefaultModelsExpandDepth(-1);
        });
    }
}
