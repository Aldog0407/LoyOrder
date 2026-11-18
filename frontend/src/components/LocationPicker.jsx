import { useStore } from '../state/StoreContext.jsx';
import MapAddressSelector from './MapAddressSelector.jsx';

/** Adapter between the reusable map and the existing checkout store. */
export default function LocationPicker({ onClose }) {
  const { state, dispatch } = useStore();
  return <MapAddressSelector initialAddress={state.address} initialPin={state.pin}
    onClose={onClose} onSave={location => {
      dispatch({ type: 'SET_ADDRESS', ...location });
      onClose();
    }} />;
}
