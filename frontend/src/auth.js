// Las credenciales viven en memoria de esta pestaña.
import { peticion } from './services/authApi.js';
let sesion = null;
let usuario = null;
let temporizador;
let aviso;
let renovacion;
let cerrando = false;
let ultimoIntento = 0;
let alVencer = () => {};
let alAdvertir = () => {};

export const actual = () => usuario;
export function cuandoVenza(fn) { alVencer = fn; }
export function cuandoSeAcerqueElVencimiento(fn) { alAdvertir = fn; }

function finalizar(vencida) {
  const anterior = usuario;
  clearTimeout(temporizador);
  clearTimeout(aviso);
  sesion = null;
  usuario = null;
  alAdvertir(false);
  if (vencida && anterior) alVencer(anterior);
}

function programar(plazos) {
  clearTimeout(temporizador);
  clearTimeout(aviso);
  alAdvertir(false);
  const fin = Math.min(Date.parse(plazos.idleExpiresAt ?? plazos.expiresAt),
    Date.parse(plazos.sessionExpiresAt ?? plazos.expiresAt));
  const restante = Math.max(0, fin - Date.now());
  aviso = setTimeout(() => {
    if (sesion === plazos) alAdvertir(true);
  }, Math.max(0, restante - 120000));
  temporizador = setTimeout(() => {
    const cerrarSiNoRenovo = () => { if (sesion === plazos) finalizar(true); };
    if (renovacion) void renovacion.then(cerrarSiNoRenovo, cerrarSiNoRenovo);
    else cerrarSiNoRenovo();
  }, restante);
}

function renovar() {
  if (renovacion) return renovacion;
  const anterior = sesion;
  if (!anterior?.refreshToken) return Promise.resolve();
  ultimoIntento = Date.now();
  renovacion = peticion('/auth/renovar', { refreshToken: anterior.refreshToken })
    .then(nueva => {
      if (sesion !== anterior) return;
      sesion = nueva;
      programar(nueva);
    })
    .catch(error => {
      if (error.status === 401 && sesion === anterior) finalizar(true);
      throw error;
    })
    .finally(() => { renovacion = null; });
  return renovacion;
}

export function registrarActividad() {
  if (!sesion?.refreshToken || renovacion || cerrando) return;
  const ahora = Date.now();
  const tiempoDesdeIntento = ahora - ultimoIntento;
  const porVencer = Date.parse(sesion.expiresAt) - ahora <= 10000;
  if (tiempoDesdeIntento < 5000 || (tiempoDesdeIntento < 60000 && !porVencer)) return;
  // Un fallo temporal se puede volver a intentar cuando haya actividad real.
  void renovar().catch(() => {});
}

export async function registrar(datos) { return peticion('/auth/registro', datos); }

export async function iniciar(datos) {
  const nueva = await peticion('/auth/login', datos);
  const identidad = await peticion('/auth/me', null, 'GET', nueva.accessToken);
  sesion = nueva;
  usuario = identidad;
  ultimoIntento = Date.now();
  programar(nueva);
  return usuario;
}

export async function cerrar() {
  if (!sesion) return;
  cerrando = true;
  try {
    if (renovacion) await renovacion.catch(() => {});
    if (sesion?.refreshToken)
      await peticion('/auth/logout', { refreshToken: sesion.refreshToken });
    else if (sesion)
      await peticion('/auth/logout', null, 'POST', sesion.accessToken);
  } catch (error) {
    if (error.status !== 401) throw error;
  } finally {
    cerrando = false;
  }
  finalizar(false);
}


export async function peticionAutenticada(ruta, datos, metodo = 'POST', renovarSiHaceFalta = true, tipoRespuesta = 'json') {
  if (!sesion || cerrando) throw new Error('Inicia sesión para continuar.');
  if (renovacion) await renovacion;
  if (!sesion || cerrando) throw new Error('Inicia sesión para continuar.');
  if (Date.parse(sesion.expiresAt) - Date.now() < 15000) {
    if (!renovarSiHaceFalta) throw new Error('Interactúa con la página para actualizar el pedido.');
    await renovar();
  }
  if (!sesion || cerrando) throw new Error('Inicia sesión para continuar.');
  try {
    return await peticion(ruta, datos, metodo, sesion.accessToken, tipoRespuesta);
  } catch (error) {
    if (error.status === 401) finalizar(true);
    throw error;
  }
}
