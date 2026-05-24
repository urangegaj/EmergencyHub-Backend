import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE_URL, EMERGENCY_TYPE_ID } from '../../config.js';

export const options = {
  vus: 15,
  duration: '90s',
  thresholds: {
    http_req_failed: ['rate<0.05'],
  },
};

const counter200 = new Counter('assign_200');
const counter409 = new Counter('assign_409');
const counter5xx = new Counter('assign_5xx');

export function setup() {
  const loginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: 'admin@test.com', password: 'TestPass123!' }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (loginRes.status !== 200) throw new Error(`concurrent_dispatchers setup: login failed: ${loginRes.status}`);
  const adminToken = JSON.parse(loginRes.body).accessToken;

  const ids = [];
  for (let i = 0; i < 50; i++) {
    const res = http.post(
      `${BASE_URL}/api/emergencies`,
      JSON.stringify({
        emergencyTypeId: EMERGENCY_TYPE_ID,
        description: `concurrent-dispatcher-pool-${i}`,
        address: `${i} Dispatch Lane`,
      }),
      {
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${adminToken}` },
        tags: { type: 'load' },
      }
    );
    if (res.status === 201 || res.status === 200) {
      ids.push(JSON.parse(res.body).id);
    }
  }
  console.log(`[concurrent_dispatchers] pre-created ${ids.length} emergencies`);
  return { adminToken, emergencyIds: ids };
}

export default function(data) {
  const { adminToken, emergencyIds } = data;
  if (!emergencyIds || emergencyIds.length === 0) return;

  const idx = Math.floor(Math.random() * emergencyIds.length);
  const emergencyId = emergencyIds[idx];

  const res = http.post(
    `${BASE_URL}/api/emergencies/${emergencyId}/assign`,
    JSON.stringify({ departments: ['Fire', 'Police', 'Medical'] }),
    {
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${adminToken}` },
      tags: { type: 'load' },
    }
  );

  check(res, {
    'concurrent dispatchers: assign returns 200 or 409 (no 5xx)': r => r.status === 200 || r.status === 409,
  });

  if (res.status === 200)      counter200.add(1);
  else if (res.status === 409) counter409.add(1);
  else if (res.status >= 500)  counter5xx.add(1);
}
