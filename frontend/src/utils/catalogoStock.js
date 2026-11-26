import { CANTIDAD_MAXIMA, crearItemCarrito, esItemValido, mismaLinea } from './carrito.js';

/** Compara líneas históricas/actuales con una respuesta NUEVA de GET /catalogo. */
export function revisarLineasConCatalogo(lineas, catalogo, carritoActual = []) {
  const productos = (catalogo?.categorias ?? []).flatMap(c => c.productos ?? []);
  const disponibles = [];
  const incidencias = [];
  for (const original of lineas ?? []) {
    const nombre = original?.name || `Producto ${original?.dishId ?? 'desconocido'}`;
    if (!esItemValido(original)) {
      incidencias.push({ tipo: 'formato', mensaje: `${nombre}: este pedido antiguo no tiene una variante identificable.` });
      continue;
    }
    const producto = productos.find(p => p.id === original.dishId);
    const variante = producto?.variantes?.find(v => v.id === original.variantId);
    if (!producto || !variante || producto.agotado || variante.agotado) {
      incidencias.push({ tipo: 'agotado', mensaje: `${nombre} (${original.variant || 'única'}): ya no está disponible.` });
      continue;
    }
    const actual = crearItemCarrito(producto, variante, original.quantity);
    if (!actual) {
      incidencias.push({ tipo: 'invalido', mensaje: `${nombre}: cantidad o datos del catálogo inválidos.` });
      continue;
    }
    const yaIncluido = [...carritoActual, ...disponibles]
      .filter(item => mismaLinea(item, actual))
      .reduce((total, item) => total + item.quantity, 0);
    if (yaIncluido + actual.quantity > CANTIDAD_MAXIMA) {
      incidencias.push({ tipo: 'limite', mensaje: `${nombre}: se superaría el límite de ${CANTIDAD_MAXIMA} unidades por variante.` });
      continue;
    }
    if (Math.round(original.price * 100) !== Math.round(actual.price * 100)) {
      incidencias.push({ tipo: 'precio', mensaje: `${nombre}: el precio cambió de $${original.price.toFixed(2)} a $${actual.price.toFixed(2)}.` });
    }
    if (original.name !== actual.name || original.variant !== actual.variant) {
      incidencias.push({ tipo: 'nombre', mensaje: `${nombre}: el nombre o la variante cambió en el catálogo.` });
    }
    disponibles.push(actual);
  }
  return { disponibles, incidencias, hayCambios: incidencias.length > 0 };
}

/** La respuesta no informa unidades disponibles: solo agotado/no agotado. */
export function verificarCheckoutConCatalogo(carrito, catalogo) {
  const resultado = revisarLineasConCatalogo(carrito, catalogo);
  return {
    ...resultado,
    valido: resultado.disponibles.length === carrito.length && !resultado.hayCambios,
  };
}
