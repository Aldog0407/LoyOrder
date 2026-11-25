import { useCallback, useEffect, useState } from 'react';
import { obtenerCatalogo } from '../services/catalogo.js';
import { horaActualizacion } from '../utils/formato.js';
import MenuCard from '../components/MenuCard.jsx';
import DishDetail from '../components/DishDetail.jsx';

const TODOS = 'todos';

// 7.2.4 · Catálogo por categorías (req. 8). Los datos vienen de GET /api/v1/catalogo.
export default function MenuView() {
  const [estado, setEstado] = useState({ cargando: true, error: null, menu: null });
  const [categoria, setCategoria] = useState(TODOS);
  const [selected, setSelected] = useState(null);

  const cargar = useCallback(async señal => {
    setEstado(actual => ({ ...actual, cargando: true, error: null }));
    try {
      const menu = await obtenerCatalogo();
      if (!señal?.cancelado) setEstado({ cargando: false, error: null, menu });
    } catch (error) {
      if (!señal?.cancelado) setEstado(actual => ({ ...actual, cargando: false, error: error.message }));
    }
  }, []);

  useEffect(() => {
    const señal = { cancelado: false };
    void cargar(señal);
    return () => { señal.cancelado = true; };
  }, [cargar]);

  const categorias = estado.menu?.categorias ?? [];
  // Si una sincronización quitó la categoría elegida, se regresa a "Todos".
  const elegida = categorias.some(c => c.id === categoria) ? categoria : TODOS;
  const platillos = categorias.filter(c => elegida === TODOS || c.id === elegida).flatMap(c => c.productos);
  const actualizado = horaActualizacion(estado.menu?.actualizadoEn);

  let contenido;
  if (estado.cargando && !estado.menu) contenido = <p className="menu-status" role="status">Cargando el menú…</p>;
  else if (estado.error && !estado.menu) contenido = <div className="menu-status error" role="alert"><p>{estado.error}</p><button className="button outline" onClick={() => cargar()}>Reintentar</button></div>;
  else if (!categorias.length) contenido = <div className="menu-status" role="status"><p>El menú aún no está disponible. El restaurante todavía no sincroniza su catálogo.</p><button className="button outline" onClick={() => cargar()}>Volver a consultar</button></div>;
  else contenido = <>
    <div className="categories" role="group" aria-label="Categorías">
      {[{ id: TODOS, nombre: 'Todos' }, ...categorias].map(c => <button className={`category ${elegida === c.id ? 'active' : ''}`} key={c.id} aria-pressed={elegida === c.id} onClick={() => setCategoria(c.id)}>{c.nombre}</button>)}
    </div>
    {estado.error && <p className="menu-status error" role="alert">{estado.error}</p>}
    <div className="dish-grid">{platillos.map(dish => <MenuCard key={dish.id} dish={dish} onSelect={setSelected}/>)}</div>
  </>;

  return <main><section className="hero container"><div className="hero-copy"><span className="eyebrow">✦ SABOR QUE TE HACE VOLVER</span><h1>Tu antojo favorito,<br/><em>a un clic de distancia.</em></h1><p>De nuestra cocina a tu mesa. Explora platillos hechos con amor.</p><a className="button light" href="#platillos">Explorar el menú ↓</a></div><div className="hero-visual"><div className="hero-orbit">✦</div><span className="hero-food" aria-hidden="true">🍔</span><span className="hero-floating">🔥 Preparado al momento</span></div></section>
    <section className="container menu-section" id="platillos" aria-busy={estado.cargando}><div className="section-top"><div><span className="eyebrow orange">NUESTRO MENÚ</span><h2>¿Qué se te antoja hoy?</h2><p>Encuentra algo delicioso para cada momento.</p></div>
      {estado.menu && <span className="availability">● {actualizado ? `Menú actualizado ${actualizado}` : 'Menú sin sincronizar'}</span>}</div>
      {contenido}
    </section>
    {selected && <DishDetail dish={selected} onClose={() => setSelected(null)}/>}
  </main>;
}
