package logz

import (
	"fmt"
	"net"
	"os"
	"time"
)

// ── Edit this default if env vars aren't available (e.g. containers, ──
// ── remote hosts). Otherwise set LOGZ_ADDR env var.                  ──
const defaultAddr = "127.0.0.1:12345"

// Log sends a message to the logzd TCP server.
// It connects, sends the message, and disconnects each time.
// Failures are silently ignored to avoid disrupting the operator.
func Log(source, message string) {
	addr := os.Getenv("LOGZ_ADDR")
	if addr == "" {
		addr = defaultAddr
	}

	fmt.Printf("[logz] attempting to send to %s: %s\n", addr, source)
	conn, err := net.DialTimeout("tcp", addr, 2*time.Second)
	if err != nil {
		fmt.Printf("[logz] dial failed: %v\n", err)
		return
	}

	_ = conn.SetWriteDeadline(time.Now().Add(2 * time.Second))
	_, err = fmt.Fprintf(conn, "%s\n%s\n__END__\n", source, message)
	if err != nil {
		fmt.Printf("[logz] write failed: %v\n", err)
	} else {
		fmt.Printf("[logz] sent successfully\n")
	}

	// Graceful shutdown - signal we're done writing
	if tc, ok := conn.(*net.TCPConn); ok {
		tc.CloseWrite()
	}
	conn.Close()
}
