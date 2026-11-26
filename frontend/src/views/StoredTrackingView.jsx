import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { peticionAutenticada } from '../auth.js';
import { money } from '../data/menu.js';

export default function StoredTrackingView() {
  const { folio } = useParams();
  const navigate = useNavigate();
  const [pedido, setPedido] = useState(null);
  const [error, setError] = useState('');
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!folio || enviando) return;
    let activa = true;
    async function consultar() {
      try {
        const resultado = await peticionAutenticada(`/pedidos/${encodeURIComponent(folio)}`, null, 'GET', false);
        if (activa) { setPedido(resultado); setError(''); }
      } catch (fallo) { if (activa) setError(fallo.message); }
    }
    void consultar();
    const timer = setInterval(() => { if (document.visibilityState === 'visible') void consultar(); }, 15000);
    return () => { activa = false; clearInterval(timer); };
  }, [folio, enviando]);

  function buscar(event) {
    event.preventDefault();
    const valor = new FormData(event.currentTarget).get('folio').trim();
    navigate(`/seguimiento/${encodeURIComponent(valor)}`);
  }

  async function calificar(event) {
    event.preventDefault();
    if (enviando) return;
    const datos = Object.fromEntries(new FormData(event.currentTarget));
    setEnviando(true);
    setError('');
    try {
      await peticionAutenticada(`/pedidos/${encodeURIComponent(folio)}/calificacion`, {
        calificacion: Number(datos.calificacion), comentario: datos.comentario.trim() || null
      });
      setPedido(actual => ({ ...actual, calificacion: Number(datos.calificacion), comentario: datos.comentario.trim() || null }));
    } catch (fallo) { setError(fallo.message); }
    finally { setEnviando(false); }
  }

  if (!folio) return <main className="container auth-wrap"><section className="auth-card">
    <h1>Consulta tu pedido</h1><p>Escribe el folio de un pedido registrado con tu cuenta.</p>
    <form onSubmit={buscar}><label>Folio<input name="folio" required maxLength={30}/></label>
      <button className="button primary full">Consultar pedido</button></form>
    <Link className="button subtle full" to="/seguimiento">Volver a mi pedido</Link>
  </section></main>;

  return <main className="container page">
    <div className="page-heading"><span className="eyebrow orange">TU PEDIDO</span><h1>Seguimiento de pedido</h1><p>Folio {folio}</p></div>
    {error && <p role="alert">{error}</p>}
    {!pedido && !error && <p role="status">Consultando tu pedido…</p>}
    {pedido && <div className="tracking-layout"><section className="surface tracking-card">
      <h2>{pedido.estado}</h2><p>{pedido.tipoEntrega}</p>
      <p>Creado el {new Date(pedido.fechaCreacion).toLocaleString('es-MX')}</p>
      {pedido.fechaEntrega && <p>Entregado el {new Date(pedido.fechaEntrega).toLocaleString('es-MX')}</p>}
      <h3>Historial de estados</h3>
      {pedido.historial.length ? <ol>{pedido.historial.map((cambio, indice) => <li key={indice}>
        <strong>{cambio.estadoNuevo}</strong> · {new Date(cambio.fechaCambio).toLocaleString('es-MX')}
        {cambio.motivo && <p>{cambio.motivo}</p>}
      </li>)}</ol> : <p>Sin cambios adicionales registrados.</p>}
      <p className="muted">Se consulta cada 15 segundos mientras esta pestaña está visible.</p>
      {pedido.calificacion != null ? <section className="pedido-rating-result" role="status" aria-label="Calificación registrada">
        <div className="pedido-rating-score"><span aria-hidden="true">★</span><strong>Tu calificación es {pedido.calificacion} de 5.</strong></div>
        {pedido.comentario && <p>{pedido.comentario}</p>}
      </section> : pedido.estado === 'Entregado' && <form className="pedido-rating" onSubmit={calificar}>
        <fieldset className="pedido-rating-fields" disabled={enviando}><legend>Califica tu pedido</legend>
          <label className="pedido-rating-field">Calificación<select name="calificacion" required defaultValue=""><option value="" disabled>Selecciona una calificación</option>{[1, 2, 3, 4, 5].map(valor => <option key={valor} value={valor}>{valor} de 5</option>)}</select></label>
          <label className="pedido-rating-field">Comentario (opcional)<textarea name="comentario" rows={4} maxLength={2000} placeholder="Cuéntanos cómo fue tu pedido…"/></label>
          <button className="button primary" disabled={enviando}>{enviando ? 'Guardando…' : 'Enviar calificación'}</button>
        </fieldset></form>}
    </section><aside className="surface summary-card"><h2>Resumen</h2>
      <div className="summary-items">{pedido.articulos.map((articulo, indice) => <div key={indice}>
        <span>{articulo.cantidad} × {articulo.nombreProducto}<small>{articulo.indicaciones}</small></span><b>{money(articulo.subtotal)}</b>
      </div>)}</div>
      <p>Subtotal {money(pedido.subtotal)}</p><p>Envío {money(pedido.costoEnvio)}</p><strong>Total {money(pedido.total)}</strong>
    </aside></div>}
    <Link className="button subtle" to="/consultar-pedido">Consultar otro folio</Link>
  </main>;
}
