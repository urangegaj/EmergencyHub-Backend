import { check } from 'k6';
import http from 'k6/http';
import { get, put } from '../../lib/http.js';
import { BASE_URL } from '../../config.js';

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const medicalToken    = data.medical.accessToken;
  const fireToken       = data.fire.accessToken;
  const policeToken     = data.police.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;

  // List
  const listRes = get('/api/medical/units', medicalToken);
  check(listRes, { 'medical units: list returns 200 for medical responder': r => r.status === 200 });
  if (listRes.status === 200) {
    const units = JSON.parse(listRes.body);
    check(units, {
      'medical units: list returns array':                     u => Array.isArray(u),
      'medical units: each unit has id, name, status fields':  u => !Array.isArray(u) || u.every(x => x.id && x.name && x.status),
    });
  }

  // Cache timing: cold vs warm
  const cold = http.get(`${BASE_URL}/api/medical/units`, {
    headers: { 'Authorization': `Bearer ${medicalToken}` },
    tags: { type: 'functional', cache: 'cold' },
  });
  const warm = http.get(`${BASE_URL}/api/medical/units`, {
    headers: { 'Authorization': `Bearer ${medicalToken}` },
    tags: { type: 'functional', cache: 'warm' },
  });
  check(cold, { 'medical units: cold cache call returns 200': r => r.status === 200 });
  check(warm, { 'medical units: warm cache call returns 200': r => r.status === 200 });
  check({ warmTime: warm.timings.duration }, {
    'medical units: warm (Redis) cache response < 100ms': o => o.warmTime < 100,
  });

  // Get a unit ID
  const units = listRes.status === 200 ? JSON.parse(listRes.body) : [];
  if (!Array.isArray(units) || units.length === 0) {
    console.warn('[medical_units] no units returned — skipping update/status tests');
    return;
  }
  const unitId = units[0].id;
  console.log(`[medical_units] testing with unitId=${unitId}`);

  // Update all status values
  const onSceneRes = put(`/api/medical/units/${unitId}/status`, medicalToken, { status: 'ON_SCENE' });
  check(onSceneRes, { 'medical units: set status ON_SCENE returns 200': r => r.status === 200 });

  const availableRes = put(`/api/medical/units/${unitId}/status`, medicalToken, { status: 'AVAILABLE' });
  check(availableRes, { 'medical units: set status AVAILABLE returns 200': r => r.status === 200 });

  const offDutyRes = put(`/api/medical/units/${unitId}/status`, medicalToken, { status: 'OFF_DUTY' });
  check(offDutyRes, { 'medical units: set status OFF_DUTY returns 200': r => r.status === 200 });

  put(`/api/medical/units/${unitId}/status`, medicalToken, { status: 'AVAILABLE' });

  // Verify cache invalidated
  const afterUpdateRes = get('/api/medical/units', medicalToken);
  check(afterUpdateRes, { 'medical units: GET after update returns 200': r => r.status === 200 });
  if (afterUpdateRes.status === 200) {
    const updated = JSON.parse(afterUpdateRes.body);
    const found = Array.isArray(updated) && updated.find(u => u.id === unitId);
    if (found) {
      check(found, { 'medical units: status reflects last update (cache invalidated)': u => u.status === 'AVAILABLE' });
    }
  }

  // Invalid status
  const invalidRes = put(`/api/medical/units/${unitId}/status`, medicalToken, { status: 'BROKEN' });
  check(invalidRes, { 'medical units: unknown status returns 400': r => r.status === 400 });

  // Authorization
  const fireGetRes = get('/api/medical/units', fireToken);
  check(fireGetRes, { 'medical units: fire token GET returns 403': r => r.status === 403 });

  const policeGetRes = get('/api/medical/units', policeToken);
  check(policeGetRes, { 'medical units: police token GET returns 403': r => r.status === 403 });

  const dispatcherPutRes = put(`/api/medical/units/${unitId}/status`, dispatcherToken, { status: 'AVAILABLE' });
  check(dispatcherPutRes, { 'medical units: dispatcher PUT status returns 403': r => r.status === 403 });
}
