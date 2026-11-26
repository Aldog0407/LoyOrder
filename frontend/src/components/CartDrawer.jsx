import { useStore } from '../state/StoreContext.jsx';
import CartFlow from './CartFlow.jsx';

/** El drawer solo controla el contenedor; CartFlow implementa el checkout. */
export default function CartDrawer() {
  const { state, dispatch } = useStore();
  if (!state.drawerOpen) return null;
  const close = () => { if (!state.confirmandoPedido) dispatch({ type: 'CLOSE_CART' }); };
  const count = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  return (
    <div className="backdrop drawer-backdrop" onMouseDown={event => {
      if (event.target === event.currentTarget) close();
    }}>
      <aside className="cart-drawer" role="dialog" aria-modal="true" aria-label="Tu carrito">
        <div className="drawer-header">
          <div><span className="eyebrow orange">TU SELECCIÓN</span><h2>Tu carrito ({count})</h2></div>
          <button className="close-button" disabled={state.confirmandoPedido} onClick={close} aria-label="Cerrar">×</button>
        </div>
        <CartFlow onClose={close}/>
      </aside>
    </div>
  );
}
