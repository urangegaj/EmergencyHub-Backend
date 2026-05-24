import { check } from 'k6';
import { get, post } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

function createEmergency(token) {
  const res = post('/api/emergencies', token, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-assign-test-${Date.now()}`,
    address: '10 Assign Blvd',
  });
  if (res.status !== 201 && res.status !== 200) {
    throw new Error(`assign: failed to create emergency: ${res.status} ${res.body}`);
  }
  const id = JSON.parse(res.body).id;
  console.log(`[emergency_assign] created emergency id=${id}`);
  return id;
}

export default function(data) {
  const adminToken = data.admin.accessToken;
  const fireToken  = data.fire.accessToken;

  const em1 = createEmergency(adminToken);
  const em2 = createEmergency(adminToken);
  const em3 = createEmergency(adminToken);

  const assignFireRes = post(`/api/emergencies/${em1}/assign`, adminToken, { departments: ['Fire'] });
  check(assignFireRes, { 'emergency assign: single dept Fire returns 200': r => r.status === 200 });

  const fireReady = pollUntil(
    () => get(`/api/fire/cases/${em1}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: fireReady }, { 'emergency assign: fire case created via Kafka': o => o.ready === true });

  if (fireReady) {
    const caseRes = get(`/api/fire/cases/${em1}`, adminToken);
    if (caseRes.status === 200) {
      const caseBody = JSON.parse(caseRes.body);
      check(caseBody, { 'emergency assign: fire case status is OPEN': b => b.status === 'OPEN' });
    }
  }

  const assignMultiRes = post(`/api/emergencies/${em2}/assign`, adminToken, { departments: ['Police', 'Medical'] });
  check(assignMultiRes, { 'emergency assign: multi-dept Police+Medical returns 200': r => r.status === 200 });

  const policeReady = pollUntil(
    () => get(`/api/police/cases/${em2}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: policeReady }, { 'emergency assign: police case created via Kafka': o => o.ready === true });

  const medicalReady = pollUntil(
    () => get(`/api/medical/cases/${em2}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: medicalReady }, { 'emergency assign: medical case created via Kafka': o => o.ready === true });

  const idempotentRes = post(`/api/emergencies/${em1}/assign`, adminToken, { departments: ['Fire'] });
  check(idempotentRes, { 'emergency assign: duplicate assign same dept returns 409': r => r.status === 409 });

  const emptyDeptRes = post(`/api/emergencies/${em3}/assign`, adminToken, { departments: [] });
  check(emptyDeptRes, { 'emergency assign: empty departments array returns 400': r => r.status === 400 });

  const invalidDeptRes = post(`/api/emergencies/${em3}/assign`, adminToken, { departments: ['InvalidDept'] });
  check(invalidDeptRes, { 'emergency assign: invalid department name returns 400': r => r.status === 400 });

  const responderAssignRes = post(`/api/emergencies/${em3}/assign`, fireToken, { departments: ['Fire'] });
  check(responderAssignRes, { 'emergency assign: responder cannot assign returns 403': r => r.status === 403 });

  const nonExistentRes = post('/api/emergencies/00000000-0000-0000-0000-000000000001/assign', adminToken, { departments: ['Fire'] });
  check(nonExistentRes, { 'emergency assign: non-existent emergency returns 404': r => r.status === 404 });

  const invalidGuidRes = post('/api/emergencies/notanid/assign', adminToken, { departments: ['Fire'] });
  check(invalidGuidRes, { 'emergency assign: invalid GUID returns 400': r => r.status === 400 });
}
