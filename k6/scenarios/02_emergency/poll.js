import { check, sleep } from 'k6';
import http from 'k6/http';
import { BASE_URL, EMERGENCY_TYPE_ID } from '../../config.js';

export const options = {
  scenarios: {
    poll_test: {
      executor: 'shared-iterations',
      vus: 2,
      iterations: 4,
    },
  },
};

function rawLogin(email, password) {
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (res.status !== 200) throw new Error(`poll setup login failed: ${res.status}`);
  return JSON.parse(res.body).accessToken;
}

function rawPost(path, token, body) {
  return http.post(
    `${BASE_URL}${path}`,
    body ? JSON.stringify(body) : null,
    { headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` }, tags: { type: 'functional' } }
  );
}

function rawGet(path, token) {
  return http.get(
    `${BASE_URL}${path}`,
    { headers: { 'Authorization': `Bearer ${token}` }, tags: { type: 'functional' } }
  );
}

export function setup() {
  const adminToken = rawLogin('admin@test.com', 'TestPass123!');

  const em1Res = rawPost('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: 'poll test - timeout sentinel',
    address: '1 Poll Ave',
  });
  if (em1Res.status !== 201 && em1Res.status !== 200) throw new Error(`poll: create em1 failed: ${em1Res.status}`);
  const em1 = JSON.parse(em1Res.body);
  console.log(`[emergency_poll] timeout sentinel id=${em1.id}`);

  const em2Res = rawPost('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: 'poll test - wakeup sentinel',
    address: '2 Poll Ave',
  });
  if (em2Res.status !== 201 && em2Res.status !== 200) throw new Error(`poll: create em2 failed: ${em2Res.status}`);
  const em2 = JSON.parse(em2Res.body);
  console.log(`[emergency_poll] wakeup sentinel id=${em2.id}`);

  return {
    adminToken,
    timeoutEmId: em1.id,
    wakeupEmId:  em2.id,
    since:       em1.version || 0,
  };
}

export default function(data) {
  const adminToken  = data.adminToken;
  const timeoutEmId = data.timeoutEmId;
  const wakeupEmId  = data.wakeupEmId;
  const since       = data.since;

  if (__VU === 1) {
    const startMs = Date.now();
    const pollRes = rawGet(`/api/emergencies/${timeoutEmId}/poll?since=${since}&timeout=3`, adminToken);
    const elapsed = Date.now() - startMs;
    check(pollRes, { 'emergency poll: timeout path returns 200': r => r.status === 200 });
    check({ elapsed }, { 'emergency poll: timeout path completes within 4000ms': o => o.elapsed < 4000 });
    if (pollRes.status === 200) {
      const body = JSON.parse(pollRes.body);
      check(body, { 'emergency poll: timeout response has id field': b => typeof b.id === 'string' });
    }

    const wakeupStart = Date.now();
    const longPollRes = rawGet(`/api/emergencies/${wakeupEmId}/poll?since=${since}&timeout=15`, adminToken);
    const wakeupElapsed = Date.now() - wakeupStart;
    check(longPollRes, { 'emergency poll: early wakeup path returns 200': r => r.status === 200 });
    check({ elapsed: wakeupElapsed }, { 'emergency poll: early wakeup resolves before 14000ms': o => o.elapsed < 14000 });
    if (longPollRes.status === 200) {
      const body = JSON.parse(longPollRes.body);
      check(body, { 'emergency poll: early wakeup response has id': b => typeof b.id === 'string' });
    }

    const invalid400 = rawGet('/api/emergencies/notanid/poll?since=0&timeout=3', adminToken);
    check(invalid400, { 'emergency poll: invalid GUID returns 400': r => r.status === 400 });

    const notFound404 = rawGet('/api/emergencies/00000000-0000-0000-0000-000000000001/poll?since=0&timeout=3', adminToken);
    check(notFound404, { 'emergency poll: nonexistent GUID returns 404': r => r.status === 404 });
  }

  if (__VU === 2) {
    sleep(1);
    rawPost(`/api/emergencies/${wakeupEmId}/assign`, adminToken, { departments: ['Police'] });
  }
}
