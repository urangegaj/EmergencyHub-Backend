import { check } from 'k6';
import http from 'k6/http';
import { register, login, refresh, logout } from '../../lib/auth.js';
import { get, getNoAuth } from '../../lib/http.js';
import { CITY_ID } from '../../config.js';

export default function(data) {
  const ts  = Date.now();
  const vu  = __VU;
  const email = `k6_lifecycle_${ts}_${vu}@test.com`;
  const password = 'TestPass123!';

  const reg = register({
    email,
    password,
    role: 'Dispatcher',
    firstName: 'Lifecycle',
    lastName: 'User',
    cityId: CITY_ID,
  });
  check(reg, { 'token_lifecycle: register fresh Dispatcher returns 200': r => r.status === 200 || r.status === 201 });

  const session1 = login(email, password);
  const T1 = session1.accessToken;
  const R1 = session1.refreshToken;
  console.log(`[token_lifecycle] T1=${T1.slice(0, 20)}... R1=${R1}`);

  const meRes1 = get('/api/me', T1);
  check(meRes1, { 'token_lifecycle: GET /api/me with T1 returns 200': r => r.status === 200 });
  if (meRes1.status === 200) {
    const me1 = JSON.parse(meRes1.body);
    check(me1, { 'token_lifecycle: /api/me role is Dispatcher': b => b.role === 'Dispatcher' });
  }

  const refRes1 = refresh(R1);
  check(refRes1, { 'token_lifecycle: refresh with R1 returns 200': r => r.status === 200 });
  const session2 = JSON.parse(refRes1.body);
  const T2 = session2.accessToken;
  const R2 = session2.refreshToken;
  console.log(`[token_lifecycle] T2=${T2.slice(0, 20)}... R2=${R2}`);

  const refRes2 = refresh(R1);
  check(refRes2, { 'token_lifecycle: reusing consumed R1 returns 401': r => r.status === 401 });

  const meRes2 = get('/api/me', T2);
  check(meRes2, { 'token_lifecycle: GET /api/me with T2 (after refresh) returns 200': r => r.status === 200 });

  const logoutRes = logout(R2, T2);
  check(logoutRes, { 'token_lifecycle: logout returns 200': r => r.status === 200 });

  const refRes3 = refresh(R2);
  check(refRes3, { 'token_lifecycle: refresh with R2 after logout returns 401': r => r.status === 401 });

  const meRes3 = get('/api/me', T2);
  check(meRes3, { 'token_lifecycle: GET /api/me with T2 after logout returns 401 (blacklisted)': r => r.status === 401 });

  const logoutRes2 = logout(R2, T2);
  check(logoutRes2, { 'token_lifecycle: idempotent second logout returns 200': r => r.status === 200 });

  const refResGarbage = refresh('00000000-0000-0000-0000-000000000000');
  check(refResGarbage, { 'token_lifecycle: garbage refresh token returns 401': r => r.status === 401 });

  const meNoAuth = getNoAuth('/api/me');
  check(meNoAuth, { 'token_lifecycle: GET /api/me without Authorization header returns 401': r => r.status === 401 });

  const meBadToken = http.get(
    `${__ENV.BASE_URL || 'http://localhost:5158'}/api/me`,
    { headers: { 'Authorization': 'Bearer notavalidtoken' }, tags: { type: 'functional' } }
  );
  check(meBadToken, { 'token_lifecycle: GET /api/me with invalid bearer token returns 401': r => r.status === 401 });
}
