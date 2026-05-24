import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL } from '../../config.js';

export const options = {
  scenarios: {
    cache_read: {
      executor: 'constant-arrival-rate',
      rate: 150,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 80,
      maxVUs: 150,
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<250', 'p(99)<500'],
    http_req_failed:   ['rate<0.01'],
  },
};

let cachedToken = null;

export function setup() {
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: 'dispatcher@test.com', password: 'TestPass123!' }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (res.status !== 200) throw new Error(`cache_pressure setup: login failed: ${res.status}`);
  return { dispatcherToken: JSON.parse(res.body).accessToken };
}

export default function(data) {
  if (!cachedToken) {
    cachedToken = data.dispatcherToken;
  }

  const res = http.get(`${BASE_URL}/api/dispatcher/units`, {
    headers: { 'Authorization': `Bearer ${cachedToken}` },
    tags: { type: 'load' },
  });

  check(res, { 'cache pressure: GET /api/dispatcher/units returns 200': r => r.status === 200 });
}
