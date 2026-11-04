# LoyOrder.Infrastructure.Loyverse: módulo cliente de la API de Loyverse

Biblioteca de .NET 10 (LTS) que usa la API REST de LoyOrder (ASP.NET Core) para hablar con Loyverse POS. Resuelve:

- **El token.** Se manda como `Authorization: Bearer` en cada petición y lo entrega `IProveedorTokenLoyverse`, nunca la configuración del repo. `ValidarTokenAsync` prueba un token recién capturado antes de guardarlo (CU-08).
- **La paginación.** Recorre el `cursor` con `limit=250` hasta traer todo. Se detiene si la API repitiera un cursor.
- **El límite de uso.** Lleva su propia cuenta con ventana deslizante: 290 peticiones cada 300 s por defecto, de un máximo de 300 (CU-10 RN-02). Loyverse no manda encabezados de límite.
- **Los errores y reintentos:**
  - 401 lanza `LoyverseNoAutorizadoException` y no reintenta.
  - 429 reintenta hasta 3 veces con espera de 2, 4 y 8 s, o la que indique `Retry-After`.
  - Los 5xx y las fallas de red se reintentan en las consultas, pero **nunca** al registrar una venta, para no duplicarla (RN08). Para eso está `BuscarReciboPorFolioAsync`.

## Consulta de existencias al confirmar un pedido

`ObtenerExistenciasDeVariantesAsync(tiendaId, varianteIds)` consulta las existencias **actuales** de las variantes del carrito
justo antes de registrar el pedido (EV-21 paso 6, EV-24). Cubre los minutos entre una sincronización y otra.

- Las variantes que no aparecen en el resultado no llevan control de inventario y no se limitan.
- Si Loyverse no responde (`LoyverseException`), el servicio de pedidos valida con la última sincronización y no bloquea el pedido.
- No sustituye al polling del CU-10, que es el que mantiene precios, disponibilidad, imágenes y artículos nuevos o eliminados.

## Estructura

```
backend/infrastructure/LoyOrder.Infrastructure.Loyverse/   Biblioteca (sin paquetes de NuGet)
  LoyverseClient.cs                  ILoyverseClient y su implementación
  LimitadorPeticiones.cs             Límite de uso con ventana deslizante + reloj inyectable
  ProveedorToken.cs                  IProveedorTokenLoyverse
  Modelos.cs                         Tienda, Categoria, Articulo, Variante, NivelInventario, TipoPago, NuevoRecibo, Recibo
  Excepciones.cs                     LoyverseException y derivadas
  LoyverseOptions.cs                 Configuración (sección "Loyverse")
  ServiceCollectionExtensions.cs     services.AddLoyverse(configuration)
tests/unit/LoyOrder.Infrastructure.Loyverse.Tests/          21 pruebas sin red (HttpMessageHandler y reloj falsos)
tests/integration/LoyOrder.Infrastructure.Loyverse.Demo/    Consola que consulta la cuenta de PRUEBA (no registra ventas)
LoyOrder.sln                                                Solución en la raíz del repo
```

## Uso en LoyOrder.Api

```csharp
// Program.cs
builder.Services.AddLoyverse(builder.Configuration);

// En un servicio (por ejemplo SincronizacionService, CU-10)
public class SincronizacionService(ILoyverseClient loyverse)
{
    public async Task EjecutarAsync(DateTimeOffset? ultima, string tiendaId, CancellationToken ct)
    {
        var categorias = await loyverse.ObtenerCategoriasAsync(ct: ct);
        var articulos  = await loyverse.ObtenerArticulosAsync(ultima, incluirEliminados: true, ct: ct);
        var inventario = await loyverse.ObtenerExistenciasAsync(tiendaId, ct);
        // ... upsert en PostgreSQL
    }
}
```

`appsettings.json` (opcional; estos son los valores por defecto):

```json
"Loyverse": {
  "UrlBase": "https://api.loyverse.com/v1.0/",
  "TamanoPagina": 250,
  "MaxPeticionesPorVentana": 290,
  "VentanaLimite": "00:05:00",
  "MaxReintentos": 3
}
```

En producción, el token se guarda cifrado con la configuración del restaurante (CU-08 RN-01). Para eso hay que registrar una implementación propia de `IProveedorTokenLoyverse` **antes** de `AddLoyverse`, que lea el token de la base de datos y lo descifre. Mientras tanto, en desarrollo se usa `Loyverse:Token` desde user-secrets o la variable `LOYVERSE_ACCESS_TOKEN` de `.env.example`.

## Probar

```bash
dotnet run --project tests/unit/LoyOrder.Infrastructure.Loyverse.Tests        # 21 pruebas, sin internet

# Demo con la cuenta de prueba (el token se queda en user-secrets, fuera del repo)
dotnet user-secrets set "Loyverse:Token" "<token de la cuenta de prueba>" --project tests/integration/LoyOrder.Infrastructure.Loyverse.Demo
dotnet run --project tests/integration/LoyOrder.Infrastructure.Loyverse.Demo
```

Las pruebas son una consola para no depender de NuGet. Si el repo ya usa xUnit, cada método `Prueba_*` se convierte en un `[Fact]` sin cambios de lógica.

## Pendiente de verificar con Postman

Estos campos y parámetros salen de la documentación, no de las pruebas de la actividad 2.5. Están marcados en `Modelos.cs`:

- `category_id`
- `track_stock`
- `option1_value`
- `show_deleted`
- `store_id` dentro de `inventory_levels`
