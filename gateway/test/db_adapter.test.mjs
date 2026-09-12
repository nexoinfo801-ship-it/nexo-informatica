import test from 'node:test';
import assert from 'node:assert/strict';
import { databaseConfigured, databaseHealth, databaseQuery } from '../db.mjs';

test('database adapter fails closed when DATABASE_URL is absent', async () => {
  assert.equal(databaseConfigured(), false);
  assert.deepEqual(await databaseHealth(), { configured: false, reachable: false });
  await assert.rejects(() => databaseQuery('SELECT 1'), /DATABASE_NOT_CONFIGURED/);
});
