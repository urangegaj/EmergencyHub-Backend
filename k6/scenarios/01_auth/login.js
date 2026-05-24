import { check } from 'k6';
import { postNoAuth } from '../../lib/http.js';
import { CITY_ID } from '../../config.js';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default function(data) {
  const res1 = postNoAuth('/api/auth/login', { email: 'admin@test.com', password: 'TestPass123!' });
  check(res1, { 'login: valid credentials returns 200': r => r.status === 200 });
  if (res1.status === 200) {
    const body = JSON.parse(res1.body);
    check(body, {
      'login: response has non-empty accessToken':  b => typeof b.accessToken === 'string' && b.accessToken.length > 0,
      'login: response has non-empty refreshToken': b => typeof b.refreshToken === 'string' && b.refreshToken.length > 0,
      'login: userId is valid GUID format':         b => GUID_RE.test(b.userId),
      'login: cityId matches configured CITY_ID':   b => b.cityId === CITY_ID,
      'login: role is Admin for admin account':     b => b.role === 'Admin',
    });
  }

  const res2 = postNoAuth('/api/auth/login', { email: 'admin@test.com', password: 'WrongPassword!' });
  check(res2, { 'login: wrong password returns 401': r => r.status === 401 });

  const res3 = postNoAuth('/api/auth/login', { email: `noone_${Date.now()}@test.com`, password: 'TestPass123!' });
  check(res3, { 'login: unknown email returns 401': r => r.status === 401 });

  const res4 = postNoAuth('/api/auth/login', {});
  check(res4, { 'login: empty body returns 400': r => r.status === 400 });

  const res5 = postNoAuth('/api/auth/login', { email: 'x@x.com' });
  check(res5, { 'login: missing password field returns 400': r => r.status === 400 });

  const res6 = postNoAuth('/api/auth/login', { password: 'TestPass123!' });
  check(res6, { 'login: missing email field returns 400': r => r.status === 400 });
}
