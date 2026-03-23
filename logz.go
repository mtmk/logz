package logz

import (
	"bufio"
	"fmt"
	"net"
	"os"
	"strings"
	"sync"
	"time"
)

var (
	serverAddr = getEnvOrDefault("LOGZ_ADDR", "127.0.0.1:12345")
	hostname   string
	logChan    = make(chan logEntry, 1000)
	once       sync.Once
)

type logEntry struct {
	srcLine string
	message string
}

func getEnvOrDefault(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func init() {
	hostname, _ = os.Hostname()
	go backgroundSender()
}

// Log sends a message to the logz server
func Log(src, message string) {
	srcLine := fmt.Sprintf("%s %s %s", time.Now().Format("15:04:05"), hostname, src)
	select {
	case logChan <- logEntry{srcLine: srcLine, message: message}:
	default:
		// Channel full, drop newest (don't block)
	}
}

func backgroundSender() {
	var conn net.Conn
	var writer *bufio.Writer

	for {
		// Connect if not connected
		if conn == nil {
			var err error
			conn, err = net.DialTimeout("tcp", serverAddr, 5*time.Second)
			if err != nil {
				time.Sleep(1 * time.Second)
				continue
			}
			writer = bufio.NewWriter(conn)
		}

		// Read from channel
		entry := <-logChan

		err := sendMessage(writer, entry.srcLine, entry.message)
		if err != nil {
			// Connection lost, close and retry
			conn.Close()
			conn = nil
			writer = nil
			// Re-queue the message (non-blocking)
			select {
			case logChan <- entry:
			default:
			}
			time.Sleep(1 * time.Second)
		}
	}
}

func sendMessage(writer *bufio.Writer, srcLine, message string) error {
	// Write source line
	if _, err := writer.WriteString(srcLine + "\n"); err != nil {
		return err
	}

	// Write message lines, escaping __END__
	normalized := strings.ReplaceAll(strings.ReplaceAll(message, "\r\n", "\n"), "\r", "\n")
	lines := strings.Split(normalized, "\n")
	for _, line := range lines {
		if line == "__END__" {
			line = "__END____END__"
		}
		if _, err := writer.WriteString(line + "\n"); err != nil {
			return err
		}
	}

	// Write terminator and flush
	if _, err := writer.WriteString("__END__\n"); err != nil {
		return err
	}
	return writer.Flush()
}
