import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizarCatalogo, obtenerCatalogo } from '../src/services/catalogo.js';
import { horaActualizacion } from '../src/utils/formato.js';

// 7.2.4 · Catálogo por categorías: respuesta de GET /api/v1/catalogo como la arma la API.
const respuesta = () => ({
  actualizadoEn: '2026-11-25T18:04:00+00:00',
  categorias: [
    { id: 1, nombre: 'Alitas', productos: [{ id: 10, nombre: 'Alitas BBQ', descripcion: 'Con salsa BBQ', imagenUrl: 'https://img.loyverse.com/a.png', precioDesde: 95, agotado: false,
      variantes: [{ id: 100, nombre: '6 piezas', precio: 95, agotado: false }, { id: 101, nombre: '10 piezas', precio: 150, agotado: false }] }] },
    { id: 2, nombre: 'Bebidas', productos: [
      { id: 20, nombre: 'Refresco', descripcion: null, imagenUrl: null, precioDesde: 25, agotado: false,
        variantes: [{ id: 200, nombre: 'Cola', precio: 25, agotado: false }, { id: 201, nombre: 'Lima-limón', precio: 22, agotado: true }] },
      { id: 21, nombre: 'Agua', descripcion: null, imagenUrl: null, precioDesde: 18, agotado: false, variantes: [{ id: 210, nombre: null, precio: 18, agotado: false }] }] },
  ],
});

test('normaliza categorías, productos y variantes del catálogo', () => {
  const menu = normalizarCatalogo(respuesta());
  assert.equal(menu.actualizadoEn, '2026-11-25T18:04:00+00:00');
  assert.deepEqual(menu.categorias.map(c => c.nombre), ['Alitas', 'Bebidas']);
  const alitas = menu.categorias[0].productos[0];
  assert.equal(alitas.categoria, 'Alitas');
  assert.equal(alitas.precioVariable, true);
  assert.equal(alitas.imagenUrl, 'https://img.loyverse.com/a.png');
  const refresco = menu.categorias[1].productos[0];
  assert.equal(refresco.precioDesde, 25, 'el precio desde ignora la variante agotada');
  assert.equal(refresco.agotado, false);
  assert.equal(refresco.variantes[1].agotado, true);
  assert.equal(menu.categorias[1].productos[1].variantes[0].nombre, null, 'artículo sin opciones');
});

test('descarta datos inválidos y URLs de imagen que no son http(s)', () => {
  const datos = respuesta();
  datos.categorias[0].productos[0].imagenUrl = 'javascript:alert(1)';
  datos.categorias[1].productos[0].variantes = [{ id: 200, nombre: 'Cola', precio: 0 }, { id: 'x', nombre: 'Mala', precio: 10 }];
  datos.categorias.push({ id: 3, nombre: 'Vacía', productos: [] }, { nombre: 'Sin id', productos: datos.categorias[0].productos });
  const menu = normalizarCatalogo(datos);
  assert.equal(menu.categorias[0].productos[0].imagenUrl, null);
  assert.deepEqual(menu.categorias[1].productos.map(p => p.nombre), ['Agua'], 'producto sin variantes vendibles fuera');
  assert.deepEqual(menu.categorias.map(c => c.nombre), ['Alitas', 'Bebidas']);
  assert.deepEqual(normalizarCatalogo(null), { actualizadoEn: null, categorias: [] });
});

test('producto con todas las variantes agotadas queda agotado', () => {
  const datos = respuesta();
  datos.categorias[1].productos[0].variantes[0].agotado = true;
  const refresco = normalizarCatalogo(datos).categorias[1].productos[0];
  assert.equal(refresco.agotado, true);
  assert.equal(refresco.precioDesde, 22);
});

test('consulta /catalogo sin idRestaurante y traduce los errores para el cliente', async () => {
  const rutas = [];
  const menu = await obtenerCatalogo(async ruta => { rutas.push(ruta); return respuesta(); });
  assert.deepEqual(rutas, ['/catalogo']);
  assert.equal(menu.categorias.length, 2);
  for (const [status, texto] of [[409, 'no está configurado'], [503, 'no está disponible'], [403, 'no puede']]) {
    await assert.rejects(obtenerCatalogo(async () => { throw Object.assign(new Error('interno'), { status }); }),
      error => error.status === status && error.message.includes(texto));
  }
  await assert.rejects(obtenerCatalogo(async () => { throw new Error('No se pudo conectar con la API.'); }), /No se pudo conectar/);
});

test('formatea la hora de la última sincronización', () => {
  const ahora = new Date('2026-11-25T20:00:00');
  assert.match(horaActualizacion('2026-11-25T18:04:00', ahora), /^hoy a las /);
  assert.match(horaActualizacion('2026-11-24T18:04:00', ahora), /24/);
  assert.equal(horaActualizacion(null, ahora), null);
  assert.equal(horaActualizacion('no es fecha', ahora), null);
});
