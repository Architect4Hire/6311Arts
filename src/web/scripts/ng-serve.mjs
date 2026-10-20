// Starts `ng serve <project>` on the port Aspire assigned (aspire.md: no fixed ports in the repo).
//
// AddJavaScriptApp hands the port over as the PORT environment variable, which the Angular CLI does not read, and
// an npm script cannot expand it portably (`$PORT` in sh, `%PORT%` in cmd). This is the one place that bridges the
// two. Run it through `npm run start:site` / `start:admin`, which Aspire does; never by hand (CLAUDE.md).
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';

const [project, ...rest] = process.argv.slice(2);
if (!project) {
  console.error('usage: node scripts/ng-serve.mjs <project> [ng serve options]');
  process.exit(2);
}

const port = process.env.PORT;
if (port !== undefined && !/^\d{1,5}$/.test(port)) {
  console.error(`PORT must be a port number, got '${port}'.`);
  process.exit(2);
}

const ng = createRequire(import.meta.url).resolve('@angular/cli/bin/ng.js');

// No live reload or HMR: the browser reaches the dev server only through the gateway, which does not carry Vite's
// websocket (the handshake fails), and the client's fallback to the dev server's own port is refused by the CSP's
// connect-src 'self' — as it should be. Changes still rebuild; refresh the page to see them (ADR 0027).
const args = [
  ng,
  'serve',
  project,
  ...(port ? ['--port', port] : []),
  '--live-reload=false',
  '--hmr=false',
  ...rest,
];

const child = spawn(process.execPath, args, { stdio: 'inherit' });

// Aspire stops the resource by signalling this process; pass it on so the dev server does not outlive it.
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => child.kill(signal));
}

child.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
