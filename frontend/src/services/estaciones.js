import { peticionAutenticada } from '../auth.js';

export const consultarEstaciones = () => peticionAutenticada('/estaciones', null, 'GET');
export const guardarImpresora = (id, datos) => peticionAutenticada(`/estaciones/impresoras${id ? `/${id}/editar` : ''}`, datos);
export const guardarEstacion = (id, datos) => peticionAutenticada(`/estaciones${id ? `/${id}/editar` : ''}`, datos);
export const asignarEstacion = (id, datos) => peticionAutenticada(`/estaciones/${id}/asignaciones`, datos);
export const retirarAsignacion = (id, asignacion) => peticionAutenticada(`/estaciones/${id}/asignaciones/${asignacion}/retirar`, null);
