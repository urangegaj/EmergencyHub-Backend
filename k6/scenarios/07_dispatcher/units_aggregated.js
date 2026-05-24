import { check } from 'k6';
import { get, getNoAuth } from '../../lib/http.js';

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;
  const fireToken       = data.fire.accessToken;

  // Admin → 200 with all 3 dept keys
  const adminRes = get('/api/dispatcher/units', adminToken);
  check(adminRes, { 'dispatcher units: admin GET returns 200': r => r.status === 200 });
  if (adminRes.status === 200) {
    const body = JSON.parse(adminRes.body);
    check(body, {
      'dispatcher units: response has police key':  b => Array.isArray(b.police),
      'dispatcher units: response has fire key':    b => Array.isArray(b.fire),
      'dispatcher units: response has medical key': b => Array.isArray(b.medical),
    });
  }

  // Dispatcher → 200
  const dispRes = get('/api/dispatcher/units', dispatcherToken);
  check(dispRes, { 'dispatcher units: dispatcher GET returns 200': r => r.status === 200 });

  // Fire responder → 403
  const fireRes = get('/api/dispatcher/units', fireToken);
  check(fireRes, { 'dispatcher units: fire responder GET returns 403': r => r.status === 403 });

  // No token → 401
  const noAuthRes = getNoAuth('/api/dispatcher/units');
  check(noAuthRes, { 'dispatcher units: no token GET returns 401': r => r.status === 401 });

  // Double call with dispatcher — both succeed (verifies fan-out to 3 gRPC services)
  const disp2a = get('/api/dispatcher/units', dispatcherToken);
  const disp2b = get('/api/dispatcher/units', dispatcherToken);
  check(disp2a, { 'dispatcher units: first double-call returns 200': r => r.status === 200 });
  check(disp2b, { 'dispatcher units: second double-call returns 200': r => r.status === 200 });

  // Observation: if any upstream service is down the whole call fails.
  // Assert at minimum no 500 (a 503 would be expected if a service is down).
  check(dispRes, { 'dispatcher units: response is not 500 (server error)': r => r.status !== 500 });
}
