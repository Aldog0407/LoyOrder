# Actividad 7.2.4 — Catálogo por categorías (req. 8, CU-03 pasos 1 y 2)

Entregable: pantalla del catálogo por categorías.

## Qué hace

La pantalla `/menu` del cliente ya no usa los datos de demostración. Muestra el catálogo que la sincronización (7.2.3) copió de Loyverse a `categoria`, `producto` y `variante_producto`. El navegador nunca habla con Loyverse ni conoce su token. Solo llama a la API de LoyOrder.

- Botones de categoría ("Todos" y una por cada categoría con productos vendibles).
- Tarjeta por producto con su imagen de Loyverse (o un marcador si no tiene), su descripción, su precio ("Desde $X" si las variantes tienen precios distintos) y la etiqueta "Agotado".
- Detalle del producto con sus variantes, el precio de cada una y las agotadas deshabilitadas. Si el artículo tiene una sola variante disponible, se elige sola.
- Leyenda con la hora de la última sincronización de inventario ("Menú actualizado hoy a las 18:04").
- Estados de carga, de error con botón "Reintentar" y de menú vacío ("El restaurante todavía no sincroniza su catálogo").

## Reglas del catálogo

| Situación | Qué ve el cliente |
| --- | --- |
| Categoría con `activo = false` | No aparece. |
| Categoría sin productos vendibles | No aparece. |
| Producto con `disponible = false` (eliminado o no disponible en la tienda; por ejemplo la hawaiana) | No aparece. |
| Variante con `disponible = false` o precio 0 (precio variable en Loyverse) | No aparece. |
| Variante que controla inventario con 0 existencias o menos (por ejemplo el refresco lima-limón) | Aparece como "Agotado" y no se puede agregar al carrito. |
| Todas las variantes de un producto agotadas | La tarjeta aparece como "Agotado" y el botón queda deshabilitado. |
| Existencias desconocidas (`null`, el inventario aún no se sincroniza) | No se marca como agotado. La validación definitiva es al confirmar el pedido contra Loyverse (7.2.6, CU-03 RN-04). |
| Categoría "Sin categoría" | Se muestra al final. |
| Imagen que no es `http://` o `https://`, o que no carga | Se muestra el marcador. |

El precio "desde" solo considera las variantes no agotadas.

## Endpoint (solo CLIENTE)

| Método y ruta | Respuesta |
| --- | --- |
| `GET /api/v1/catalogo` | 200 con `{ actualizadoEn, categorias: [{ id, nombre, productos: [{ id, nombre, descripcion, imagenUrl, precioDesde, agotado, variantes: [{ id, nombre, precio, agotado }] }] }] }`. |

- **No recibe `idRestaurante`.** La API lo resuelve igual que la sincronización: `Sincronizacion:IdRestaurante` si está configurado y, si no, el primer restaurante registrado. Funciona igual con la base de demostración (restaurante 1) que con los datos semilla de Gabriel (restaurante 9001). Si alguien manda `?idRestaurante=` en la URL, se ignora.
- **`actualizadoEn`** es el inicio de la última sincronización de inventario correcta; `null` si nunca se ha sincronizado.
- **Variante sin nombre:** `nombre` es `null` cuando el artículo de Loyverse no tiene opciones.

Códigos de error (ProblemDetails):

| Código | Cuándo |
| --- | --- |
| 401 | Sin sesión. |
| 403 | Operador o administrador. |
| 409 | No hay restaurante registrado, o `Sincronizacion:IdRestaurante` apunta a uno que no existe. Se eligió 409 en lugar de lista vacía para distinguir "el sistema no está configurado" de "el restaurante aún no sincroniza" (que es 200 con `categorias: []`). Es el mismo error que ya da la sincronización. |
| 503 | Sin base de datos. |

`GET /api/v1/categorias` sigue existiendo sin cambios.

## Cambios en el carrito (7.2.5 de Madeline)

Eran indispensables para poder agregar los productos reales. El carrito validaba cada artículo contra los datos de demostración con `getDish()`, así que habría rechazado todo lo del catálogo real.

- Cada línea del carrito guarda una copia de lo que vio el cliente: `dishId` (id del producto), `variantId`, `name`, `variant`, `image`, `price` y `quantity`. La arma `crearItemCarrito()` en `frontend/src/utils/carrito.js`, que rechaza variantes agotadas, variantes de otro producto y cantidades fuera de 1 a 20.
- `ADD_ITEM`, `REORDER` y `validateCheckout()` validan con `esItemValido()` en lugar de `getDish()`. Dos líneas son la misma si coinciden producto y variante.
- Al cargar el carrito guardado en el navegador se descartan las líneas con el formato de demostración (sin `variantId`). La configuración y los pedidos guardados no se tocan.
- El historial y el seguimiento de demostración muestran `item.name` si existe. Siguen con datos de demostración hasta la 7.2.14 y la 7.2.10.
- Se ajustaron los datos de 2 pruebas existentes (`checkout.test.js` y `address.test.js`) al formato nuevo de la línea del carrito.

## Archivos

| Capa | Archivo |
| --- | --- |
| Application | `MenuCliente.cs`: `ConsultarMenu`, `IMenuRepository` y las reglas de agotado y agrupación. Reutiliza `ICatalogoRepository.RestauranteAsync` y `UltimaCorrectaAsync` de la 7.2.3. |
| Persistence | `MenuRepository.cs`: una consulta parametrizada que une `categoria`, `producto` y `variante_producto`. |
| API | `CatalogoController.cs` y el registro en `ApiHost.cs`. |
| Frontend | `services/catalogo.js` (usa `peticionAutenticada` de `auth.js`), `utils/carrito.js`, `utils/formato.js`, `components/ImagenPlatillo.jsx`, `MenuCard.jsx`, `DishDetail.jsx`, `CartFlow.jsx`, `views/MenuView.jsx` y estilos al final de `styles.css`. |

## Configuración y base de datos

No hay configuración nueva. Usa `Sincronizacion:IdRestaurante` de la 7.2.3 (opcional).

No hay migración: `loyorder_api` ya tenía SELECT sobre todas las tablas (`GRANT SELECT ON ALL TABLES` en `LoyOrderObjetos_y_Permisos.sql`). La prueba de Persistence lo comprueba.

## Cómo probarlo en una laptop

1. Tener la base con el catálogo sincronizado (pasos de `docs/ACTIVIDAD_7_2_3.md`, con el token de la **cuenta de prueba** de Loyverse).
2. Arrancar la API como en `docs/ACTIVIDAD_7_2_1.md`, con `ConnectionStrings__LoyOrder` apuntando a la base local. Si la base tiene varios restaurantes, agregar `$env:Sincronizacion__IdRestaurante = "<id>"`.
3. Arrancar el frontend: `cd frontend; npm ci; npm run dev`.
4. Iniciar sesión como cliente en http://localhost:5173/login. Debe abrir `/menu` con las 5 categorías de la cuenta de prueba (Alitas, Hamburguesas, Papas y complementos, Bebidas, Postres).
5. Comprobar que:
   - La hawaiana **no** aparece.
   - En el refresco, la variante lima-limón aparece como "Agotado" y no se puede elegir.
   - Al agregar una variante, el carrito muestra el nombre, la variante y el precio de Loyverse.
6. En el Back Office de la cuenta de prueba, cambiar un precio, esperar la sincronización (2 min por defecto) y presionar "Reintentar" o recargar la página.

Para las capturas de evidencia, tapen tokens, contraseñas y datos personales.

## Pruebas

```powershell
dotnet build LoyOrder.sln
dotnet run --project tests\unit\LoyOrder.Application.Tests
dotnet run --project tests\unit\LoyOrder.Infrastructure.Loyverse.Tests
dotnet run --project tests\integration\LoyOrder.Api.Tests
$env:LOYORDER_PRUEBAS_ADMIN = "Host=localhost;Username=postgres;Password=<tu contraseña>"
dotnet run --project tests\integration\LoyOrder.Persistence.Tests
cd frontend; npm test; npm run lint; npm run build
```

| Proyecto | Pruebas | Qué se agregó |
| --- | ---: | --- |
| `LoyOrder.Application.Tests` | 26 | 7: agrupación y orden, restaurante configurado o el primero (y 409 sin restaurante), variante agotada, producto agotado, existencias desconocidas, variante sin nombre y precio 0, fecha de actualización. |
| `LoyOrder.Infrastructure.Loyverse.Tests` | 24 | Sin cambios. |
| `LoyOrder.Api.Tests` | 89 | 10 del catálogo: 200 con variantes agotadas, que se ignora `idRestaurante` de la URL, 503, 500 sin detalles internos, 409 sin restaurante, 401 y 403 para administrador y operador. Las otras 12 nuevas son del seguimiento de pedidos de Aldo. |
| `LoyOrder.Persistence.Tests` | 6 | 1 con PostgreSQL real: orden de categorías con "Sin categoría" al final, categoría inactiva oculta, hawaiana oculta, precio 0 oculto, lima-limón agotada, aislamiento entre restaurantes, restaurante inexistente y SELECT de `loyorder_api`. |
| Frontend (`npm test`) | 33 | 8: `catalogo.test.js` (normalización, datos inválidos, URL de imagen insegura, producto agotado, errores 403, 409 y 503, hora de actualización) y `carrito.test.js` (creación de la línea, agotados, cantidades y líneas de demostración). |

Ninguna prueba usa la cuenta real de Loyverse ni necesita token.

## Límites de esta entrega

- **Existencias al agregar al carrito:** se valida con la última sincronización (cada 2 min por defecto). La consulta en vivo a Loyverse al confirmar el pedido es de la 7.2.6.
- **"Repetir pedido":** solo comprueba que la línea tenga el formato correcto, no que el producto siga disponible ni que conserve el precio. Eso es de la 7.2.14.
- **Calificación:** se quitó la ★ de las tarjetas porque era un dato de demostración. Las calificaciones reales son de la 7.2.18.
- **Paginación:** el menú se descarga completo en una sola petición, sin paginar. La cuenta de prueba tiene 17 artículos y 22 variantes.
- **Imágenes:** se cargan directo desde el servidor de imágenes de Loyverse.
