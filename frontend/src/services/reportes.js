import { peticionAutenticada } from '../auth.js';

const ruta = ({ tipo, desde, hasta }, formato) => {
  const consulta = new URLSearchParams({ desde, hasta });
  return `/admin/reportes/${tipo}${formato ? `/descarga/${formato}` : ''}?${consulta}`;
};

export const consultarReporte = filtros => peticionAutenticada(ruta(filtros), null, 'GET');
export const descargarReporte = (filtros, formato) =>
  peticionAutenticada(ruta(filtros, formato), null, 'GET', true, 'blob');
