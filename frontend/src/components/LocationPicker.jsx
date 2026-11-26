import { useStore } from '../state/StoreContext.jsx';
import MapAddressSelector from './MapAddressSelector.jsx';

/** The address form lives entirely inside the map modal. Only saving updates the checkout store. */
export default function LocationPicker({ onClose }) {
  const { state, dispatch } = useStore();
  return <MapAddressSelector
    initialAddress={state.address}
    initialPin={state.pin}
    initialCheckoutAddress={state.checkoutAddress}
    onClose={onClose}
    onSave={location => {
      dispatch({ type: 'SET_ADDRESS', address: location.address, pin: location.pin });
      dispatch({ type: 'SET_CHECKOUT_ADDRESS', campo: 'calle', valor: location.checkoutAddress.calle });
      dispatch({ type: 'SET_CHECKOUT_ADDRESS', campo: 'numero', valor: location.checkoutAddress.numero });
      onClose();
    }}
  />;
}
