import { check, sleep } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

function runLifecycle(adminToken, fireToken, policeToken, medicalToken) {
  const emRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-assessment-lifecycle-${Date.now()}`,
    address: '1 Assessment Ave',
  });
  if (emRes.status !== 201 && emRes.status !== 200) {
    throw new Error(`assessment: create emergency failed: ${emRes.status}`);
  }
  const emergencyId = JSON.parse(emRes.body).id;
  console.log(`[assessment_reports] lifecycle emergencyId=${emergencyId}`);

  const assignRes = post(`/api/emergencies/${emergencyId}/assign`, adminToken, {
    departments: ['Fire', 'Police', 'Medical'],
  });
  if (assignRes.status !== 200) throw new Error(`assessment: assign failed: ${assignRes.status}`);

  pollUntil(() => get(`/api/fire/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);
  pollUntil(() => get(`/api/police/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);
  pollUntil(() => get(`/api/medical/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);

  put(`/api/fire/cases/${emergencyId}`, fireToken, { status: 'IN_PROGRESS' });
  put(`/api/fire/cases/${emergencyId}`, fireToken, { status: 'CLOSED' });
  put(`/api/police/cases/${emergencyId}`, policeToken, { status: 'IN_PROGRESS' });
  put(`/api/police/cases/${emergencyId}`, policeToken, { status: 'CLOSED' });
  put(`/api/medical/cases/${emergencyId}`, medicalToken, { status: 'IN_PROGRESS' });
  put(`/api/medical/cases/${emergencyId}`, medicalToken, { status: 'CLOSED' });

  const resolved = pollUntil(
    () => {
      const r = get(`/api/emergencies/${emergencyId}`, adminToken);
      return r.status === 200 && JSON.parse(r.body).status === 'RESOLVED';
    },
    ASYNC_WAIT, POLL_INTERVAL
  );
  if (!resolved) console.warn('[assessment_reports] timed out waiting for RESOLVED');

  const reportReady = pollUntil(
    () => get(`/api/assessments/${emergencyId}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  if (!reportReady) console.warn('[assessment_reports] timed out waiting for assessment');

  return emergencyId;
}

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const fireToken       = data.fire.accessToken;
  const policeToken     = data.police.accessToken;
  const medicalToken    = data.medical.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;

  // Nonexistent GUID → 404
  const notFoundRes = get('/api/assessments/00000000-0000-0000-0000-000000000001', adminToken);
  check(notFoundRes, { 'assessment: GET nonexistent GUID returns 404': r => r.status === 404 });

  // Invalid GUID → 400
  const invalidRes = get('/api/assessments/notanid', adminToken);
  check(invalidRes, { 'assessment: GET invalid GUID returns 400': r => r.status === 400 });

  // Run full lifecycle to get a resolved emergency
  const emergencyId = runLifecycle(adminToken, fireToken, policeToken, medicalToken);

  // GET assessment → 200
  const reportRes = get(`/api/assessments/${emergencyId}`, adminToken);
  check(reportRes, { 'assessment: GET existing report returns 200': r => r.status === 200 });
  let reportStatus = null;
  if (reportRes.status === 200) {
    const report = JSON.parse(reportRes.body);
    reportStatus = report.status;
    console.log(`[assessment_reports] report status=${report.status} retryCount=${report.retryCount}`);
    check(report, {
      'assessment: report has emergencyId':  b => typeof b.emergencyId === 'string',
      'assessment: report has status':       b => typeof b.status === 'string',
      'assessment: report has retryCount':   b => typeof b.retryCount === 'number',
    });
  }

  // Retry on COMPLETED → 400 or 409
  if (reportStatus === 'COMPLETED') {
    const retryCompletedRes = post(`/api/assessments/${emergencyId}/retry`, adminToken, {});
    check(retryCompletedRes, {
      'assessment: retry on COMPLETED returns 400 or 409': r => r.status === 400 || r.status === 409,
    });
  }

  // Retry on non-COMPLETED → 200 (transitions back to PENDING)
  if (reportStatus !== null && reportStatus !== 'COMPLETED') {
    const retryRes = post(`/api/assessments/${emergencyId}/retry`, adminToken, {});
    check(retryRes, { 'assessment: retry on non-COMPLETED report returns 200': r => r.status === 200 });
    if (retryRes.status === 200) {
      const retried = JSON.parse(retryRes.body);
      check(retried, {
        'assessment: retry response status is PENDING or PROCESSING': b =>
          b.status === 'PENDING' || b.status === 'PROCESSING',
      });
    }
  } else if (reportStatus === 'COMPLETED') {
    console.log('[assessment_reports] report already COMPLETED — skipping retry-on-non-completed test');
  }

  // Authorization: fire token → 403 on GET
  const fireGetRes = get(`/api/assessments/${emergencyId}`, fireToken);
  check(fireGetRes, { 'assessment: fire token GET returns 403': r => r.status === 403 });

  // Authorization: dispatcher token → 403 on retry
  const dispatcherRetryRes = post(`/api/assessments/${emergencyId}/retry`, dispatcherToken, {});
  check(dispatcherRetryRes, { 'assessment: dispatcher POST retry returns 403': r => r.status === 403 });
}
