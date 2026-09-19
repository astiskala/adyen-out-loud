const DNS_QUERY_URL = "https://cloudflare-dns.com/dns-query";
const RECORD_TYPES = ["A", "AAAA"] as const;
const MIN_TTL = 60_000;
const MAX_TTL = 300_000;

interface DnsAns {
  type: number;
  TTL: number;
  data: string;
}
interface DnsRes {
  Status: number;
  Answer?: DnsAns[];
}
interface Cache {
  addrs: Set<string>;
  exp: number;
}

let cache: Cache | undefined;
let pending: Promise<Cache> | undefined;

async function query(host: string, type: string): Promise<DnsAns[]> {
  const res = await fetch(`${DNS_QUERY_URL}?name=${encodeURIComponent(host)}&type=${type}`, {
    headers: { accept: "application/dns-json" },
  });
  if (!res.ok) throw new Error(`DNS ${res.status}`);
  const b = await res.json<DnsRes>();
  if (b.Status !== 0) throw new Error(`DNS status ${b.Status}`);
  const want = type === "A" ? 1 : 28;
  return (b.Answer ?? []).filter((a) => a.type === want);
}

async function lookup(host: string): Promise<Cache> {
  const ans = (await Promise.all(RECORD_TYPES.map((t) => query(host, t)))).flat();
  if (!ans.length) throw new Error("DNS empty");
  const ttl = Math.min(...ans.map((a) => a.TTL)) * 1000;
  return {
    addrs: new Set(ans.map((a) => a.data.toLowerCase())),
    exp: Date.now() + Math.min(Math.max(ttl, MIN_TTL), MAX_TTL),
  };
}

async function addrs(host: string): Promise<Set<string>> {
  if (cache && cache.exp > Date.now()) return cache.addrs;
  pending ??= lookup(host)
    .then((f) => (cache = f))
    .finally(() => (pending = undefined));
  return (await pending).addrs;
}

/**
 * Checks if client IP matches Adyen's DNS-published webhook sender IPs.
 * @param {string} hostname - The host whose A/AAAA records list the allowed senders.
 * @param {string | null} clientIp - The caller's address (CF-Connecting-IP).
 * @returns {Promise<boolean>} True if the address is one of the host's.
 */
export async function isWebhookSource(
  /** Host whose DNS records are the allowed addresses. */
  hostname: string,
  /** The connecting client address, or null when unknown. */
  clientIp: string | null,
): Promise</** True when the client address is one of the host's addresses. */ boolean> {
  return clientIp !== null && (await addrs(hostname)).has(clientIp.toLowerCase());
}
