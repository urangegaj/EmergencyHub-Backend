import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, EMERGENCY_TYPE_ID } from '../../config.js';

export const options = {
  scenarios: {
    spike: {
      executor: 'ramping-arrival-rate',
      startRate: 2,
      timeUnit: '1s',
      preAllocatedVUs: 100,
      maxVUs: 200,
      stages: [
        { duration: '15s', target: 2  },
        { duration: '10s', target: 60 },
        { duration: '30s', target: 60 },
        { duration: '15s', target: 2  },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<1500', 'p(99)<3000'],
    http_req_failed:   ['rate<0.03'],
  },
};

let cachedToken = null;

export function setup() {
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: 'admin@test.com', password: 'TestPass123!' }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (res.status !== 200) throw new Error(`spike setup: login failed: ${res.status}`);
  return { adminToken: JSON.parse(res.body).accessToken };
}

export default function(data) {
  if (!cachedToken) {
    cachedToken = data.adminToken;
  }

  const res = http.post(
    `${BASE_URL}/api/emergencies`,
    JSON.stringify({
      emergencyTypeId: EMERGENCY_TYPE_ID,
      description: `spike-test-${Date.now()}-${__VU}`,
      address: `${__VU} Spike Street`,
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${cachedToken}`,
      },
      tags: { type: 'load' },
    }
  );

  check(res, { 'spike: POST /api/emergencies returns 201': r => r.status === 201 || r.status === 200 });
}
