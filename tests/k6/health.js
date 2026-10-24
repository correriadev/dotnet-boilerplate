import http from 'k6/http';
import { check, sleep } from 'k6';
import { baseUrl, options as configuredOptions } from './lib/config.js';

export const options = configuredOptions;
export { handleSummary } from './lib/summary.js';

export default function () {
  const url = `${baseUrl}/health`;
  const params = {
    redirects: 0,
    timeout: '10s',
    headers: {
      Accept: 'application/json',
      'User-Agent': 'k6-load-tester/1.0',
    },
    tags: {
      name: 'GET /health',
    },
  };

  const response = http.get(url, params);

  let body = null;
  let validJson = false;

  try {
    body = response.json();
    validJson = true;
  } catch {
    validJson = false;
  }

  check(response, {
    'HTTP 200 OK': (r) => r.status === 200,
    'Content-Type JSON': (r) => {
      const contentType = String(r.headers['Content-Type'] || r.headers['content-type'] || '');
      return contentType.toLowerCase().includes('application/json');
    },
    'JSON válido': () => validJson,
    'Status Healthy': () =>
      body !== null &&
      typeof body === 'object' &&
      !Array.isArray(body) &&
      body.status === 'Healthy',
    'Latência unitária < 1000ms': (r) => r.timings.duration < 1000,
  });

  // Intervalo controlado para simular cadência realista entre iterações por VU
  sleep(1);
}

