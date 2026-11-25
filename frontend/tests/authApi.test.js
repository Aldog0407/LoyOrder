import { test } from 'node:test';
import assert from 'node:assert/strict';
import { authApi, ApiError } from '../src/services/authApi.js';

test('registro envía JSON camelCase y lee idDesafio', async () => {
  const previous = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    assert.equal(url, 'http://localhost:5080/api/v1/auth/registro');
    assert.deepEqual(JSON.parse(options.body), { nombre: 'Ana', correo: 'ana@example.com', contrasena: 'clave', telefono: '123' });
    return new Response(JSON.stringify({ idDesafio: 'abc', correoVerificado: false }), { status: 201 });
  };
  try { assert.equal((await authApi.register({ nombre: 'Ana', correo: 'ana@example.com', contrasena: 'clave', telefono: '123' })).idDesafio, 'abc'); }
  finally { globalThis.fetch = previous; }
});

test('login propaga 403 CORREO_NO_VERIFICADO y desafío', async () => {
  const previous = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify({ codigo: 'CORREO_NO_VERIFICADO', idDesafio: 'xyz' }), { status: 403 });
  try {
    await assert.rejects(authApi.login({ correo: 'ana@example.com', contrasena: 'clave' }), err => err instanceof ApiError && err.status === 403 && err.codigo === 'CORREO_NO_VERIFICADO' && err.idDesafio === 'xyz');
  } finally { globalThis.fetch = previous; }
});

test('confirmación acepta 204 sin cuerpo', async () => {
  const previous = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    assert.ok(url.endsWith('/correo/confirmar'));
    assert.deepEqual(JSON.parse(options.body), { idDesafio: 'xyz', codigo: '123456' });
    return new Response(null, { status: 204 });
  };
  try { assert.deepEqual(await authApi.confirmEmail({ idDesafio: 'xyz', codigo: '123456' }), {}); }
  finally { globalThis.fetch = previous; }
});

test('429 lee Retry-After', async () => {
  const previous = globalThis.fetch;
  globalThis.fetch = async () => new Response(JSON.stringify({ codigo: 'TOO_MANY_REQUESTS' }), { status: 429, headers: { 'Retry-After': '75' } });
  try { await assert.rejects(authApi.resendEmail({ idDesafio: 'xyz' }), err => err.status === 429 && err.retryAfter === 75); }
  finally { globalThis.fetch = previous; }
});


test('recuperación y restablecimiento conservan el contrato del backend', async t => {
  const solicitudes = [];
  t.mock.method(globalThis, 'fetch', async (url, opciones) => {
    solicitudes.push({ url, datos: JSON.parse(opciones.body) });
    return new Response(null, { status: 204 });
  });
  await authApi.requestPassword({ correo: 'cliente@example.test' });
  await authApi.resetPassword({ token: 'secreto', nuevaContrasena: 'Nueva123!' });
  assert.ok(solicitudes[0].url.endsWith('/contrasena/solicitar'));
  assert.deepEqual(solicitudes[0].datos, { correo: 'cliente@example.test' });
  assert.ok(solicitudes[1].url.endsWith('/contrasena/restablecer'));
  assert.deepEqual(solicitudes[1].datos, { token: 'secreto', nuevaContrasena: 'Nueva123!' });
});
