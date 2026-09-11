import crypto from 'node:crypto';

export function sha256Hex(value) {
  return crypto.createHash('sha256').update(String(value), 'utf8').digest('hex');
}

export function stable(value) {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.keys(value).sort().map((k) => [k, stable(value[k])]));
  }
  return value;
}

export function signCompact(payload, privatePem) {
  const raw = JSON.stringify(stable(payload));
  const b64 = Buffer.from(raw, 'utf8').toString('base64url');
  const sig = crypto.sign('sha256', Buffer.from(b64, 'utf8'), {
    key: privatePem,
    dsaEncoding: 'ieee-p1363',
  });
  if (sig.length !== 64) throw new Error('STATUS_SIGNATURE_INVALID');
  return `${b64}.${sig.toString('base64url')}`;
}

export function decodePemB64(value) {
  if (!value) return '';
  return Buffer.from(String(value), 'base64').toString('utf8');
}

export function uuid() {
  return crypto.randomUUID();
}
