// Dependency-free load test (Node 18+):  node ops/load-test.js http://localhost:5129 50 20
// args: base URL, concurrent clients, seconds.  Hits the read paths a shopper uses and reports latency.
const [base = 'http://localhost:5129', clientsArg = '50', secondsArg = '20'] = process.argv.slice(2);
const clients = Number(clientsArg);
const seconds = Number(secondsArg);

const paths = [
  '/api/v1/home',
  '/api/v1/products?categorySlug=smartphones',
  '/api/v1/products?categorySlug=laptops&sort=price-asc',
  '/api/v1/categories',
  '/api/v1/categories/menu',
  '/api/v1/brands'
];

const latencies = [];
let errors = 0;
const stopAt = Date.now() + seconds * 1000;

async function worker() {
  while (Date.now() < stopAt) {
    const path = paths[Math.floor(Math.random() * paths.length)];
    const started = performance.now();
    try {
      const res = await fetch(base + path);
      await res.arrayBuffer();
      if (!res.ok) errors++;
    } catch {
      errors++;
    }
    latencies.push(performance.now() - started);
  }
}

await Promise.all(Array.from({ length: clients }, worker));

latencies.sort((a, b) => a - b);
const pct = (p) => latencies[Math.min(latencies.length - 1, Math.floor(latencies.length * p))].toFixed(0);
console.log(`requests: ${latencies.length}  errors: ${errors}  rps: ${(latencies.length / seconds).toFixed(0)}`);
console.log(`latency ms  p50: ${pct(0.5)}  p95: ${pct(0.95)}  p99: ${pct(0.99)}  max: ${latencies[latencies.length - 1].toFixed(0)}`);
process.exit(errors > latencies.length * 0.01 ? 1 : 0);
