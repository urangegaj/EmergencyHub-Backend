import { check } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

function createAndAssign(adminToken) {
  const emRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-fire-cases-${Date.now()}`,
    address: '1 Fire Cases Rd',
  });
  if (emRes.status !== 201 && emRes.status !== 200) {
    throw new Error(`fire cases: create emergency failed: ${emRes.status}`);
  }
  const id = JSON.parse(emRes.body).id;

  const assignRes = post(`/api/emergencies/${id}/assign`, adminToken, { departments: ['Fire'] });
  if (assignRes.status !== 200) throw new Error(`fire cases: assign failed: ${assignRes.status}`);

  const ready = pollUntil(
    () => get(`/api/fire/cases/${id}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  if (!ready) throw new Error(`fire cases: timed out waiting for fire case id=${id}`);
  console.log(`[fire_cases] emergencyId=${id}`);
  return id;
}

export default function(data) {
  const adminToken = data.admin.accessToken;
  const fireToken  = data.fire.accessToken;

  const caseId = createAndAssign(adminToken);

  // List checks
  const listRes = get('/api/fire/cases', fireToken);
  check(listRes, { 'fire cases: list returns 200 for fire responder': r => r.status === 200 });
  if (listRes.status === 200) {
    check(JSON.parse(listRes.body), { 'fire cases: list returns array': b => Array.isArray(b) });
  }

  const listOpenRes = get('/api/fire/cases?status=OPEN', fireToken);
  check(listOpenRes, { 'fire cases: list with status=OPEN returns 200': r => r.status === 200 });
  if (listOpenRes.status === 200) {
    const items = JSON.parse(listOpenRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'fire cases: all items in OPEN filter have status OPEN': a => a.every(i => i.status === 'OPEN') });
    }
  }

  const listClosedRes = get('/api/fire/cases?status=CLOSED', fireToken);
  check(listClosedRes, { 'fire cases: list with status=CLOSED returns 200': r => r.status === 200 });
  if (listClosedRes.status === 200) {
    const items = JSON.parse(listClosedRes.body);
    if (Array.isArray(items) && items.length > 0) {
      check(items, { 'fire cases: all items in CLOSED filter have status CLOSED': a => a.every(i => i.status === 'CLOSED') });
    }
  }

  const listInvalidRes = get('/api/fire/cases?status=INVALID', fireToken);
  check(listInvalidRes, {
    'fire cases: invalid status filter returns 200 or 400': r => r.status === 200 || r.status === 400,
  });

  // Get single
  const getRes = get(`/api/fire/cases/${caseId}`, fireToken);
  check(getRes, { 'fire cases: GET by emergencyId returns 200': r => r.status === 200 });
  if (getRes.status === 200) {
    const body = JSON.parse(getRes.body);
    check(body, { 'fire cases: GET single emergencyId matches': b => b.emergencyId === caseId || b.id === caseId });
  }

  const get404Res = get('/api/fire/cases/00000000-0000-0000-0000-000000000001', fireToken);
  check(get404Res, { 'fire cases: GET nonexistent GUID returns 404': r => r.status === 404 });

  // State machine happy path
  const progressRes = put(`/api/fire/cases/${caseId}`, fireToken, { status: 'IN_PROGRESS' });
  check(progressRes, { 'fire cases: transition OPEN → IN_PROGRESS returns 200': r => r.status === 200 });

  const closeRes = put(`/api/fire/cases/${caseId}`, fireToken, { status: 'CLOSED' });
  check(closeRes, { 'fire cases: transition IN_PROGRESS → CLOSED returns 200': r => r.status === 200 });

  // State machine invalid transitions — need a fresh OPEN case
  const caseId2 = createAndAssign(adminToken);

  const skipInProgressRes = put(`/api/fire/cases/${caseId2}`, fireToken, { status: 'CLOSED' });
  check(skipInProgressRes, { 'fire cases: invalid transition OPEN → CLOSED returns 400': r => r.status === 400 });

  put(`/api/fire/cases/${caseId2}`, fireToken, { status: 'IN_PROGRESS' });
  const backwardsRes = put(`/api/fire/cases/${caseId2}`, fireToken, { status: 'OPEN' });
  check(backwardsRes, { 'fire cases: invalid transition IN_PROGRESS → OPEN returns 400': r => r.status === 400 });

  put(`/api/fire/cases/${caseId2}`, fireToken, { status: 'CLOSED' });
  const recloseRes = put(`/api/fire/cases/${caseId2}`, fireToken, { status: 'CLOSED' });
  check(recloseRes, { 'fire cases: invalid re-close CLOSED → CLOSED returns 400': r => r.status === 400 });

  // Unit assignment
  const unitsRes = get('/api/fire/units', fireToken);
  if (unitsRes.status === 200) {
    const units = JSON.parse(unitsRes.body);
    if (Array.isArray(units) && units.length > 0) {
      const unitId = units[0].id;
      const caseId3 = createAndAssign(adminToken);
      put(`/api/fire/cases/${caseId3}`, fireToken, { status: 'IN_PROGRESS' });
      const unitAssignRes = put(`/api/fire/cases/${caseId3}`, fireToken, { status: 'CLOSED', unitId });
      check(unitAssignRes, { 'fire cases: close with unitId returns 200': r => r.status === 200 });
    } else {
      console.warn('[fire_cases] no units available — skipping unit assignment test');
    }
  }

  // Invalid input
  const caseId4 = createAndAssign(adminToken);
  const invalidStatusRes = put(`/api/fire/cases/${caseId4}`, fireToken, { status: 'FLYING' });
  check(invalidStatusRes, { 'fire cases: unknown status string returns 400': r => r.status === 400 });
}
