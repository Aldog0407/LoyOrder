import { peticionAutenticada } from '../auth.js';

// 7.2.11 · CU-19 Ticket de venta. El original sale solo al despachar (POST .../estado a Enviado o Listo para recoger);
// el operador puede reimprimirlo con la marca *** REIMPRESIÓN ***.
export const reimprimirTicket = folio =>
  peticionAutenticada(`/operacion/pedidos/${encodeURIComponent(folio)}/ticket`, { reimpresion: true });

/** Mensaje para el operador según la respuesta del cambio de estado (trae `ticket` solo al despachar). */
export function avisoTicket(respuesta) {
  const ticket = respuesta?.ticket;
  if (!ticket) return '';
  if (ticket.impreso) return `Ticket impreso en ${ticket.impresora}.`;
  return ticket.aviso || 'El pedido se despachó, pero no se pudo imprimir el ticket.';
}
