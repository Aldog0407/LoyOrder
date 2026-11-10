import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rutaInicial, destinoProtegido } from '../src/utils/auth-routing.js';
import { cumpleContrasena } from '../src/politica-contrasena.js';

test('visitante necesita sesión antes de acceder a cualquier rol', () => {
  for (const rol of ['CLIENTE', 'OPERADOR', 'ADMINISTRADOR']) assert.equal(destinoProtegido(null, [rol]), '/login');
});
test('cada rol solo puede abrir su área, sin herencia administrativa', () => {
  const rutas = { CLIENTE: '/menu', OPERADOR: '/operacion', ADMINISTRADOR: '/admin' };
  for (const [rol, ruta] of Object.entries(rutas)) {
    assert.equal(rutaInicial({ rol }), ruta);
    for (const requerido of Object.keys(rutas)) assert.equal(destinoProtegido({ rol }, [requerido]), rol === requerido ? null : ruta);
  }
});
test('registro conserva longitud y símbolo, los espacios no cuentan como símbolo', () => {
  for (const [clave, valido] of [['abcdefgh', false], ['abcd1234', false], ['abcd 1234', false], ['abcdef!', false], ['abcdefg!', true], ['a'.repeat(127)+'!', true], ['a'.repeat(128)+'!', false], ['abcdefgh★', true], ['abcdefgh😀', true]]) assert.equal(cumpleContrasena(clave), valido);
});
test('login usa el token para consultar identidad y logout limpia la sesión', async t => {
  const auth = await import('../src/auth.js?caso=login');
  const requests = [];
  t.mock.method(globalThis, 'fetch', async (url, init) => {
    requests.push({url,init});
    if (url.endsWith('/login')) return Response.json({accessToken:'token-de-prueba',expiresAt:new Date(Date.now()+60000).toISOString()});
    if (url.endsWith('/me')) return Response.json({idUsuario:1,nombre:'Cliente',rol:'CLIENTE'});
    return new Response(null,{status:204});
  });
  try {
    assert.equal((await auth.iniciar({correo:'cliente@example.com',contrasena:'Ejemplo123!'})).rol, 'CLIENTE');
    assert.equal(requests[1].init.headers.Authorization, 'Bearer token-de-prueba');
    await auth.cerrar();
    assert.equal(requests[2].url.endsWith('/logout'),true);
    assert.equal(auth.actual(), null);
  } finally { await auth.cerrar(); }
});
test('identidad rechazada no deja una sesión iniciada', async t => {
  const auth = await import('../src/auth.js?caso=identidad');
  t.mock.method(globalThis,'fetch',async url => url.endsWith('/login') ? Response.json({accessToken:'token',expiresAt:new Date(Date.now()+60000).toISOString()}) : Response.json({title:'Sesión inválida'},{status:401}));
  await assert.rejects(auth.iniciar({correo:'cliente@example.com',contrasena:'Ejemplo123!'}), /Sesión inválida/);
  assert.equal(auth.actual(),null);
});
test('sesión vencida avisa a React y elimina la identidad', async t => {
  const auth = await import('../src/auth.js?caso=expiracion');
  let expiracion;
  t.mock.method(globalThis,'setTimeout', fn => { expiracion=fn;return 1; });
  t.mock.method(globalThis,'clearTimeout', () => {});
  t.mock.method(globalThis,'fetch', async url => url.endsWith('/login') ? Response.json({accessToken:'token',expiresAt:new Date(Date.now()+1000).toISOString()}) : Response.json({rol:'CLIENTE'}));
  let avisos=0;
  auth.cuandoVenza(()=>avisos++);
  await auth.iniciar({correo:'cliente@example.com',contrasena:'Ejemplo123!'});
  expiracion();
  assert.equal(avisos,1);
  assert.equal(auth.actual(),null);
});
test('fallo de red se muestra como error de conexión', async t => {
  const auth = await import('../src/auth.js?caso=red');
  t.mock.method(globalThis,'fetch',async ()=>{throw new TypeError('red');});
  await assert.rejects(auth.registrar({}),/No se pudo conectar con la API/);
});
test('logout 401 limpia un token ya inválido', async t => {
  const auth = await import('../src/auth.js?caso=logout401');
  t.mock.method(globalThis,'fetch', async url => {
    if(url.endsWith('/login')) return Response.json({accessToken:'token',expiresAt:new Date(Date.now()+60000).toISOString()});
    if(url.endsWith('/me')) return Response.json({rol:'CLIENTE'});
    return Response.json({title:'Sesión inválida'},{status:401});
  });
  try {await auth.iniciar({correo:'cliente@example.com',contrasena:'Ejemplo123!'}); await auth.cerrar(); assert.equal(auth.actual(),null);}
  finally {await auth.cerrar();}
});

test('el vencimiento del JWT no termina la sesión activa y una interacción renueva una sola vez', async t => {
  const auth = await import('../src/auth.js?caso=renovacion');
  let ahora = Date.now();
  const fecha = minutos => new Date(ahora + minutos * 60000).toISOString();
  const inicial = { accessToken: 'acceso-1', refreshToken: 'renovacion-1',
    expiresAt: fecha(15), idleExpiresAt: fecha(30), sessionExpiresAt: fecha(480) };
  const temporizadores = [];
  t.mock.method(Date, 'now', () => ahora);
  t.mock.method(globalThis, 'setTimeout', (fn, espera) => {
    temporizadores.push({ fn, espera });
    return temporizadores.length;
  });
  t.mock.method(globalThis, 'clearTimeout', () => {});
  let renovaciones = 0;
  let cerrarCon;
  t.mock.method(globalThis, 'fetch', async (url, init) => {
    if (url.endsWith('/login')) return Response.json(inicial);
    if (url.endsWith('/me')) return Response.json({ idUsuario: 3, rol: 'ADMINISTRADOR' });
    if (url.endsWith('/renovar')) {
      assert.equal(JSON.parse(init.body).refreshToken, 'renovacion-1');
      renovaciones++;
      return Response.json({ ...inicial, accessToken: 'acceso-2', refreshToken: 'renovacion-2',
        expiresAt: fecha(15), idleExpiresAt: fecha(30) });
    }
    cerrarCon = JSON.parse(init.body).refreshToken;
    return new Response(null, { status: 204 });
  });
  await auth.iniciar({ correo: 'admin@example.test', contrasena: 'Ejemplo123!' });
  assert.equal(temporizadores.at(-1).espera, 30 * 60000);
  ahora += 16 * 60000;
  auth.registrarActividad();
  auth.registrarActividad();
  await auth.cerrar();
  assert.equal(renovaciones, 1);
  assert.equal(cerrarCon, 'renovacion-2');
  assert.equal(auth.actual(), null);
});
