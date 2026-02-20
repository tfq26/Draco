const { spawn, execSync } = require('child_process');
const http = require('http');

// Colors for console logging
const colors = {
    reset: "\x1b[0m",
    api: "\x1b[36m",    // Cyan
    ngrok: "\x1b[35m",  // Magenta
    cli: "\x1b[32m",    // Green
    error: "\x1b[31m"   // Red
};

function log(service, message, color) {
    const timestamp = new Date().toLocaleTimeString();
    console.log(`${color}[${timestamp}] [${service}]${colors.reset} ${message}`);
}

async function start() {
    console.log(`${colors.api}--- Starting Draco Autonomous Sentinel System ---${colors.reset}\n`);

    // 1. Cleanup existing processes on port 5020
    try {
        log("System", "Cleaning up port 5020...", colors.reset);
        execSync("lsof -ti:5020 | xargs kill -9 2>/dev/null || true");
    } catch (e) { }

    // 2. Start Draco API
    const apiProc = spawn('dotnet', ['run'], {
        cwd: './src/Draco.Api',
        shell: true
    });

    apiProc.stdout.on('data', (data) => log("API", data.toString().trim(), colors.api));
    apiProc.stderr.on('data', (data) => log("API-ERR", data.toString().trim(), colors.error));

    // 3. Start Ngrok
    log("Ngrok", "Launching tunnel...", colors.ngrok);
    const ngrokProc = spawn('/opt/homebrew/bin/ngrok', ['http', '5020'], { shell: true });

    ngrokProc.stderr.on('data', (data) => log("Ngrok-ERR", data.toString().trim(), colors.error));

    // Retry logic to fetch the URL
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
                        console.log(`${colors.ngrok} LIVE WEBHOOK URL: ${url}/api/webhook/twilio ${colors.reset}`);
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

    // 4. Start Draco CLI Sentinel
    log("CLI", "Initializing monitoring loop...", colors.cli);
    const cliProc = spawn('dotnet', ['run', '--', 'start'], {
        cwd: './src/Draco.Cli',
        shell: true
    });

    cliProc.stdout.on('data', (data) => log("CLI", data.toString().trim(), colors.cli));
    cliProc.stderr.on('data', (data) => log("CLI-ERR", data.toString().trim(), colors.error));

    // Handle process termination
    process.on('SIGINT', () => {
        log("System", "Shutting down Draco...", colors.reset);
        apiProc.kill();
        ngrokProc.kill();
        cliProc.kill();
        process.exit();
    });
}

start();
