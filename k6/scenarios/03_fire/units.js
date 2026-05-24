import { check } from 'k6';
import http from 'k6/http';
import { get, put } from '../../lib/http.js';
import { BASE_URL } from '../../config.js';

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const fireToken       = data.fire.accessToken;
  const policeToken     = data.police.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;

  // List
  const listRes = get('/api/fire/units', fireToken);
  check(listRes, { 'fire units: list returns 200 for fire responder': r => r.status === 200 });
  if (listRes.status === 200) {
    const units = JSON.parse(listRes.body);
    check(units, {
      'fire units: list returns array':                     u => Array.isArray(u),
      'fire units: each unit has id, name, status fields':  u => !Array.isArray(u) || u.every(x => x.id && x.name && x.status),
    });
  }

  // Cache timing: cold vs warm
  const cold = http.get(`${BASE_URL}/api/fire/units`, {
    headers: { 'Authorization': `Bearer ${fireToken}` },
    tags: { type: 'functional', cache: 'cold' },
  });
  const warm = http.get(`${BASE_URL}/api/fire/units`, {
    headers: { 'Authorization': `Bearer ${fireToken}` },
    tags: { type: 'functional', cache: 'warm' },
  });
  check(cold, { 'fire units: cold cache call returns 200': r => r.status === 200 });
  check(warm, { 'fire units: warm cache call returns 200': r => r.status === 200 });
  check({ warmTime: warm.timings.duration }, {
    'fire units: warm (Redis) cache response < 100ms': o => o.warmTime < 100,
  });

  // Get a unit ID
  const units = listRes.status === 200 ? JSON.parse(listRes.body) : [];
  if (!Array.isArray(units) || units.length === 0) {
    console.warn('[fire_units] no units returned — skipping update/status tests');
    return;
  }
  const unitId = units[0].id;
  console.log(`[fire_units] testing with unitId=${unitId}`);

  // Update all status values
  const onSceneRes = put(`/api/fire/units/${unitId}/status`, fireToken, { status: 'ON_SCENE' });
  check(onSceneRes, { 'fire units: set status ON_SCENE returns 200': r => r.status === 200 });

  const availableRes = put(`/api/fire/units/${unitId}/status`, fireToken, { status: 'AVAILABLE' });
  check(availableRes, { 'fire units: set status AVAILABLE returns 200': r => r.status === 200 });

  const offDutyRes = put(`/api/fire/units/${unitId}/status`, fireToken, { status: 'OFF_DUTY' });
  check(offDutyRes, { 'fire units: set status OFF_DUTY returns 200': r => r.status === 200 });

  put(`/api/fire/units/${unitId}/status`, fireToken, { status: 'AVAILABLE' });

  // Verify cache invalidated
  const afterUpdateRes = get('/api/fire/units', fireToken);
  check(afterUpdateRes, { 'fire units: GET after update returns 200': r => r.status === 200 });
  if (afterUpdateRes.status === 200) {
    const updated = JSON.parse(afterUpdateRes.body);
    const found = Array.isArray(updated) && updated.find(u => u.id === unitId);
    if (found) {
      check(found, { 'fire units: status reflects last update (cache invalidated)': u => u.status === 'AVAILABLE' });
    }
  }

  // Invalid status
  const invalidRes = put(`/api/fire/units/${unitId}/status`, fireToken, { status: 'BROKEN' });
  check(invalidRes, { 'fire units: unknown status returns 400': r => r.status === 400 });

  // Authorization
  const policeGetRes = get('/api/fire/units', policeToken);
  check(policeGetRes, { 'fire units: police token GET returns 403': r => r.status === 403 });

  const dispatcherPutRes = put(`/api/fire/units/${unitId}/status`, dispatcherToken, { status: 'AVAILABLE' });
  check(dispatcherPutRes, { 'fire units: dispatcher PUT status returns 403': r => r.status === 403 });
}
