import { useEffect, useState } from 'react';
import './EstacionesConfig.css';
import Modal from './Modal.jsx';
import { consultarEstaciones, guardarImpresora, guardarEstacion, asignarEstacion, retirarAsignacion } from '../services/estaciones.js';

const ipv4Valida = valor => {
  const partes = valor.split('.');
  return partes.length === 4 && partes.every(p => /^(0|[1-9][0-9]{0,2})$/.test(p) && Number(p) <= 255);
};
const ipValida = valor => {
  if (!valor || valor.length > 45 || !/^[0-9a-fA-F:.]+$/.test(valor)) return false;
  if (!valor.includes(':')) return ipv4Valida(valor) && valor !== '0.0.0.0';
  if (valor.includes('.') && !ipv4Valida(valor.split(':').at(-1))) return false;
  try { return new URL(`http://[${valor}]/`).hostname !== '[::]'; }
  catch { return false; }
};
const nombreValido = valor => Boolean(valor.trim()) && valor.length <= 100;

const nuevaImpresora = () => ({ idImpresora: null, nombre: '', direccionIp: '', activa: false });
const nuevaEstacion = () => ({ idEstacion: null, nombre: '', idImpresora: '', activa: false });
const nuevaAsignacion = () => ({ estacion: '', tipo: 'categoria', elemento: '' });
const etiquetaVariante = v => `${v.producto}${v.variante ? ` · ${v.variante}` : ''}`;

export default function EstacionesConfig() {
  const [datos, setDatos] = useState(null);
  const [impresora, setImpresora] = useState(nuevaImpresora);
  const [estacion, setEstacion] = useState(nuevaEstacion);
  const [asignacion, setAsignacion] = useState(nuevaAsignacion);
  const [modal, setModal] = useState(null);
  const [ocupado, setOcupado] = useState(false);
  const [aviso, setAviso] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    let vigente = true;
    consultarEstaciones().then(resultado => { if (vigente) setDatos(resultado); })
      .catch(e => { if (vigente) setError(e.message); });
    return () => { vigente = false; };
  }, []);

  const ejecutar = async (operacion, mensaje, limpiar) => {
    setOcupado(true);
    setError('');
    setAviso('');
    try {
      if (operacion) {
        await operacion();
        limpiar?.();
        setModal(null);
        setAviso(mensaje);
      }
      setDatos(await consultarEstaciones());
    } catch (e) { setError(e.message); }
    finally { setOcupado(false); }
  };

  const enviarImpresora = e => {
    e.preventDefault();
    const { idImpresora, nombre, direccionIp, activa } = impresora;
    if (!nombreValido(nombre) || !ipValida(direccionIp)) {
      setAviso(''); setError('Revisa el nombre y la dirección IP antes de guardar.'); return;
    }
    if (datos.impresoras.some(i => i.idImpresora !== idImpresora && (i.nombre.trim().toLowerCase() === nombre.trim().toLowerCase() || i.direccionIp.toLowerCase() === direccionIp.toLowerCase()))) { setError('Ya existe una impresora con ese nombre o IP.'); return; }
    if (!activa && datos.estaciones.some(s => s.idImpresora === idImpresora && s.activa)) {
      setAviso(''); setError('Desactiva primero la estación asociada.'); return;
    }
    void ejecutar(() => guardarImpresora(idImpresora, { nombre, direccionIp, activa }), 'Impresora guardada.', () => setImpresora(nuevaImpresora()));
  };
  const enviarEstacion = e => {
    e.preventDefault();
    const { idEstacion, nombre, idImpresora, activa } = estacion;
    if (!nombreValido(nombre) || !idImpresora || (activa && !impresoraDe(Number(idImpresora))?.activa)) {
      setAviso(''); setError('Revisa el nombre y la impresora. Una estación activa necesita una impresora activa.'); return;
    }
    if (datos.estaciones.some(s => s.idEstacion !== idEstacion && s.nombre.trim().toLowerCase() === nombre.trim().toLowerCase())) { setError('Ya existe una estación con ese nombre.'); return; }
    void ejecutar(() => guardarEstacion(idEstacion, { nombre, idImpresora: Number(idImpresora), activa }), 'Estación guardada.', () => setEstacion(nuevaEstacion()));
  };
  const enviarAsignacion = e => {
    e.preventDefault();
    const { estacion: id, tipo, elemento } = asignacion;
    const previa = datos.asignaciones.find(a => tipo === 'categoria' ? a.idCategoria === Number(elemento) : a.idVariante === Number(elemento));
    if (previa && previa.idEstacion === Number(id)) { setError('El elemento ya pertenece a esta estación.'); return; }
    if (previa && !window.confirm('¿Cambiar la estación de este elemento?')) return;
    const solicitud = { reasignar: Boolean(previa), idCategoria: tipo === 'categoria' ? Number(elemento) : null, idVariante: tipo === 'variante' ? Number(elemento) : null };
    void ejecutar(() => asignarEstacion(Number(id), solicitud), 'Asignación guardada.', () => setAsignacion(a => ({ ...a, elemento: '' })));
  };
  const quitar = (id, elemento) => {
    if (window.confirm('¿Retirar esta asignación? No se eliminarán productos ni pedidos.'))
      void ejecutar(() => retirarAsignacion(id, elemento), 'Asignación retirada.');
  };
  const impresoraDe = id => datos?.impresoras.find(i => i.idImpresora === id);
  const nombreAsignacion = a => a.idCategoria != null
    ? `Categoría: ${datos.categorias.find(c => c.idCategoria === a.idCategoria)?.nombre ?? a.idCategoria}`
    : `Variante: ${datos.variantes.find(v => v.idVariante === a.idVariante) ? etiquetaVariante(datos.variantes.find(v => v.idVariante === a.idVariante)) : a.idVariante}`;
  const sinEstacion = datos?.variantes.filter(v => {
    if (!v.disponible) return false;
    const a = datos.asignaciones.find(a => a.idVariante === v.idVariante) ?? datos.asignaciones.find(a => a.idCategoria === v.idCategoria);
    return !datos.estaciones.some(s => s.idEstacion === a?.idEstacion && s.activa && impresoraDe(s.idImpresora)?.activa);
  }) ?? [];
  const impresorasLibres = datos?.impresoras.filter(i => !datos.estaciones.some(s => s.idImpresora === i.idImpresora && s.idEstacion !== estacion.idEstacion)) ?? [];
  const opciones = datos ? (asignacion.tipo === 'categoria'
    ? datos.categorias.filter(c => c.activa).map(c => ({ id: c.idCategoria, nombre: c.nombre }))
    : datos.variantes.filter(v => v.disponible).map(v => ({ id: v.idVariante, nombre: etiquetaVariante(v) }))) : [];

  return <section className="surface settings-card estaciones-config">
    <div className="settings-card-head"><span className="settings-symbol">🖨</span><div><h2>Estaciones de preparación</h2><p>Distribuye las categorías y variantes entre las impresoras de cocina.</p></div></div>
    <div className="estaciones-toolbar"><button className="button outline" type="button" disabled={ocupado} onClick={() => void ejecutar(null)}>{ocupado ? 'Procesando…' : 'Actualizar lista'}</button></div>
    <div role="status">{aviso && <p className="estaciones-aviso">{aviso}</p>}</div>{error && !modal && <p role="alert" className="estaciones-error">{error}</p>}
    {!datos ? <p>{error ? 'No se cargó la configuración.' : 'Cargando configuración…'}</p> : <>
      <p className="settings-hint">El estado activo es una configuración, no una prueba de conectividad. Aquí no se envían comandas.</p>
      <div className="settings-grid">
        <section className="surface settings-card">
          <div className="estaciones-toolbar"><h3>Impresoras de cocina</h3><button className="button primary" type="button" disabled={ocupado} onClick={() => { setImpresora(nuevaImpresora()); setError(''); setModal('impresora'); }}>+ Nueva impresora</button></div>
          {datos.impresoras.map(i => <div key={i.idImpresora} className="printer-status"><div><strong>{i.nombre}</strong><small>{i.direccionIp} · {i.activa ? 'Activa' : 'Inactiva'}</small></div><button type="button" className="button outline" disabled={ocupado} onClick={() => { setImpresora({ ...i }); setError(''); setModal('impresora'); }}>Editar</button></div>)}
          {modal === 'impresora' && <Modal label="Impresora de cocina" className="estaciones-dialog" onClose={() => { if (!ocupado) setModal(null); }}><form onSubmit={enviarImpresora}>
            <fieldset disabled={ocupado} style={{ border: 0, padding: 0, minWidth: 0 }}>
              <h4>{impresora.idImpresora ? 'Editar impresora' : 'Nueva impresora'}</h4>
              <label className="settings-field">Nombre<input required maxLength={100} pattern=".*\S.*" value={impresora.nombre} onChange={e => setImpresora({ ...impresora, nombre: e.target.value })}/></label>
              <label className="settings-field">Dirección IP<input required maxLength={45} placeholder="192.0.2.12" autoComplete="off" spellCheck={false} autoCapitalize="none" aria-describedby="estaciones-ip-ayuda" pattern="[0-9A-Fa-f:.]+" value={impresora.direccionIp} onChange={e => setImpresora({ ...impresora, direccionIp: e.target.value.replace(/[^0-9a-fA-F:.]/g, '') })}/><small id="estaciones-ip-ayuda">IPv4: 192.0.2.12 · IPv6: 2001:db8::12 (máx. 45 caracteres).</small>{impresora.direccionIp && !ipValida(impresora.direccionIp) && <small className="estaciones-error">Introduce una dirección IP completa y válida.</small>}</label>
              <label className="settings-payment estaciones-toggle"><span><b>Activa</b><small>Habilitar en la configuración</small></span><input type="checkbox" checked={impresora.activa} onChange={e => setImpresora({ ...impresora, activa: e.target.checked })}/></label>
              <div className="printer-actions"><button className="button primary" type="submit" disabled={!nombreValido(impresora.nombre) || !ipValida(impresora.direccionIp)}>Guardar impresora</button><button className="button outline" type="button" onClick={() => setImpresora(nuevaImpresora())}>Limpiar</button></div>
            </fieldset>
          {error && <p role="alert" className="estaciones-error">{error}</p>}</form></Modal>}
        </section>
        <section className="surface settings-card">
          <div className="estaciones-toolbar"><h3>Estaciones</h3><button className="button primary" type="button" disabled={ocupado} onClick={() => { setEstacion(nuevaEstacion()); setError(''); setModal('estacion'); }}>+ Nueva estación</button></div>
          {datos.estaciones.map(s => <div className="printer-status" key={s.idEstacion}><div><strong>{s.nombre}</strong><small>{impresoraDe(s.idImpresora)?.nombre ?? 'Impresora no disponible'} · {s.activa ? 'Activa' : 'Inactiva'}</small></div><button type="button" className="button outline" disabled={ocupado} onClick={() => { setEstacion({ ...s }); setError(''); setModal('estacion'); }}>Editar</button></div>)}
          {modal === 'estacion' && <Modal label="Estación de preparación" className="estaciones-dialog" onClose={() => { if (!ocupado) setModal(null); }}><form onSubmit={enviarEstacion}>
            <fieldset disabled={ocupado} style={{ border: 0, padding: 0, minWidth: 0 }}>
              <h4>{estacion.idEstacion ? 'Editar estación' : 'Nueva estación'}</h4>
              <label className="settings-field">Nombre<input required maxLength={100} pattern=".*\S.*" value={estacion.nombre} onChange={e => setEstacion({ ...estacion, nombre: e.target.value })}/></label>
              <label className="settings-field">Impresora<select required value={estacion.idImpresora} onChange={e => setEstacion({ ...estacion, idImpresora: e.target.value, activa: estacion.activa && Boolean(impresoraDe(Number(e.target.value))?.activa) })}><option value="">Seleccionar</option>{impresorasLibres.map(i => <option key={i.idImpresora} value={i.idImpresora}>{i.nombre}{i.activa ? '' : ' (inactiva)'}</option>)}</select></label>
              <label className="settings-payment estaciones-toggle"><span><b>Activa</b><small>Habilitar en la configuración</small></span><input type="checkbox" checked={estacion.activa} disabled={!impresoraDe(Number(estacion.idImpresora))?.activa} onChange={e => setEstacion({ ...estacion, activa: e.target.checked })}/></label>
              <div className="printer-actions"><button className="button primary" type="submit" disabled={!nombreValido(estacion.nombre) || !estacion.idImpresora}>Guardar estación</button><button className="button outline" type="button" onClick={() => setEstacion(nuevaEstacion())}>Limpiar</button></div>
            </fieldset>
          {error && <p role="alert" className="estaciones-error">{error}</p>}</form></Modal>}
        </section>
      </div>
      <h3 className="estaciones-subtitulo">Asignaciones del catálogo</h3>
      <p className="settings-hint">Una categoría o variante solo puede tener una asignación directa. Puedes cambiarla directamente de estación. Propuesta de prioridad: la variante específica prevalece sobre su categoría; pendiente de integrar con impresión.</p>
      {datos.estaciones.map(s => <div className="surface settings-card" key={s.idEstacion} style={{ marginBlock: '12px' }}>
        <strong>{s.nombre} · {s.activa ? 'Activa' : 'Inactiva'}</strong>
        {!datos.asignaciones.some(a => a.idEstacion === s.idEstacion) && <p className="settings-hint">Sin asignaciones.</p>}
        {datos.asignaciones.filter(a => a.idEstacion === s.idEstacion).map(a => <div className="printer-status" key={a.idAsignacion}><span>{nombreAsignacion(a)}</span><button className="button outline" type="button" disabled={ocupado} onClick={() => quitar(s.idEstacion, a.idAsignacion)}>Retirar</button></div>)}
      </div>)}
      <form onSubmit={enviarAsignacion}>
        <fieldset disabled={ocupado} style={{ border: 0, padding: 0, minWidth: 0 }}>
          <div className="settings-grid">
            <label className="settings-field">Estación<select required value={asignacion.estacion} onChange={e => setAsignacion({ ...asignacion, estacion: e.target.value })}><option value="">Seleccionar</option>{datos.estaciones.map(s => <option key={s.idEstacion} value={s.idEstacion}>{s.nombre}</option>)}</select></label>
            <label className="settings-field">Tipo<select value={asignacion.tipo} onChange={e => setAsignacion({ ...asignacion, tipo: e.target.value, elemento: '' })}><option value="categoria">Categoría</option><option value="variante">Variante de producto</option></select></label>
            <label className="settings-field">Elemento<select required value={asignacion.elemento} onChange={e => setAsignacion({ ...asignacion, elemento: e.target.value })}><option value="">Seleccionar</option>{opciones.map(o => <option key={o.id} value={o.id}>{o.nombre}</option>)}</select></label>
          </div>
          <button className="button primary" type="submit" disabled={!opciones.length || !asignacion.estacion || !asignacion.elemento}>Asignar</button>
        </fieldset>
      </form>
      <p className="integration-note">Variantes disponibles sin estación activa e impresora activa (según la prioridad propuesta): {sinEstacion.length ? sinEstacion.map(etiquetaVariante).join(', ') : 'Ninguna'}.</p>
    </>}
  </section>;
}
