import https from 'node:https';
import { isIP } from 'node:net';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const mirrors = new Set(['archive.ubuntu.com', 'security.ubuntu.com']);
const bootstrapAddresses = ['1.1.1.1', '1.0.0.1'];

export function decodeAnswers(body) {
  const response = JSON.parse(body);
  if (response.Status !== 0 || !Array.isArray(response.Answer)) throw new Error('HTTPS DNS returned no successful answer');
  const addresses = [...new Set(response.Answer.filter(answer => answer.type === 1).map(answer => answer.data))];
  if (!addresses.length || addresses.some(address => isIP(address) !== 4)) throw new Error('HTTPS DNS returned invalid or missing IPv4 answers');
  return addresses;
}

export function requestOptions(host, address) {
  if (!mirrors.has(host) || !bootstrapAddresses.includes(address)) throw new Error('Only official Ubuntu mirrors and the configured HTTPS resolver are allowed');
  return {
    hostname: 'cloudflare-dns.com', servername: 'cloudflare-dns.com',
    path: `/dns-query?name=${encodeURIComponent(host)}&type=A`,
    headers: { accept: 'application/dns-json' },
    family: 4, rejectUnauthorized: true, timeout: 8000,
    signal: AbortSignal.timeout(10000),
    // Bootstrap the public HTTPS resolver without consulting the broken DNS.
    // TLS still verifies cloudflare-dns.com; this is not a mirror-IP pin.
    lookup: (_name, options, done) => options.all
      ? done(null, [{ address, family: 4 }]) : done(null, address, 4),
  };
}

function queryMirror(host, address) {
  return new Promise((accept, reject) => {
    const request = https.get(requestOptions(host, address), response => {
      if (response.statusCode !== 200) { response.resume(); reject(new Error(`HTTPS DNS returned HTTP ${response.statusCode}`)); return; }
      const chunks = [];
      let size = 0;
      response.on('data', chunk => {
        size += chunk.length;
        if (size > 65536) request.destroy(new Error('HTTPS DNS response exceeds the size limit'));
        else chunks.push(chunk);
      });
      response.on('error', reject);
      response.on('end', () => {
        try { accept(decodeAnswers(Buffer.concat(chunks).toString('utf8'))); }
        catch (error) { reject(error); }
      });
    });
    request.on('timeout', () => request.destroy(new Error('HTTPS DNS connection timed out')));
    request.on('error', reject);
  });
}

export async function resolveMirror(host, query = queryMirror) {
  if (!mirrors.has(host)) throw new Error('Only official Ubuntu mirror names are allowed');
  let failure;
  for (const address of bootstrapAddresses) {
    try { return await query(host, address); }
    catch (error) { failure = error; }
  }
  throw new Error(`Authenticated HTTPS IPv4 resolution failed for ${host}: ${failure?.message}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { process.stdout.write((await resolveMirror(process.argv[2])).join('\n') + '\n'); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
