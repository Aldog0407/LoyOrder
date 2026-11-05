import { categories, dishes, findDish, money } from './data.js';
import { state, update, save } from './store.js';

const app = document.querySelector('#app');
const overlays = document.querySelector('#overlay-root');
const toastRoot = document.querySelector('#toast-root');
const paths = ['/menu', '/seguimiento', '/historial', '/admin'];
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const icon = name => ({ cart: '🛒', clock: '◷', history: '▤', grid: '▦' })[name] || '•';
const activeOrder = () => state.orders.find(o => o.id === state.activeOrderId);
const itemCount = () => state.cart.reduce((s, i) => s + i.quantity, 0);
const subtotal = () => state.cart.reduce((s, i) => s + i.price * i.quantity, 0);
const deliveryFee = () => state.delivery === 'domicilio' ? 35 : 0;
let path = paths.includes(location.pathname) ? location.pathname : '/menu';
let detailQuantity = 1;
let detailVariant = '';
let rating = 0;
let ratingOrderId = null;
let mapDraft = { ...state.location };

function navigate(next) {
  path = paths.includes(next) ? next : '/menu';
  history.pushState({}, '', path);
  update({ drawer: false, modal: null, mobileMenu: false });
  render(); window.scrollTo({ top: 0, behavior: 'instant' });
}
window.addEventListener('popstate', () => { path = paths.includes(location.pathname) ? location.pathname : '/menu'; update({ modal: null, drawer: false }); render(); });

function toast(message, kind = 'success') {
  const el = document.createElement('div');
  el.className = `toast ${kind}`;
  el.setAttribute('role', 'status');
  el.textContent = message;
  toastRoot.append(el);
  setTimeout(() => { el.classList.add('leaving'); setTimeout(() => el.remove(), 280); }, 3100);
}

function header() {
  const links = [['/menu', 'Explorar menú'], ['/seguimiento', 'Mi pedido'], ['/historial', 'Historial']];
  return `<header class="site-header"><div class="header-inner"><a class="logo" href="/menu" data-link><span class="logo-mark">✳</span>Loy<span>Order</span></a>
    <nav class="main-nav ${state.mobileMenu ? 'open' : ''}" aria-label="Navegación principal">${links.map(([url, label]) => `<a href="${url}" data-link class="${path === url ? 'active' : ''}">${label}</a>`).join('')}</nav>
    <div class="header-actions"><button class="role-switch ${path === '/admin' ? 'selected' : ''}" data-route="${path === '/admin' ? '/menu' : '/admin'}">${path === '/admin' ? '← Vista cliente' : 'Vista admin ↗'}</button><button class="cart-top" data-action="cart" aria-label="Abrir carrito">🛒 <span>Carrito</span><b>${itemCount()}</b></button><button class="menu-toggle" data-action="mobile-menu" aria-label="Abrir menú">☰</button></div></div></header>`;
}

function card(d) {
  return `<article class="dish-card ${d.available ? '' : 'sold-out'}"><div class="dish-art" style="--art:${d.color}"><span>${d.emoji}</span>${!d.available ? '<span class="stock-label">Agotado</span>' : ''}</div><div class="dish-content"><div class="dish-meta"><span>${d.category}</span><span class="rating">★ ${d.rating}</span></div><h3>${d.name}</h3><p>${d.description}</p><div class="dish-footer"><strong>${money(d.price)}</strong><button ${!d.available ? 'disabled' : `data-detail="${d.id}"`} aria-label="Ver ${d.name}">${d.available ? '+ Agregar' : 'No disponible'}</button></div></div></article>`;
}

function menu() {
  const filtered = state.category === 'Todos' ? dishes : dishes.filter(d => d.category === state.category);
  return `<main><section class="hero container"><div class="hero-copy"><span class="eyebrow">✦ SABOR QUE TE HACE VOLVER</span><h1>Tu antojo favorito,<br><em>a un clic de distancia.</em></h1><p>De nuestra cocina a tu mesa. Explora platillos hechos con amor y pide lo que más se te antoje.</p><button class="button light" data-action="explore">Explorar el menú ↓</button></div><div class="hero-visual"><div class="hero-orbit">✦</div><span class="hero-food">🍔</span><span class="hero-floating">🔥 Preparado al momento</span></div></section>
  <section class="container menu-section" id="platillos"><div class="section-top"><div><span class="eyebrow orange">NUESTRO MENÚ</span><h2>¿Qué se te antoja hoy?</h2><p>Encuentra algo delicioso para cada momento.</p></div><span class="availability">● Cocina abierta · Pedidos disponibles</span></div><div class="categories" role="group" aria-label="Filtrar categorías">${categories.map(c => `<button class="category ${state.category === c ? 'active' : ''}" data-category="${c}">${c}</button>`).join('')}</div><div class="dish-grid">${filtered.map(card).join('')}</div></section></main>`;
}

function timeline(order) {
  const labels = ['Recibido', 'Preparando', order.delivery === 'domicilio' ? 'Enviado' : 'Listo para recoger', 'Entregado'];
  return `<div class="timeline">${labels.map((label, i) => `<div class="timeline-step ${i <= order.status ? 'done' : ''} ${i === order.status ? 'current' : ''}"><div class="step-dot">${i < order.status ? '✓' : i + 1}</div><div class="step-copy"><strong>${label}</strong><small>${i < order.status ? 'Completado' : i === order.status ? 'Estado actual' : 'Pendiente'}</small></div></div>`).join('')}</div>`;
}

function tracking() {
  const order = activeOrder();
  if (!order) return `<main class="container page"><div class="page-heading"><span class="eyebrow orange">TU PEDIDO</span><h1>Seguimiento de pedido</h1></div><div class="empty-state"><div>🛵</div><h2>Todavía no hay un pedido activo</h2><p>Cuando hagas un pedido, podrás seguir su progreso aquí.</p><button class="button primary" data-route="/menu">Explorar menú</button></div></main>`;
  return `<main class="container page"><div class="page-heading"><span class="eyebrow orange">EN TIEMPO REAL</span><h1>Seguimiento de pedido</h1><p>Entérate de cada paso de tu pedido.</p></div><div class="tracking-layout"><section class="surface tracking-card"><div class="order-head"><div><span class="eyebrow orange">PEDIDO ${order.id}</span><h2>${order.status === 3 ? '¡Tu pedido fue entregado!' : '¡Estamos en ello!'}</h2><p>${order.status === 3 ? 'Esperamos que lo hayas disfrutado.' : 'Tu comida está en buenas manos.'}</p></div><div class="status-illustration">${order.status === 3 ? '🎉' : '👨‍🍳'}</div></div>${timeline(order)}${order.status < 3 ? `<button class="button outline" data-action="advance">Simular siguiente estado →</button>` : !order.rated ? `<button class="button primary" data-rate="${order.id}">★ Calificar pedido</button>` : '<span class="badge success">★ Pedido calificado</span>'}</section><aside class="surface summary-card"><h3>Resumen de tu pedido</h3><p class="muted">${order.id} · ${order.date}</p><div class="summary-items">${order.items.map(i => `<div><span>${i.quantity}× ${findDish(i.dishId)?.name || 'Platillo'}<small>${escapeHtml(i.variant)}</small></span><b>${money(i.price * i.quantity)}</b></div>`).join('')}</div><div class="summary-total"><span>Total</span><strong>${money(order.total)}</strong></div><div class="info-pair"><span>Entrega</span><b>${order.delivery === 'domicilio' ? 'A domicilio' : 'Recoger en local'}</b></div><div class="info-pair"><span>Pago</span><b>${order.payment === 'tarjeta' ? 'Tarjeta (simulado)' : 'Efectivo'}</b></div><button class="text-button" data-route="/historial">Ver historial completo →</button></aside></div></main>`;
}

function historyPage() {
  return `<main class="container page"><div class="page-heading"><span class="eyebrow orange">TUS FAVORITOS SIEMPRE CERCA</span><h1>Historial de pedidos</h1><p>Una buena comida merece repetirse.</p></div><div class="history-list">${state.orders.map(o => `<article class="surface history-card"><div class="history-icon">🛍️</div><div class="history-main"><div class="history-title"><h3>Pedido ${o.id}</h3><span class="badge ${o.status === 3 ? 'success' : 'warning'}">${o.status === 3 ? 'Entregado' : 'En proceso'}</span></div><p>${o.date} · ${o.items.map(i => `${i.quantity}× ${findDish(i.dishId)?.name || 'Platillo'}`).join(', ')}</p><strong>${money(o.total)}</strong></div><div class="history-actions"><button class="button outline" data-reorder="${o.id}">↻ Reordenar</button>${o.status === 3 && !o.rated ? `<button class="button subtle" data-rate="${o.id}">★ Calificar</button>` : ''}<button class="text-button" data-track="${o.id}">Ver detalle →</button></div></article>`).join('')}</div></main>`;
}

function admin() {
  const factor = { Hoy: 1, Semana: 5.4, Mes: 23 }[state.period];
  const orders = state.orders;
  const completed = orders.filter(o => o.status === 3).length;
  const sales = orders.filter(o => o.status === 3).reduce((s, o) => s + o.total, 0);
  return `<main class="container page admin-page"><div class="admin-top"><div><span class="eyebrow orange">PANEL DE CONTROL</span><h1>Dashboard</h1><p>Un vistazo al desempeño de LoyOrder.</p></div><div class="periods" aria-label="Seleccionar periodo">${['Hoy', 'Semana', 'Mes'].map(p => `<button class="${p === state.period ? 'active' : ''}" data-period="${p}">${p}</button>`).join('')}</div></div><div class="kpi-grid"><div class="surface kpi"><span class="kpi-icon orange-bg">＄</span><span>Ventas ${state.period.toLowerCase()}</span><strong>${money(Math.round(sales * factor))}</strong><small>Datos ilustrativos del periodo</small></div><div class="surface kpi"><span class="kpi-icon green-bg">✓</span><span>Pedidos completados</span><strong>${Math.round(completed * factor)}</strong><small>Estimación de demostración</small></div><div class="surface kpi"><span class="kpi-icon yellow-bg">★</span><span>Calificación promedio</span><strong>4.8 <small>/ 5.0</small></strong><small>Valor de ejemplo</small></div></div><section class="surface table-section"><div class="table-top"><div><h2>Últimos pedidos</h2><p>Actividad reciente del restaurante</p></div><span class="badge success">● Actualizado en esta sesión</span></div><div class="table-scroll"><table><thead><tr><th>Pedido</th><th>Fecha</th><th>Productos</th><th>Total</th><th>Estado</th><th>Acción</th></tr></thead><tbody>${orders.map(o => `<tr><td><strong>${o.id}</strong></td><td>${o.date}</td><td>${o.items.reduce((s, i) => s + i.quantity, 0)} artículos</td><td><strong>${money(o.total)}</strong></td><td><span class="badge ${o.status === 3 ? 'success' : 'warning'}">${o.status === 3 ? 'Entregado' : ['Recibido', 'Preparando', 'Enviado / listo'][o.status]}</span></td><td><button class="text-button" data-track="${o.id}">Ver →</button></td></tr>`).join('')}</tbody></table></div></section><div class="demo-note">ℹ️ Dashboard de demostración: los indicadores de periodo son ilustrativos y no provienen de un servidor.</div></main>`;
}

function detailModal(id) {
  const d = findDish(id);
  if (!d) return '';
  return `<div class="backdrop" data-close><section class="modal detail-modal" role="dialog" aria-modal="true" aria-label="Detalle de platillo"><button class="close-button" data-close aria-label="Cerrar">×</button><div class="detail-art" style="--art:${d.color}">${d.emoji}</div><div class="detail-body"><span class="eyebrow orange">${d.category} · ★ ${d.rating}</span><h2>${d.name}</h2><p>${d.description}</p><strong class="detail-price">${money(d.price)}</strong><fieldset class="variant-field"><legend>Elige una variante <span>* Requerido</span></legend>${d.variants.map(v => `<label class="variant-option"><input type="radio" name="variant" value="${escapeHtml(v)}" ${detailVariant === v ? 'checked' : ''}><span>${v}</span></label>`).join('')}</fieldset><div class="detail-bottom"><div class="quantity"><button data-qty="-1" aria-label="Restar cantidad">−</button><b>${detailQuantity}</b><button data-qty="1" aria-label="Sumar cantidad">+</button></div><button class="button primary flex-button" data-add="${d.id}">Agregar · ${money(d.price * detailQuantity)}</button></div></div></section></div>`;
}

function cartDrawer() {
  const delivery = state.delivery || 'local';
  const payment = state.payment || 'efectivo';
  return `<div class="backdrop drawer-backdrop" data-close><aside class="cart-drawer" role="dialog" aria-modal="true" aria-label="Tu carrito"><div class="drawer-header"><div><span class="eyebrow orange">TU SELECCIÓN</span><h2>Tu carrito <span>(${itemCount()})</span></h2></div><button class="close-button" data-close aria-label="Cerrar carrito">×</button></div><div class="drawer-body">${state.cart.length ? `<div class="cart-lines">${state.cart.map((i, index) => `<div class="cart-line"><div class="cart-emoji">${findDish(i.dishId)?.emoji || '🍽️'}</div><div class="cart-line-info"><strong>${findDish(i.dishId)?.name}</strong><small>${escapeHtml(i.variant)}</small><span>${money(i.price * i.quantity)}</span><div class="line-actions"><button data-cart-qty="${index}:-1" aria-label="Reducir">−</button><b>${i.quantity}</b><button data-cart-qty="${index}:1" aria-label="Aumentar">+</button><button class="remove" data-remove="${index}">Eliminar</button></div></div></div>`).join('')}</div><h3>¿Cómo lo quieres recibir?</h3><div class="choice-grid"><label class="choice ${delivery === 'local' ? 'chosen' : ''}"><input type="radio" name="delivery" value="local" ${delivery === 'local' ? 'checked' : ''}>🏠 Recoger en local</label><label class="choice ${delivery === 'domicilio' ? 'chosen' : ''}"><input type="radio" name="delivery" value="domicilio" ${delivery === 'domicilio' ? 'checked' : ''}>🛵 A domicilio</label></div>${delivery === 'domicilio' ? `<div class="address-box"><div><small>Dirección de entrega</small><strong>${escapeHtml(state.address)}</strong></div><button data-action="map">Cambiar →</button></div>` : ''}<h3>Método de pago</h3><div class="choice-grid"><label class="choice ${payment === 'efectivo' ? 'chosen' : ''}"><input type="radio" name="payment" value="efectivo" ${payment === 'efectivo' ? 'checked' : ''}>💵 Efectivo</label><label class="choice ${payment === 'tarjeta' ? 'chosen' : ''}"><input type="radio" name="payment" value="tarjeta" ${payment === 'tarjeta' ? 'checked' : ''}>💳 Tarjeta</label></div><p class="fine-print">La confirmación de tarjeta es simulada. No se solicitan datos bancarios.</p>` : `<div class="cart-empty"><span>🛒</span><h3>Tu carrito está vacío</h3><p>Agrega algo delicioso desde nuestro menú.</p><button class="button outline" data-action="close">Ver menú</button></div>`}</div>${state.cart.length ? `<footer class="drawer-footer"><div><span>Subtotal</span><b>${money(subtotal())}</b></div><div><span>Envío</span><b>${delivery === 'local' ? 'Gratis' : money(35)}</b></div><div class="grand-total"><span>Total</span><strong>${money(subtotal() + (delivery === 'domicilio' ? 35 : 0))}</strong></div><button class="button primary full" data-action="checkout">Confirmar pedido →</button></footer>` : ''}</aside></div>`;
}

function mapModal() {
  const { x, y } = mapDraft;
  return `<div class="backdrop" data-close><section class="modal map-modal" role="dialog" aria-modal="true" aria-label="Seleccionar ubicación"><div class="modal-title"><div><span class="eyebrow orange">ENTREGA A DOMICILIO</span><h2>Elige tu ubicación</h2><p>Arrastra el pin o toca el mapa para ajustar el punto.</p></div><button class="close-button" data-close aria-label="Cerrar">×</button></div><div class="demo-map" id="demo-map" role="img" aria-label="Mapa esquemático interactivo de demostración"><div class="map-road road-a"></div><div class="map-road road-b"></div><div class="map-road road-c"></div><div class="map-park">PARQUE CENTRAL 🌳</div><div class="map-label map-label-a">AV. CENTRAL</div><div class="map-label map-label-b">CALLE REFORMA</div><div class="map-pin" style="left:${x}%;top:${y}%" aria-hidden="true">📍</div></div><label class="field-label" for="address">Dirección y referencias</label><input id="address" class="address-input" maxlength="160" value="${escapeHtml(state.address)}" placeholder="Calle, número, colonia" /><p class="fine-print">Mapa esquemático de prueba; no usa geocodificación ni calcula distancias reales.</p><button class="button primary full" data-action="save-map">Confirmar ubicación</button></section></div>`;
}

function confirmModal() {
  const payment = state.payment || 'efectivo';
  return `<div class="backdrop" data-close><section class="modal confirm-modal" role="dialog" aria-modal="true" aria-label="Confirmar pedido"><button class="close-button" data-close aria-label="Cerrar">×</button><div class="confirm-icon">✓</div><h2>¿Confirmamos tu pedido?</h2><p>Revisa el total antes de enviarlo a cocina.</p><div class="confirm-total"><span>Total a pagar</span><strong>${money(subtotal() + (state.delivery === 'domicilio' ? 35 : 0))}</strong></div><p class="confirm-description">${state.delivery === 'domicilio' ? 'Entrega a domicilio' : 'Recoger en local'} · ${payment === 'tarjeta' ? 'Pago con tarjeta simulado' : 'Pago en efectivo'}</p><button class="button primary full" data-action="place-order">Sí, realizar pedido</button><button class="button subtle full" data-action="back-cart">Volver al carrito</button></section></div>`;
}

function rateModal() {
  return `<div class="backdrop" data-close><section class="modal rate-modal" role="dialog" aria-modal="true" aria-label="Calificar pedido"><button class="close-button" data-close aria-label="Cerrar">×</button><div class="rate-face">💛</div><h2>¿Qué tal estuvo todo?</h2><p>Tu opinión nos ayuda a mejorar.</p><div class="stars" role="group" aria-label="Calificación de 1 a 5 estrellas">${[1, 2, 3, 4, 5].map(n => `<button data-star="${n}" class="${n <= rating ? 'selected' : ''}" aria-label="${n} estrellas" aria-pressed="${n === rating}">★</button>`).join('')}</div><small>${rating ? ['','Muy malo','Malo','Regular','Bueno','¡Excelente!'][rating] : 'Selecciona una calificación'}</small><button class="button primary full" data-action="submit-rating" ${!rating ? 'disabled' : ''}>Enviar calificación</button></section></div>`;
}

function reorderModal(id) {
  const o = state.orders.find(o => o.id === id);
  return `<div class="backdrop" data-close><section class="modal confirm-modal" role="dialog" aria-modal="true" aria-label="Confirmar reordenación"><button class="close-button" data-close aria-label="Cerrar">×</button><div class="rate-face">🔄</div><h2>¿Deseas volver a pedirlo?</h2><p>Agregaremos los platillos disponibles del pedido ${o.id} a tu carrito. Podrás revisarlos antes de confirmar.</p><button class="button primary full" data-confirm-reorder="${id}">Sí, reordenar</button><button class="button subtle full" data-close>Cancelar</button></section></div>`;
}

function render() {
  document.title = `${({ '/menu': 'Menú', '/seguimiento': 'Seguimiento', '/historial': 'Historial', '/admin': 'Dashboard' })[path]} · LoyOrder`;
  app.innerHTML = `${header()}${({ '/menu': menu, '/seguimiento': tracking, '/historial': historyPage, '/admin': admin })[path]()}<footer class="site-footer"><div class="container"><span class="logo"><span class="logo-mark">✳</span>Loy<span>Order</span></span><p>Hecho con sabor y mucho cariño. · Demo frontend © 2026</p></div></footer><button class="cart-fab" data-action="cart" aria-label="Abrir carrito">🛒${itemCount() ? `<b>${itemCount()}</b>` : ''}</button>`;
  overlays.innerHTML = state.drawer ? cartDrawer() : state.modal?.type === 'detail' ? detailModal(state.modal.id) : state.modal?.type === 'map' ? mapModal() : state.modal?.type === 'confirm' ? confirmModal() : state.modal?.type === 'rate' ? rateModal() : state.modal?.type === 'reorder' ? reorderModal(state.modal.id) : '';
  document.body.classList.toggle('overlay-open', !!state.drawer || !!state.modal);
  if (state.modal?.type === 'map') setupMap();
}

function setupMap() {
  const map = document.querySelector('#demo-map');
  const updatePin = event => {
    const rect = map.getBoundingClientRect();
    mapDraft = { x: Math.max(5, Math.min(95, Math.round((event.clientX - rect.left) / rect.width * 100))), y: Math.max(8, Math.min(92, Math.round((event.clientY - rect.top) / rect.height * 100))) };
    const pin = map.querySelector('.map-pin');
    pin.style.left = `${mapDraft.x}%`; pin.style.top = `${mapDraft.y}%`;
  };
  map.addEventListener('pointerdown', event => { map.setPointerCapture(event.pointerId); updatePin(event); });
  map.addEventListener('pointermove', event => { if (map.hasPointerCapture(event.pointerId)) updatePin(event); });
}

document.addEventListener('click', event => {
  const el = event.target.closest('button, a[data-link], [data-close]');
  if (!el) return;
  if (el.matches('a[data-link]')) { event.preventDefault(); navigate(new URL(el.href).pathname); return; }
  if (el.hasAttribute('data-close')) { if (event.target !== el && el.classList.contains('backdrop')) return; update({ modal: null, drawer: false }); render(); return; }
  const d = el.dataset;
  if (d.route) return navigate(d.route);
  if (d.category) { update({ category: d.category }); render(); return; }
  if (d.period) { update({ period: d.period }); render(); return; }
  if (d.detail) { detailQuantity = 1; detailVariant = ''; update({ modal: { type: 'detail', id: +d.detail } }); render(); return; }
  if (d.qty) { detailQuantity = Math.max(1, Math.min(20, detailQuantity + Number(d.qty))); render(); return; }
  if (d.add) {
    if (!detailVariant) return toast('Selecciona una variante para continuar.', 'error');
    const dish = findDish(+d.add);
    if (!dish?.available) return toast('Este platillo está agotado.', 'error');
    const item = state.cart.find(i => i.dishId === dish.id && i.variant === detailVariant);
    if (item) item.quantity += detailQuantity;
    else state.cart.push({ dishId: dish.id, variant: detailVariant, quantity: detailQuantity, price: dish.price });
    update({ modal: null }); render(); toast(`${dish.name} agregado al carrito`); return;
  }
  if (d.cartQty) { const [index, delta] = d.cartQty.split(':').map(Number); state.cart[index].quantity += delta; if (state.cart[index].quantity <= 0) state.cart.splice(index, 1); save(); render(); return; }
  if (d.remove) { state.cart.splice(+d.remove, 1); save(); render(); toast('Platillo eliminado del carrito'); return; }
  if (d.rate) { rating = 0; ratingOrderId = d.rate; update({ modal: { type: 'rate' } }); render(); return; }
  if (d.star) { rating = +d.star; render(); return; }
  if (d.reorder) { update({ modal: { type: 'reorder', id: d.reorder } }); render(); return; }
  if (d.confirmReorder) {
    const order = state.orders.find(o => o.id === d.confirmReorder);
    const valid = order.items.filter(i => findDish(i.dishId)?.available);
    valid.forEach(i => { const present = state.cart.find(c => c.dishId === i.dishId && c.variant === i.variant); if (present) present.quantity += i.quantity; else state.cart.push({ ...i }); });
    update({ modal: null, drawer: true }); render(); toast(valid.length ? 'Platillos disponibles agregados al carrito' : 'Ya no hay platillos disponibles', valid.length ? 'success' : 'error'); return;
  }
  if (d.track) { update({ activeOrderId: d.track }); navigate('/seguimiento'); return; }
  const action = d.action;
  if (action === 'cart') { update({ drawer: true, modal: null }); render(); }
  if (action === 'close') { update({ drawer: false, modal: null }); render(); }
  if (action === 'mobile-menu') { update({ mobileMenu: !state.mobileMenu }); render(); }
  if (action === 'explore') document.querySelector('#platillos')?.scrollIntoView({ behavior: 'smooth' });
  if (action === 'map') { mapDraft = { ...state.location }; update({ drawer: false, modal: { type: 'map' } }); render(); }
  if (action === 'save-map') { const address = document.querySelector('#address').value.trim(); if (address.length < 8) return toast('Ingresa una dirección más completa.', 'error'); update({ address, location: { ...mapDraft }, modal: null, drawer: true }); render(); toast('Ubicación guardada'); }
  if (action === 'checkout') { if (!state.cart.length) return; update({ drawer: false, modal: { type: 'confirm' } }); render(); }
  if (action === 'back-cart') { update({ modal: null, drawer: true }); render(); }
  if (action === 'place-order') {
    if (!state.cart.length) return;
    const id = `LY-${Math.floor(Date.now() % 900000 + 100000)}`;
    const order = { id, date: new Date().toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' }), items: state.cart.map(i => ({ ...i })), total: subtotal() + (state.delivery === 'domicilio' ? 35 : 0), delivery: state.delivery || 'local', payment: state.payment || 'efectivo', status: 0, rated: false };
    state.orders.unshift(order); state.cart = []; update({ activeOrderId: id, modal: null }); navigate('/seguimiento'); toast(`¡Pedido ${id} recibido!`); return;
  }
  if (action === 'advance') { const order = activeOrder(); if (order && order.status < 3) { order.status++; save(); render(); toast('Estado del pedido actualizado'); } }
  if (action === 'submit-rating') { if (!rating) return; const order = state.orders.find(o => o.id === ratingOrderId); if (order) { order.rated = true; order.rating = rating; } update({ modal: null }); render(); toast('¡Gracias por tu calificación!'); }
});

document.addEventListener('change', event => {
  if (event.target.name === 'variant') { detailVariant = event.target.value; return; }
  if (event.target.name === 'delivery') { update({ delivery: event.target.value }); render(); }
  if (event.target.name === 'payment') { update({ payment: event.target.value }); render(); }
});
document.addEventListener('keydown', event => { if (event.key === 'Escape' && (state.modal || state.drawer)) { update({ modal: null, drawer: false }); render(); } });
render();
