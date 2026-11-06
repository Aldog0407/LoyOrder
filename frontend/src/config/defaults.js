export const defaultConfig = {
  openingTime: '09:00', closingTime: '22:00',
  shipping: { mode: 'flat', base: 35, radiusKm: 5, perKm: 8 },
  paymentMethods: { cash: true, card: true, digital: false },
  printer: { connected: false },
};

export const paymentCatalog = [
  { id: 'cash', label: 'Efectivo', icon: '💵', loyverseType: 'CASH' },
  { id: 'card', label: 'Tarjeta de crédito / débito', icon: '💳', loyverseType: 'CARD' },
  { id: 'digital', label: 'Pagos digitales', icon: '📲', loyverseType: 'OTHER' },
];
