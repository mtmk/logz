import * as net from 'net'
import * as os from 'os'

const SERVER_HOST = process.env.LOGZ_HOST ?? '127.0.0.1'
const SERVER_PORT = parseInt(process.env.LOGZ_PORT ?? '12345', 10)
const HOSTNAME = os.hostname()

type QueueEntry = { srcLine: string; message: string }

const queue: QueueEntry[] = []
let socket: net.Socket | null = null
let connecting = false
let draining = false

function ensureConnection() {
  if (socket || connecting) return
  connecting = true

  const s = new net.Socket()
  s.setEncoding('utf-8')

  s.connect(SERVER_PORT, SERVER_HOST, () => {
    socket = s
    connecting = false
    drain()
  })

  s.on('error', () => {
    socket = null
    connecting = false
    s.destroy()
    // Retry after delay
    setTimeout(ensureConnection, 1000)
  })

  s.on('close', () => {
    socket = null
    connecting = false
  })
}

function drain() {
  if (draining || !socket) return
  draining = true

  while (queue.length > 0 && socket) {
    const entry = queue.shift()!
    const escaped = entry.message
      .replace(/\r\n/g, '\n')
      .split('\n')
      .map((line) => (line === '__END__' ? '__END____END__' : line))
      .join('\n')

    try {
      socket.write(`${entry.srcLine}\n${escaped}\n__END__\n`)
    } catch {
      // Re-queue on write failure
      queue.unshift(entry)
      socket?.destroy()
      socket = null
      break
    }
  }

  draining = false
}

export function logz(src: string, message: string) {
  const ts = new Date().toLocaleTimeString('en-GB', { hour12: false })
  const srcLine = `${ts} ${HOSTNAME} ${src}`

  // Cap queue size
  if (queue.length < 1000) {
    queue.push({ srcLine, message })
  }

  ensureConnection()
  drain()
}
