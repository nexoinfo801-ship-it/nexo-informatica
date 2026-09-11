export function text(value, { min = 0, max = 500, field = 'value' } = {}) {
  const s = String(value ?? '').trim();
  if (s.length < min || s.length > max) throw bad(`${field.toUpperCase()}_INVALID`);
  return s;
}

export function installId(value) {
  const s = text(value, { min: 8, max: 128, field: 'install_id' });
  if (!/^[A-Za-z0-9._:-]+$/.test(s)) throw bad('INSTALL_ID_INVALID');
  return s;
}

export function action(value) {
  const s = text(value, { min: 1, max: 80, field: 'action' });
  if (!/^[a-z0-9_:-]+$/i.test(s)) throw bad('ACTION_INVALID');
  return s;
}

export function licenseKey(value) {
  const s = text(value, { min: 16, max: 16384, field: 'license_key' });
  if (/\s/.test(s)) throw bad('LICENSE_KEY_INVALID');
  return s;
}

export function safeProduct(value) {
  const s = text(value || 'NEXO_ERP_PRO', { min: 1, max: 64, field: 'product' });
  if (s !== 'NEXO_ERP_PRO') throw bad('PRODUCT_NOT_ALLOWED');
  return s;
}

export function priority(value) {
  return text(value || 'Normal', { min: 1, max: 40, field: 'priority' });
}

export function category(value) {
  return text(value || 'Geral', { min: 1, max: 80, field: 'category' });
}

export function status(value) {
  const s = text(value, { min: 1, max: 40, field: 'status' });
  const allowed = new Set(['ACTIVE', 'REVOKED', 'SUSPENDED', 'TRANSFERRED', 'CLIENT_BLOCKED', 'ACTIVATION_BLOCKED']);
  if (!allowed.has(s)) throw bad('LICENSE_STATUS_INVALID');
  return s;
}

export function bad(message, statusCode = 400) {
  const e = new Error(message);
  e.status = statusCode;
  return e;
}
