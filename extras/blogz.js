const http = require("http");
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");
const WebSocket = require("ws");

// ── Edit these defaults or set env vars ──────────────────────
const DEFAULT_LOG_DIR = "./logz-data";
const DEFAULT_CHROMIUM_PATH = "chrome";
const DEFAULT_CHROMIUM_DATA_DIR = "./blogz-profiles";
// ─────────────────────────────────────────────────────────────

const LOG_DIR = process.env.LOGZ_DIR || DEFAULT_LOG_DIR;
const CHROMIUM_PATH = process.env.LOGZ_CHROMIUM_PATH || DEFAULT_CHROMIUM_PATH;
const CHROMIUM_DATA_DIR = process.env.LOGZ_CHROMIUM_DATA_DIR || DEFAULT_CHROMIUM_DATA_DIR;

const CDP_DOMAINS = [
  "Network.enable",
  "Page.enable",
  "Runtime.enable",
  "Console.enable",
  "Log.enable",
  "DOM.enable",
  "Fetch.enable",
];

function getDebuggerUrl(port) {
  return new Promise((resolve, reject) => {
    http
      .get(`http://127.0.0.1:${port}/json/version`, (res) => {
        let data = "";
        res.on("data", (chunk) => (data += chunk));
        res.on("end", () => {
          try {
            const info = JSON.parse(data);
            resolve(info.webSocketDebuggerUrl);
          } catch (e) {
            reject(new Error(`Failed to parse /json/version: ${e.message}`));
          }
        });
      })
      .on("error", (e) =>
        reject(
          new Error(
            `Cannot connect to Chrome on port ${port}. Is it running with --remote-debugging-port=${port}? (${e.message})`
          )
        )
      );
  });
}

function getTargets(port) {
  return new Promise((resolve, reject) => {
    http
      .get(`http://127.0.0.1:${port}/json`, (res) => {
        let data = "";
        res.on("data", (chunk) => (data += chunk));
        res.on("end", () => {
          try {
            resolve(JSON.parse(data));
          } catch (e) {
            reject(new Error(`Failed to parse /json: ${e.message}`));
          }
        });
      })
      .on("error", reject);
  });
}

function timestamp() {
  return new Date().toISOString();
}

function log(logFile, text) {
  fs.appendFileSync(logFile, text);
}

function connectAndLog(wsUrl, logFile, label) {
  const ws = new WebSocket(wsUrl);
  let msgId = 1;

  log(logFile, `\n--- Session started: ${timestamp()} ---\n`);
  log(logFile, `--- Target: ${label} ---\n`);
  log(logFile, `--- WebSocket: ${wsUrl} ---\n\n`);

  ws.on("open", () => {
    console.log(`  [connected] ${label}`);

    // Enable CDP domains
    for (const method of CDP_DOMAINS) {
      const msg = JSON.stringify({ id: msgId++, method });
      ws.send(msg);
    }
  });

  ws.on("message", (data) => {
    const line = `[${timestamp()}] ${data}`;
    log(logFile, line + "\n");

    // console.log(line);

    // Fetch.requestPaused requires us to continue the request, otherwise Chrome blocks
    try {
      const msg = JSON.parse(data);
      if (msg.method === "Fetch.requestPaused") {
        ws.send(JSON.stringify({
          id: msgId++,
          method: "Fetch.continueRequest",
          params: { requestId: msg.params.requestId },
        }));
      }
    } catch (_) {}
  });

  ws.on("close", () => {
    log(logFile, `\n--- Session closed: ${timestamp()} ---\n`);
    console.log(`  [closed] ${label}`);
  });

  ws.on("error", (err) => {
    log(logFile, `\n--- Error: ${err.message} ---\n`);
    console.error(`  [error] ${label}: ${err.message}`);
  });

  return ws;
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main() {
  const n = parseInt(process.argv[2], 10);
  if (!n || n < 1 || n > 9) {
    console.error(
      "Usage: node index.js <1-9>\n\n" +
        "  Launches Chromium with --remote-debugging-port=922N\n" +
        "  and --user-data-dir=./blogz-profiles/browserN\n" +
        "  then connects and logs all CDP messages.\n\n" +
        "  Example: node index.js 3\n" +
        "    -> port 9223, data dir browser3\n"
    );
    process.exit(1);
  }

  const port = 9220 + n;
  const userDataDir = path.join(CHROMIUM_DATA_DIR, `browser${n}`);
  const logFile = path.join(LOG_DIR, `log-browser-${port}.txt`);

  console.log(`blogz - CDP logger`);
  console.log(`Instance: browser${n}`);
  console.log(`Port:     ${port}`);
  console.log(`Data:     ${userDataDir}`);
  console.log(`Log:      ${logFile}`);
  console.log();

  // Launch browser
  const args = [
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${userDataDir}`,
  ];
  console.log(`> "${CHROMIUM_PATH}" ${args.join(" ")}`);
  const child = spawn(CHROMIUM_PATH, args, { stdio: "ignore" });

  // Wait for browser to start
  console.log(`Waiting for browser to start...\n`);
  await sleep(3000);

  // Connect
  try {
    const browserWsUrl = await getDebuggerUrl(port);
    console.log(`Browser WebSocket: ${browserWsUrl}`);
    var browserWs = connectAndLog(browserWsUrl, logFile, `browser:${port}`);

    const targets = await getTargets(port);
    const pages = targets.filter(
      (t) => t.type === "page" && t.webSocketDebuggerUrl
    );
    console.log(`Found ${pages.length} page target(s)`);
    for (const page of pages) {
      const label = page.title || page.url || page.id;
      connectAndLog(page.webSocketDebuggerUrl, logFile, label);
    }
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }

  console.log(`\nLogging... press Ctrl+C to stop.\n`);

  process.on("SIGINT", () => {
    console.log("\nClosing browser gracefully...");
    if (browserWs && browserWs.readyState === WebSocket.OPEN) {
      browserWs.send(JSON.stringify({ id: 99999, method: "Browser.close" }));
      setTimeout(() => process.exit(0), 2000);
    } else {
      process.exit(0);
    }
  });
}

main();
