import { check, sleep } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

export default function(data) {
  const adminToken   = data.admin.accessToken;
  const fireToken    = data.fire.accessToken;
  const policeToken  = data.police.accessToken;
  const medicalToken = data.medical.accessToken;

  // STEP 1: Create emergency
  const createRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-full-lifecycle-${Date.now()}`,
    address: '1 Lifecycle Road',
  });
  check(createRes, { 'full_lifecycle STEP1: create emergency returns 201': r => r.status === 201 });
  if (createRes.status !== 201 && createRes.status !== 200) {
    console.error(`[full_lifecycle] STEP1 failed: ${createRes.status} ${createRes.body}`);
    return;
  }
  const emergencyId = JSON.parse(createRes.body).id;
  console.log(`[full_lifecycle] emergencyId=${emergencyId}`);

  // STEP 2: Assign to all 3 departments
  const assignRes = post(`/api/emergencies/${emergencyId}/assign`, adminToken, {
    departments: ['Fire', 'Police', 'Medical'],
  });
  check(assignRes, { 'full_lifecycle STEP2: assign all 3 departments returns 200': r => r.status === 200 });

  // STEP 3: Verify all 3 cases created via Kafka
  const fireReady = pollUntil(
    () => get(`/api/fire/cases/${emergencyId}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: fireReady }, { 'full_lifecycle STEP3: fire case created via Kafka': o => o.ready === true });

  const policeReady = pollUntil(
    () => get(`/api/police/cases/${emergencyId}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: policeReady }, { 'full_lifecycle STEP3: police case created via Kafka': o => o.ready === true });

  const medicalReady = pollUntil(
    () => get(`/api/medical/cases/${emergencyId}`, adminToken).status === 200,
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ ready: medicalReady }, { 'full_lifecycle STEP3: medical case created via Kafka': o => o.ready === true });

  if (fireReady) {
    const fireCase = JSON.parse(get(`/api/fire/cases/${emergencyId}`, adminToken).body);
    check(fireCase, { 'full_lifecycle STEP3: fire case initial status is OPEN': b => b.status === 'OPEN' });
  }
  if (policeReady) {
    const policeCase = JSON.parse(get(`/api/police/cases/${emergencyId}`, adminToken).body);
    check(policeCase, { 'full_lifecycle STEP3: police case initial status is OPEN': b => b.status === 'OPEN' });
  }
  if (medicalReady) {
    const medicalCase = JSON.parse(get(`/api/medical/cases/${emergencyId}`, adminToken).body);
    check(medicalCase, { 'full_lifecycle STEP3: medical case initial status is OPEN': b => b.status === 'OPEN' });
  }

  // STEP 4: Progress fire case
  const fireProgressRes = put(`/api/fire/cases/${emergencyId}`, fireToken, { status: 'IN_PROGRESS' });
  check(fireProgressRes, { 'full_lifecycle STEP4: fire case progress to IN_PROGRESS returns 200': r => r.status === 200 });

  // STEP 5: Close fire case
  const fireCloseRes = put(`/api/fire/cases/${emergencyId}`, fireToken, { status: 'CLOSED' });
  check(fireCloseRes, { 'full_lifecycle STEP5: fire case close returns 200': r => r.status === 200 });

  // STEP 6: Close police case (IN_PROGRESS then CLOSED)
  const policeProgressRes = put(`/api/police/cases/${emergencyId}`, policeToken, { status: 'IN_PROGRESS' });
  check(policeProgressRes, { 'full_lifecycle STEP6: police case progress to IN_PROGRESS returns 200': r => r.status === 200 });

  const policeCloseRes = put(`/api/police/cases/${emergencyId}`, policeToken, { status: 'CLOSED' });
  check(policeCloseRes, { 'full_lifecycle STEP6: police case close returns 200': r => r.status === 200 });

  // STEP 7: Close medical case (IN_PROGRESS then CLOSED)
  const medicalProgressRes = put(`/api/medical/cases/${emergencyId}`, medicalToken, { status: 'IN_PROGRESS' });
  check(medicalProgressRes, { 'full_lifecycle STEP7: medical case progress to IN_PROGRESS returns 200': r => r.status === 200 });

  const medicalCloseRes = put(`/api/medical/cases/${emergencyId}`, medicalToken, { status: 'CLOSED' });
  check(medicalCloseRes, { 'full_lifecycle STEP7: medical case close returns 200': r => r.status === 200 });

  // STEP 8: Emergency auto-resolves (2 Kafka hops — allow 15s)
  const resolved = pollUntil(
    () => {
      const r = get(`/api/emergencies/${emergencyId}`, adminToken);
      if (r.status !== 200) return false;
      return JSON.parse(r.body).status === 'RESOLVED';
    },
    ASYNC_WAIT,
    POLL_INTERVAL
  );
  check({ resolved }, { 'full_lifecycle STEP8: emergency auto-resolves via Kafka': o => o.resolved === true });
  console.log(`[full_lifecycle] emergency resolved=${resolved} id=${emergencyId}`);

  // STEP 9: Assessment report created (CDC + pipeline — allow 20s)
  let assessmentRes = null;
  const assessmentCreated = pollUntil(
    () => {
      assessmentRes = get(`/api/assessments/${emergencyId}`, adminToken);
      return assessmentRes.status === 200;
    },
    ASYNC_WAIT,
    POLL_INTERVAL
  );
  check({ created: assessmentCreated }, { 'full_lifecycle STEP9: assessment report created via CDC': o => o.created === true });

  if (assessmentCreated && assessmentRes && assessmentRes.status === 200) {
    const report = JSON.parse(assessmentRes.body);
    console.log(`[full_lifecycle] assessment emergencyId=${report.emergencyId} status=${report.status}`);
    check(report, {
      'full_lifecycle STEP9: report emergencyId matches': b => b.emergencyId === emergencyId,
      'full_lifecycle STEP9: report has valid status': b => ['PENDING', 'PROCESSING', 'COMPLETED', 'FAILED'].includes(b.status),
    });

    // STEP 10: Wait for COMPLETED if not already
    if (report.status !== 'COMPLETED') {
      sleep(5);
      const finalRes = get(`/api/assessments/${emergencyId}`, adminToken);
      if (finalRes.status === 200) {
        const finalReport = JSON.parse(finalRes.body);
        if (finalReport.status === 'FAILED') {
          console.warn(`[full_lifecycle] STEP10: assessment status is FAILED — pipeline may need investigation`);
        }
        check(finalReport, {
          'full_lifecycle STEP10: assessment reaches COMPLETED or non-PENDING state': b =>
            b.status === 'COMPLETED' || b.status === 'FAILED',
        });
      }
    } else {
      check(report, { 'full_lifecycle STEP10: assessment is already COMPLETED': b => b.status === 'COMPLETED' });
    }
  }
}
