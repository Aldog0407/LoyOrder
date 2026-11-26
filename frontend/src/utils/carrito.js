// Artículos del carrito a partir del catálogo real (7.2.4). Cada línea guarda una copia de lo que el cliente vio
// (producto, variante, nombre y precio) para no depender de los datos de demostración. El precio y las existencias
// definitivos se validan al confirmar el pedido contra la API (7.2.6).
export const CANTIDAD_MAXIMA = 20;

const idValido = valor => Number.isSafeInteger(valor) && valor > 0;

export function esItemValido(item) {
  return !!item && idValido(item.dishId) && idValido(item.variantId) &&
    typeof item.name === 'string' && item.name.trim() !== '' && typeof item.variant === 'string' &&
    Number.isFinite(item.price) && item.price > 0 &&
    Number.isSafeInteger(item.quantity) && item.quantity >= 1 && item.quantity <= CANTIDAD_MAXIMA;
}

export const mismaLinea = (a, b) => a.dishId === b.dishId && a.variantId === b.variantId;

/** Línea del carrito para un producto y variante del catálogo; null si está agotado o los datos no son válidos. */
export function crearItemCarrito(producto, variante, cantidad) {
  if (!producto || producto.agotado || !variante || variante.agotado) return null;
  if (!producto.variantes?.some(v => v.id === variante.id)) return null;
  if (!Number.isSafeInteger(cantidad) || cantidad < 1 || cantidad > CANTIDAD_MAXIMA) return null;
  const item = {
    dishId: producto.id,
    variantId: variante.id,
    name: producto.nombre,
    variant: variante.nombre ?? '',
    image: producto.imagenUrl ?? null,
    price: variante.precio,
    quantity: cantidad,
  };
  return esItemValido(item) ? item : null;
}
