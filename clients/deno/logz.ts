// ── Edit these defaults or set LOGZ_HOST / LOGZ_PORT env vars ──
const DEFAULT_HOST = "127.0.0.1"
const DEFAULT_PORT = 12345
// ────────────────────────────────────────────────────────────────

const LOGZ_HOST = Deno.env.get("LOGZ_HOST") ?? DEFAULT_HOST
const LOGZ_PORT = parseInt(Deno.env.get("LOGZ_PORT") ?? String(DEFAULT_PORT), 10)

export async function logz(source: string, message: string): Promise<void> {
  console.log(`[logz] attempting to send: ${source} - ${message}`)
  try {
    const timestamp = new Date().toISOString().slice(11, 19)
    const hostname = Deno.hostname()
    const srcLine = `${timestamp} ${hostname} ${source}`
    console.log(`[logz] connecting to ${LOGZ_HOST}:${LOGZ_PORT}`)
    const conn = await Deno.connect({ hostname: LOGZ_HOST, port: LOGZ_PORT })
    const encoder = new TextEncoder()
    const data = `${srcLine}\n${message}\n__END__\n`
    await conn.write(encoder.encode(data))
    try { await conn.writable.close() } catch { /* ignore */ }
    conn.close()
    console.log(`[logz] sent successfully`)
  } catch (e) {
    console.error(`[logz] failed:`, e)
  }
}
