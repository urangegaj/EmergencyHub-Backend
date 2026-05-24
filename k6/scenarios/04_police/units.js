import { check } from 'k6';
import http from 'k6/http';
import { get, put } from '../../lib/http.js';
import { BASE_URL } from '../../config.js';

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const policeToken     = data.police.accessToken;
  const fireToken       = data.fire.accessToken;
  const medicalToken    = data.medical.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;

  // List
  const listRes = get('/api/police/units', policeToken);
  check(listRes, { 'police units: list returns 200 for police responder': r => r.status === 200 });
  if (listRes.status === 200) {
    const units = JSON.parse(listRes.body);
    check(units, {
      'police units: list returns array':                     u => Array.isArray(u),
      'police units: each unit has id, name, status fields':  u => !Array.isArray(u) || u.every(x => x.id && x.name && x.status),
    });
  }

  // Cache timing: cold vs warm
  const cold = http.get(`${BASE_URL}/api/police/units`, {
    headers: { 'Authorization': `Bearer ${policeToken}` },
    tags: { type: 'functional', cache: 'cold' },
  });
  const warm = http.get(`${BASE_URL}/api/police/units`, {
    headers: { 'Authorization': `Bearer ${policeToken}` },
    tags: { type: 'functional', cache: 'warm' },
  });
  check(cold, { 'police units: cold cache call returns 200': r => r.status === 200 });
  check(warm, { 'police units: warm cache call returns 200': r => r.status === 200 });
  check({ warmTime: warm.timings.duration }, {
    'police units: warm (Redis) cache response < 100ms': o => o.warmTime < 100,
  });

  // Get a unit ID
  const units = listRes.status === 200 ? JSON.parse(listRes.body) : [];
  if (!Array.isArray(units) || units.length === 0) {
    console.warn('[police_units] no units returned — skipping update/status tests');
    return;
  }
  const unitId = units[0].id;
  console.log(`[police_units] testing with unitId=${unitId}`);

  // Update all status values
  const onSceneRes = put(`/api/police/units/${unitId}/status`, policeToken, { status: 'ON_SCENE' });
  check(onSceneRes, { 'police units: set status ON_SCENE returns 200': r => r.status === 200 });

  const availableRes = put(`/api/police/units/${unitId}/status`, policeToken, { status: 'AVAILABLE' });
  check(availableRes, { 'police units: set status AVAILABLE returns 200': r => r.status === 200 });

  const offDutyRes = put(`/api/police/units/${unitId}/status`, policeToken, { status: 'OFF_DUTY' });
  check(offDutyRes, { 'police units: set status OFF_DUTY returns 200': r => r.status === 200 });

  put(`/api/police/units/${unitId}/status`, policeToken, { status: 'AVAILABLE' });

  // Verify cache invalidated
  const afterUpdateRes = get('/api/police/units', policeToken);
  check(afterUpdateRes, { 'police units: GET after update returns 200': r => r.status === 200 });
  if (afterUpdateRes.status === 200) {
    const updated = JSON.parse(afterUpdateRes.body);
    const found = Array.isArray(updated) && updated.find(u => u.id === unitId);
    if (found) {
      check(found, { 'police units: status reflects last update (cache invalidated)': u => u.status === 'AVAILABLE' });
    }
  }

  // Invalid status
  const invalidRes = put(`/api/police/units/${unitId}/status`, policeToken, { status: 'BROKEN' });
  check(invalidRes, { 'police units: unknown status returns 400': r => r.status === 400 });

  // Authorization
  const fireGetRes = get('/api/police/units', fireToken);
  check(fireGetRes, { 'police units: fire token GET returns 403': r => r.status === 403 });

  const medicalGetRes = get('/api/police/units', medicalToken);
  check(medicalGetRes, { 'police units: medical token GET returns 403': r => r.status === 403 });

  const dispatcherPutRes = put(`/api/police/units/${unitId}/status`, dispatcherToken, { status: 'AVAILABLE' });
  check(dispatcherPutRes, { 'police units: dispatcher PUT status returns 403': r => r.status === 403 });
}
