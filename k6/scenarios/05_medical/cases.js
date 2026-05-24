import { check } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

function createAndAssign(adminToken) {
  const emRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-medical-cases-${Date.now()}`,
    address: '1 Medical Cases Rd',
  });
  if (emRes.status !== 201 && emRes.status !== 200) {
    throw new Error(`medical cases: create emergency failed: ${emRes.status}`);
  }
  const id = JSON.parse(emRes.body).id;

  const assignRes = post(`/api/emergencies/${id}/assign`, adminToken, { departments: ['Medical'] });
  if (assignRes.status !== 200) throw new Error(`medical cases: assign failed: ${assignRes.status}`);

  const ready = pollUntil(
    () => get(`/api/medical/cases/${id}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  if (!ready) throw new Error(`medical cases: timed out waiting for medical case id=${id}`);
  console.log(`[medical_cases] emergencyId=${id}`);
  return id;
}

export default function(data) {
  const adminToken   = data.admin.accessToken;
  const medicalToken = data.medical.accessToken;
  const fireToken    = data.fire.accessToken;
  const policeToken  = data.police.accessToken;

  const caseId = createAndAssign(adminToken);

  // List checks
  const listRes = get('/api/medical/cases', medicalToken);
  check(listRes, { 'medical cases: list returns 200 for medical responder': r => r.status === 200 });
  if (listRes.status === 200) {
    check(JSON.parse(listRes.body), { 'medical cases: list returns array': b => Array.isArray(b) });
  }

  const listOpenRes = get('/api/medical/cases?status=OPEN', medicalToken);
  check(listOpenRes, { 'medical cases: list with status=OPEN returns 200': r => r.status === 200 });
  if (listOpenRes.status === 200) {
    const items = JSON.parse(listOpenRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'medical cases: all items in OPEN filter have status OPEN': a => a.every(i => i.status === 'OPEN') });
    }
  }

  const listClosedRes = get('/api/medical/cases?status=CLOSED', medicalToken);
  check(listClosedRes, { 'medical cases: list with status=CLOSED returns 200': r => r.status === 200 });
  if (listClosedRes.status === 200) {
    const items = JSON.parse(listClosedRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'medical cases: all items in CLOSED filter have status CLOSED': a => a.every(i => i.status === 'CLOSED') });
    }
  }

  const listInvalidRes = get('/api/medical/cases?status=INVALID', medicalToken);
  check(listInvalidRes, {
    'medical cases: invalid status filter returns 200 or 400': r => r.status === 200 || r.status === 400,
  });

  // Get single
  const getRes = get(`/api/medical/cases/${caseId}`, medicalToken);
  check(getRes, { 'medical cases: GET by emergencyId returns 200': r => r.status === 200 });
  if (getRes.status === 200) {
    const body = JSON.parse(getRes.body);
    check(body, { 'medical cases: GET single emergencyId matches': b => b.emergencyId === caseId || b.id === caseId });
  }

  const get404Res = get('/api/medical/cases/00000000-0000-0000-0000-000000000001', medicalToken);
  check(get404Res, { 'medical cases: GET nonexistent GUID returns 404': r => r.status === 404 });

  // State machine happy path
  const progressRes = put(`/api/medical/cases/${caseId}`, medicalToken, { status: 'IN_PROGRESS' });
  check(progressRes, { 'medical cases: transition OPEN → IN_PROGRESS returns 200': r => r.status === 200 });

  const closeRes = put(`/api/medical/cases/${caseId}`, medicalToken, { status: 'CLOSED' });
  check(closeRes, { 'medical cases: transition IN_PROGRESS → CLOSED returns 200': r => r.status === 200 });

  // State machine invalid transitions — fresh case
  const caseId2 = createAndAssign(adminToken);

  const skipInProgressRes = put(`/api/medical/cases/${caseId2}`, medicalToken, { status: 'CLOSED' });
  check(skipInProgressRes, { 'medical cases: invalid transition OPEN → CLOSED returns 400': r => r.status === 400 });

  put(`/api/medical/cases/${caseId2}`, medicalToken, { status: 'IN_PROGRESS' });
  const backwardsRes = put(`/api/medical/cases/${caseId2}`, medicalToken, { status: 'OPEN' });
  check(backwardsRes, { 'medical cases: invalid transition IN_PROGRESS → OPEN returns 400': r => r.status === 400 });

  put(`/api/medical/cases/${caseId2}`, medicalToken, { status: 'CLOSED' });
  const recloseRes = put(`/api/medical/cases/${caseId2}`, medicalToken, { status: 'CLOSED' });
  check(recloseRes, { 'medical cases: invalid re-close CLOSED → CLOSED returns 400': r => r.status === 400 });

  // Unit assignment
  const unitsRes = get('/api/medical/units', medicalToken);
  if (unitsRes.status === 200) {
    const units = JSON.parse(unitsRes.body);
    if (Array.isArray(units) && units.length > 0) {
      const unitId = units[0].id;
      const caseId3 = createAndAssign(adminToken);
      put(`/api/medical/cases/${caseId3}`, medicalToken, { status: 'IN_PROGRESS' });
      const unitAssignRes = put(`/api/medical/cases/${caseId3}`, medicalToken, { status: 'CLOSED', unitId });
      check(unitAssignRes, { 'medical cases: close with unitId returns 200': r => r.status === 200 });
    } else {
      console.warn('[medical_cases] no units available — skipping unit assignment test');
    }
  }

  // Invalid input
  const caseId4 = createAndAssign(adminToken);
  const invalidStatusRes = put(`/api/medical/cases/${caseId4}`, medicalToken, { status: 'FLYING' });
  check(invalidStatusRes, { 'medical cases: unknown status string returns 400': r => r.status === 400 });

  // Cross-department authorization
  const fireGetRes = get('/api/medical/cases', fireToken);
  check(fireGetRes, { 'medical cases: fire token GET returns 403': r => r.status === 403 });

  const policeGetRes = get('/api/medical/cases', policeToken);
  check(policeGetRes, { 'medical cases: police token GET returns 403': r => r.status === 403 });

  const firePutRes = put(`/api/medical/cases/${caseId}`, fireToken, { status: 'IN_PROGRESS' });
  check(firePutRes, { 'medical cases: fire token PUT returns 403': r => r.status === 403 });

  const policePutRes = put(`/api/medical/cases/${caseId}`, policeToken, { status: 'IN_PROGRESS' });
  check(policePutRes, { 'medical cases: police token PUT returns 403': r => r.status === 403 });
}
