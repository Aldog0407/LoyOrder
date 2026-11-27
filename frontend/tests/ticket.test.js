import test from 'node:test';
import assert from 'node:assert/strict';
import { avisoTicket, reimprimirTicket } from '../src/services/ticket.js';

// 7.2.11 · CU-19 ticket de venta desde el panel del operador.
test('aviso del ticket solo cuando el cambio de estado despacha', () => {
  assert.equal(avisoTicket({ folio: 'LY-1', estado: 'En preparación' }), '');
  assert.equal(avisoTicket({ folio: 'LY-1', estado: 'Enviado', ticket: { impreso: true, impresora: 'Caja' } }), 'Ticket impreso en Caja.');
  assert.match(avisoTicket({ ticket: { impreso: false, aviso: 'La impresora Caja no respondió.' } }), /no respondió/);
  assert.match(avisoTicket({ ticket: { impreso: false } }), /no se pudo imprimir/);
});

test('reimprimir manda la marca de reimpresión al folio correcto', async () => {
  const llamadas = [];
  const previo = globalThis.fetch;
  globalThis.fetch = async (url, opciones) => { llamadas.push({ url, cuerpo: opciones.body }); return new Response('{}', { status: 200 }); };
  try { await assert.rejects(reimprimirTicket('LY 1/2'), /Inicia sesión/); }
  finally { globalThis.fetch = previo; }
  assert.equal(llamadas.length, 0, 'sin sesión no se llama a la API');
});
