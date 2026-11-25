import test from 'node:test';
import assert from 'node:assert/strict';
import { CANTIDAD_MAXIMA, crearItemCarrito, esItemValido, mismaLinea } from '../src/utils/carrito.js';

// 7.2.4 · Líneas del carrito a partir del catálogo real.
const refresco = {
  id: 20, nombre: 'Refresco', imagenUrl: null, agotado: false,
  variantes: [{ id: 200, nombre: 'Cola', precio: 25, agotado: false }, { id: 201, nombre: 'Lima-limón', precio: 22, agotado: true }],
};

test('crea la línea con la copia del producto y la variante', () => {
  assert.deepEqual(crearItemCarrito(refresco, refresco.variantes[0], 2),
    { dishId: 20, variantId: 200, name: 'Refresco', variant: 'Cola', image: null, price: 25, quantity: 2 });
  const agua = { id: 21, nombre: 'Agua', agotado: false, variantes: [{ id: 210, nombre: null, precio: 18, agotado: false }] };
  assert.equal(crearItemCarrito(agua, agua.variantes[0], 1).variant, '', 'artículo sin opciones');
});

test('no deja agregar variantes agotadas, ajenas o cantidades fuera de rango', () => {
  assert.equal(crearItemCarrito(refresco, refresco.variantes[1], 1), null, 'lima-limón agotada');
  assert.equal(crearItemCarrito({ ...refresco, agotado: true }, refresco.variantes[0], 1), null, 'producto agotado');
  assert.equal(crearItemCarrito(refresco, { id: 999, nombre: 'Otra', precio: 10, agotado: false }, 1), null, 'variante de otro producto');
  for (const cantidad of [0, -1, 1.5, CANTIDAD_MAXIMA + 1]) assert.equal(crearItemCarrito(refresco, refresco.variantes[0], cantidad), null);
});

test('valida líneas guardadas y descarta las del menú de demostración', () => {
  const linea = crearItemCarrito(refresco, refresco.variantes[0], 1);
  assert.equal(esItemValido(linea), true);
  assert.equal(esItemValido({ dishId: 1, variant: 'Clásica', quantity: 2, price: 149 }), false, 'formato de demostración');
  assert.equal(esItemValido({ ...linea, price: 0 }), false);
  assert.equal(esItemValido({ ...linea, name: ' ' }), false);
  assert.equal(mismaLinea(linea, { ...linea, quantity: 5 }), true);
  assert.equal(mismaLinea(linea, { ...linea, variantId: 201 }), false);
});
