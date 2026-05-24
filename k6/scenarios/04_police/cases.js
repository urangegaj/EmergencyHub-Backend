import { check } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

function createAndAssign(adminToken, policeToken) {
  const emRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-police-cases-${Date.now()}`,
    address: '1 Police Cases Rd',
  });
  if (emRes.status !== 201 && emRes.status !== 200) {
    throw new Error(`police cases: create emergency failed: ${emRes.status}`);
  }
  const id = JSON.parse(emRes.body).id;

  const assignRes = post(`/api/emergencies/${id}/assign`, adminToken, { departments: ['Police'] });
  if (assignRes.status !== 200) throw new Error(`police cases: assign failed: ${assignRes.status}`);

  const ready = pollUntil(
    () => get(`/api/police/cases/${id}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  if (!ready) throw new Error(`police cases: timed out waiting for police case id=${id}`);
  console.log(`[police_cases] emergencyId=${id}`);
  return id;
}

export default function(data) {
  const adminToken   = data.admin.accessToken;
  const policeToken  = data.police.accessToken;
  const fireToken    = data.fire.accessToken;
  const medicalToken = data.medical.accessToken;

  const caseId = createAndAssign(adminToken, policeToken);

  // List checks
  const listRes = get('/api/police/cases', policeToken);
  check(listRes, { 'police cases: list returns 200 for police responder': r => r.status === 200 });
  if (listRes.status === 200) {
    check(JSON.parse(listRes.body), { 'police cases: list returns array': b => Array.isArray(b) });
  }

  const listOpenRes = get('/api/police/cases?status=OPEN', policeToken);
  check(listOpenRes, { 'police cases: list with status=OPEN returns 200': r => r.status === 200 });
  if (listOpenRes.status === 200) {
    const items = JSON.parse(listOpenRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'police cases: all items in OPEN filter have status OPEN': a => a.every(i => i.status === 'OPEN') });
    }
  }

  const listClosedRes = get('/api/police/cases?status=CLOSED', policeToken);
  check(listClosedRes, { 'police cases: list with status=CLOSED returns 200': r => r.status === 200 });
  if (listClosedRes.status === 200) {
    const items = JSON.parse(listClosedRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'police cases: all items in CLOSED filter have status CLOSED': a => a.every(i => i.status === 'CLOSED') });
    }
  }

  const listInvalidRes = get('/api/police/cases?status=INVALID', policeToken);
  check(listInvalidRes, {
    'police cases: invalid status filter returns 200 or 400': r => r.status === 200 || r.status === 400,
  });

  // Get single
  const getRes = get(`/api/police/cases/${caseId}`, policeToken);
  check(getRes, { 'police cases: GET by emergencyId returns 200': r => r.status === 200 });
  if (getRes.status === 200) {
    const body = JSON.parse(getRes.body);
    check(body, { 'police cases: GET single emergencyId matches': b => b.emergencyId === caseId || b.id === caseId });
  }

  const get404Res = get('/api/police/cases/00000000-0000-0000-0000-000000000001', policeToken);
  check(get404Res, { 'police cases: GET nonexistent GUID returns 404': r => r.status === 404 });

  // State machine happy path
  const progressRes = put(`/api/police/cases/${caseId}`, policeToken, { status: 'IN_PROGRESS' });
  check(progressRes, { 'police cases: transition OPEN → IN_PROGRESS returns 200': r => r.status === 200 });

  const closeRes = put(`/api/police/cases/${caseId}`, policeToken, { status: 'CLOSED' });
  check(closeRes, { 'police cases: transition IN_PROGRESS → CLOSED returns 200': r => r.status === 200 });

  // State machine invalid transitions — fresh case
  const caseId2 = createAndAssign(adminToken, policeToken);

  const skipInProgressRes = put(`/api/police/cases/${caseId2}`, policeToken, { status: 'CLOSED' });
  check(skipInProgressRes, { 'police cases: invalid transition OPEN → CLOSED returns 400': r => r.status === 400 });

  put(`/api/police/cases/${caseId2}`, policeToken, { status: 'IN_PROGRESS' });
  const backwardsRes = put(`/api/police/cases/${caseId2}`, policeToken, { status: 'OPEN' });
  check(backwardsRes, { 'police cases: invalid transition IN_PROGRESS → OPEN returns 400': r => r.status === 400 });

  put(`/api/police/cases/${caseId2}`, policeToken, { status: 'CLOSED' });
  const recloseRes = put(`/api/police/cases/${caseId2}`, policeToken, { status: 'CLOSED' });
  check(recloseRes, { 'police cases: invalid re-close CLOSED → CLOSED returns 400': r => r.status === 400 });

  // Unit assignment
  const unitsRes = get('/api/police/units', policeToken);
  if (unitsRes.status === 200) {
    const units = JSON.parse(unitsRes.body);
    if (Array.isArray(units) && units.length > 0) {
      const unitId = units[0].id;
      const caseId3 = createAndAssign(adminToken, policeToken);
      put(`/api/police/cases/${caseId3}`, policeToken, { status: 'IN_PROGRESS' });
      const unitAssignRes = put(`/api/police/cases/${caseId3}`, policeToken, { status: 'CLOSED', unitId });
      check(unitAssignRes, { 'police cases: close with unitId returns 200': r => r.status === 200 });
    } else {
      console.warn('[police_cases] no units available — skipping unit assignment test');
    }
  }

  // Invalid input
  const caseId4 = createAndAssign(adminToken, policeToken);
  const invalidStatusRes = put(`/api/police/cases/${caseId4}`, policeToken, { status: 'FLYING' });
  check(invalidStatusRes, { 'police cases: unknown status string returns 400': r => r.status === 400 });

  // Cross-department authorization
  const fireGetRes = get('/api/police/cases', fireToken);
  check(fireGetRes, { 'police cases: fire token GET returns 403': r => r.status === 403 });

  const medicalGetRes = get('/api/police/cases', medicalToken);
  check(medicalGetRes, { 'police cases: medical token GET returns 403': r => r.status === 403 });

  const firePutRes = put(`/api/police/cases/${caseId}`, fireToken, { status: 'IN_PROGRESS' });
  check(firePutRes, { 'police cases: fire token PUT returns 403': r => r.status === 403 });

  const medicalPutRes = put(`/api/police/cases/${caseId}`, medicalToken, { status: 'IN_PROGRESS' });
  check(medicalPutRes, { 'police cases: medical token PUT returns 403': r => r.status === 403 });
}
