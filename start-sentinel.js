const { spawn, execSync } = require('child_process');
const http = require('http');

// Colors for console logging
const colors = {
    reset: "\x1b[0m",
    api: "\x1b[36m",    // Cyan
    ngrok: "\x1b[35m",  // Magenta
    cli: "\x1b[32m",    // Green
    web: "\x1b[33m",    // Yellow/Gold
    error: "\x1b[31m"   // Red
};

function httpGet(url) {
    return new Promise((resolve, reject) => {
        const request = http.get(url, (res) => {
            const chunks = [];
            res.on('data', (chunk) => chunks.push(chunk));
            res.on('end', () => {
                resolve({
                    statusCode: res.statusCode || 0,
                    body: Buffer.concat(chunks).toString('utf8')
                });
            });
        });

        request.on('error', reject);
        request.setTimeout(2000, () => {
            request.destroy(new Error(`Timed out connecting to ${url}`));
        });
    });
}

async function waitForService(url, label, timeoutMs = 45000) {
    const startedAt = Date.now();

    while (Date.now() - startedAt < timeoutMs) {
        try {
            const response = await httpGet(url);
            if (response.statusCode >= 200 && response.statusCode < 500) {
                log(label, `Healthy at ${url}`, colors.reset);
                return true;
            }
        } catch (error) {
            // Keep polling until timeout so local startup races do not break auth.
        }

        await new Promise((resolve) => setTimeout(resolve, 1500));
    }

    log(label, `Timed out waiting for ${url}`, colors.error);
    return false;
}

function log(service, message, color) {
    const timestamp = new Date().toLocaleTimeString();
    // Only print strings that aren't purely empty whitespace
    if (message.trim().length > 0) {
        console.log(`${color}[${timestamp}] [${service}]${colors.reset} ${message}`);
    }
}

async function start() {
    const isVerbose = process.argv.includes('--verbose') || process.argv.includes('-v');

    console.log(`${colors.api}--- Starting Draco Autonomous Sentinel System ---${colors.reset}`);
    console.log(`Mode: ${isVerbose ? 'Verbose (All Logs)' : 'Quiet (Errors/Essential Logs)'}`);
    console.log(`Use 'bun start-sentinel.js --verbose' to see all SQL queries.\n`);

    // 1. Cleanup existing processes
    try {
        log("System", "Cleaning up ports 5020, 5173, 4321 and stale tunnels...", colors.reset);
        execSync("lsof -ti:5020,5173,4321 | xargs kill -9 2>/dev/null || true");
        execSync("killall ngrok 2>/dev/null || true");
        // Give ngrok and system sockets a moment to realize the session is closed
        await new Promise(r => setTimeout(r, 2000));
    } catch (e) { }

    // Prepare Environment Variables for .NET to mute verbose EF Core SQL queries
    const dotnetEnv = { ...process.env };
    if (!isVerbose) {
        // Suppress Microsoft and EF Core spam unless verbose mode is on
        dotnetEnv['Logging__LogLevel__Microsoft'] = 'Warning';
        dotnetEnv['Logging__LogLevel__Microsoft.Hosting.Lifetime'] = 'Information'; // Keep start/stop events
    }

    // 2. Start Draco API
    log("API", "Launching Backend...", colors.api);
    const apiProc = spawn('dotnet', ['run'], {
        cwd: './src/Draco.Api',
        shell: true,
        env: dotnetEnv
    });

    apiProc.stdout.on('data', (data) => log("API", data.toString().trim(), colors.api));
    apiProc.stderr.on('data', (data) => log("API-ERR", data.toString().trim(), colors.error));

    await waitForService('http://127.0.0.1:5020/health', 'API');

    // 3. Start Draco UI (React/Vite)
    log("WEB", "Launching React UI (Vite)...", colors.web);
    const webProc = spawn('bun', ['dev'], {
        cwd: './src/Draco.Web',
        shell: true
    });

    webProc.stdout.on('data', (data) => log("WEB", data.toString().trim(), colors.web));
    webProc.stderr.on('data', (data) => log("WEB-ERR", data.toString().trim(), colors.error));

    // 4. Start Ngrok
    log("Ngrok", "Launching tunnel...", colors.ngrok);
    const ngrokProc = spawn('/opt/homebrew/bin/ngrok', ['http', '5020', '--log=stdout'], { shell: true });

    ngrokProc.stderr.on('data', (data) => log("Ngrok-ERR", data.toString().trim(), colors.error));

    // Retry logic to fetch the public URL from ngrok
    const fetchUrl = () => {
        http.get('http://127.0.0.1:4040/api/tunnels', (res) => {
            let data = '';
            res.on('data', (chunk) => data += chunk);
            res.on('end', () => {
                try {
                    const json = JSON.parse(data);
                    if (json.tunnels && json.tunnels.length > 0) {
                        const url = json.tunnels[0].public_url;
                        console.log(`\n${colors.ngrok}==========================================${colors.reset}`);
                        console.log(`${colors.ngrok} LIVE TWILIO WEBHOOK URL: ${url}/api/webhooks/twilio/messages ${colors.reset}`);
                        console.log(`${colors.ngrok}==========================================${colors.reset}\n`);
                    } else {
                        setTimeout(fetchUrl, 2000);
                    }
                } catch (e) {
                    setTimeout(fetchUrl, 2000);
                }
            });
        }).on("error", () => {
            setTimeout(fetchUrl, 2000);
        });
    };
    fetchUrl();

    // 5. Start Draco CLI Sentinel - Delayed slightly to prevent .NET MSBuild file lock collisions
    let cliProc;
    setTimeout(() => {
        log("CLI", "Initializing monitoring loop...", colors.cli);
        cliProc = spawn('dotnet', ['run', '--', 'start'], {
            cwd: './src/Draco.Cli',
            shell: true,
            env: dotnetEnv
        });

        cliProc.stdout.on('data', (data) => log("CLI", data.toString().trim(), colors.cli));
        cliProc.stderr.on('data', (data) => log("CLI-ERR", data.toString().trim(), colors.error));
    }, 4000);

    // Handle process termination
    process.on('SIGINT', () => {
        log("System", "Shutting down Draco Services...", colors.reset);
        apiProc.kill();
        webProc.kill();
        ngrokProc.kill();
        if (cliProc) cliProc.kill();
        process.exit();
    });
}

start();
