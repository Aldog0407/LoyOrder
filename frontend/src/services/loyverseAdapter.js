/** Contrato de integración futura; jamás introducir tokens privados en Vite. */
export const loyverseAdapter = {
  async listPaymentMethods() {
    // FUTURO: GET /api/integrations/loyverse/payment-types en nuestro backend.
    return { connected: false, methods: [], message: 'Loyverse no conectado' };
  },
  async testPrint(connected) {
    await new Promise(resolve => setTimeout(resolve, 550));
    if (!connected) throw new Error('Impresora desconectada (demo)');
    // FUTURO: POST /api/pos/print-test. No se comunica con hardware.
    return { ok: true, folio: `TEST-${Date.now().toString(36).toUpperCase()}` };
  },
};
