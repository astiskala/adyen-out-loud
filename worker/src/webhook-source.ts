const DNS_QUERY_URL = "https://cloudflare-dns.com/dns-query";
const RECORD_TYPES = ["A", "AAAA"] as const;
const MIN_CACHE_MS = 60_000;
const MAX_CACHE_MS = 300_000;

interface DnsAnswer {
  type: number;
  TTL: number;
  data: string;
}

interface DnsResponse {
  Status: number;
  Answer?: DnsAnswer[];
}

interface CachedAddresses {
  addresses: Set<string>;
  expiresAt: number;
}

let cache: CachedAddresses | undefined;
let pending: Promise<CachedAddresses> | undefined;

async function query(hostname: string, type: (typeof RECORD_TYPES)[number]): Promise<DnsAnswer[]> {
  const response = await fetch(`${DNS_QUERY_URL}?name=${encodeURIComponent(hostname)}&type=${type}`, {
    headers: { accept: "application/dns-json" },
  });
  if (!response.ok) throw new Error(`DNS lookup failed with ${response.status}`);
  const body = await response.json<DnsResponse>();
  if (body.Status !== 0) throw new Error(`DNS lookup failed with status ${body.Status}`);
  const wanted = type === "A" ? 1 : 28;
  return (body.Answer ?? []).filter((answer) => answer.type === wanted);
}

async function lookup(hostname: string): Promise<CachedAddresses> {
  const answers = (await Promise.all(RECORD_TYPES.map((type) => query(hostname, type)))).flat();
  if (answers.length === 0) throw new Error("DNS lookup returned no addresses");
  const ttlMs = Math.min(...answers.map((answer) => answer.TTL)) * 1000;
  return {
    addresses: new Set(answers.map((answer) => answer.data.toLowerCase())),
    expiresAt: Date.now() + Math.min(Math.max(ttlMs, MIN_CACHE_MS), MAX_CACHE_MS),
  };
}

async function addressesOf(hostname: string): Promise<Set<string>> {
  if (cache && cache.expiresAt > Date.now()) return cache.addresses;
  pending ??= lookup(hostname)
    .then((fresh) => (cache = fresh))
    .finally(() => {
      pending = undefined;
    });
  return (await pending).addresses;
}

/**
 * Whether `clientIp` (Cloudflare's `CF-Connecting-IP`) is one of the addresses `hostname` currently
 * resolves to. Adyen publishes its webhook sender addresses as the DNS records of `out.adyen.com`
 * and may change them, so they are resolved over DNS-over-HTTPS and cached briefly per isolate.
 * Rejects if the lookup fails, so callers can fail closed.
 * @param {string} hostname - Host whose DNS records are the allowed addresses.
 * @param {string | null} clientIp - The connecting client address, or null when unknown.
 * @returns {Promise<boolean>} True when the client address is one of the host's addresses.
 */
export async function isWebhookSource(hostname: string, clientIp: string | null): Promise<boolean> {
  const addresses = await addressesOf(hostname);
  return clientIp !== null && addresses.has(clientIp.toLowerCase());
}
