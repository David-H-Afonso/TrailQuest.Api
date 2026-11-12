import assert from 'node:assert/strict';
import test from 'node:test';
import { decodeAnswers, requestOptions, resolveMirror } from './resolve-build-hosts-https.mjs';

test('decodes and deduplicates IPv4 answers without treating CNAMEs as addresses', () => {
  assert.deepEqual(decodeAnswers(JSON.stringify({ Status: 0, Answer: [
    { type: 5, data: 'mirror.example.' }, { type: 1, data: '192.0.2.10' },
    { type: 1, data: '192.0.2.10' }, { type: 1, data: '192.0.2.11' },
  ] })), ['192.0.2.10', '192.0.2.11']);
});

for (const [name, body] of [
  ['malformed JSON', 'not-json'],
  ['DNS failure', JSON.stringify({ Status: 3, Answer: [{ type: 1, data: '192.0.2.10' }] })],
  ['missing answers', JSON.stringify({ Status: 0 })],
  ['empty IPv4 answer', JSON.stringify({ Status: 0, Answer: [{ type: 5, data: 'mirror.example.' }] })],
  ['IPv6 disguised as an A record', JSON.stringify({ Status: 0, Answer: [{ type: 1, data: '2001:db8::1' }] })],
]) test(`rejects ${name}`, () => assert.throws(() => decodeAnswers(body)));

test('HTTPS bootstrap bypasses DNS while retaining hostname/certificate verification and deadlines', () => {
  const options = requestOptions('archive.ubuntu.com', '1.1.1.1');
  assert.equal(options.hostname, 'cloudflare-dns.com');
  assert.equal(options.servername, 'cloudflare-dns.com');
  assert.equal(options.rejectUnauthorized, true);
  assert.equal(options.family, 4);
  assert.equal(options.path, '/dns-query?name=archive.ubuntu.com&type=A');
  assert(options.timeout > 0);
  assert(options.signal instanceof AbortSignal);
  options.lookup('cloudflare-dns.com', {}, (error, address, family) => {
    assert.equal(error, null); assert.equal(address, '1.1.1.1'); assert.equal(family, 4);
  });
  options.lookup('cloudflare-dns.com', { all: true }, (error, addresses) => {
    assert.equal(error, null); assert.deepEqual(addresses, [{ address: '1.1.1.1', family: 4 }]);
  });
  assert.throws(() => requestOptions('unconfigured.example', '1.1.1.1'));
  assert.throws(() => requestOptions('archive.ubuntu.com', '192.0.2.10'));
});

test('falls back to the second resolver address after a connection/TLS failure', async () => {
  const calls = [];
  const addresses = await resolveMirror('security.ubuntu.com', async (host, address) => {
    calls.push({ host, address });
    if (address === '1.1.1.1') throw new Error('synthetic TLS failure');
    return ['192.0.2.10'];
  });
  assert.deepEqual(addresses, ['192.0.2.10']);
  assert.deepEqual(calls, [{ host: 'security.ubuntu.com', address: '1.1.1.1' }, { host: 'security.ubuntu.com', address: '1.0.0.1' }]);
});

test('rejects unknown mirror names without any network request', async () => {
  await assert.rejects(resolveMirror('unconfigured.example', () => assert.fail('Unexpected network request')));
});

test('fails closed when both HTTPS attempts fail', async () => {
  let calls = 0;
  await assert.rejects(resolveMirror('archive.ubuntu.com', async () => { calls++; throw new Error('synthetic unavailable resolver'); }), /Authenticated HTTPS IPv4 resolution failed/);
  assert.equal(calls, 2);
});
