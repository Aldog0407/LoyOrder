// Token solo en memoria: recargar la página requiere iniciar sesión otra vez.
const base = 'http://localhost:5080/api/v1';
let token = null;
let usuario = null;
let temporizador;
let alVencer = () => {};
export const actual = () => usuario;
export function cuandoVenza(fn) { alVencer = fn; }
async function peticion(ruta, datos, metodo = 'POST') {
  let respuesta;
  try {
    respuesta = await fetch(base + ruta, {
      method: metodo,
      headers: { ...(datos ? {'Content-Type':'application/json'} : {}), ...(token ? {Authorization:'Bearer '+token} : {}) },
      ...(datos ? {body:JSON.stringify(datos)} : {})
    });
  } catch { throw new Error('No se pudo conectar con la API. Comprueba que esté encendida en el puerto 5080.'); }
  const cuerpo = await respuesta.json().catch(() => null);
  if (!respuesta.ok) {
    const detalle = cuerpo?.errors ? Object.values(cuerpo.errors).flat().join(' ') : cuerpo?.title;
    const error = new Error(respuesta.status === 429 ? 'Demasiados intentos. Espera un minuto.' : detalle || 'No se pudo completar la solicitud.'); error.status = respuesta.status; throw error;
  }
  return cuerpo;
}
export async function registrar(datos) { return peticion('/auth/registro', datos); }
export async function iniciar(datos) {
  const sesion = await peticion('/auth/login', datos);
  token = sesion.accessToken;
  try { usuario = await peticion('/auth/me', null, 'GET'); }
  catch (error) { token = null; usuario = null; throw error; }
  clearTimeout(temporizador);
  temporizador = setTimeout(() => { token=null;usuario=null;alVencer(); }, Math.max(0, Date.parse(sesion.expiresAt)-Date.now()));
  return usuario;
}
export async function cerrar() {
  try { await peticion('/auth/logout'); } catch (error) { if (error.status !== 401) throw error; }
  clearTimeout(temporizador);token=null;usuario=null;
}
