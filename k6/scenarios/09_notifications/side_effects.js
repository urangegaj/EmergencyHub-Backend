/**
 * Black-box notification verification.
 *
 * Direct email verification is out of k6 scope. To verify actual email delivery,
 * use one of:
 *   - MailHog/Mailtrap HTTP API (e.g., GET http://localhost:8025/api/v2/messages)
 *   - A future GET /api/admin/notifications endpoint
 *
 * This test verifies system health and absence of 500 errors through the
 * notification-triggering flows, confirming no notification side-effect crashes the system.
 */
import { check, sleep } from 'k6';
import { get, post, put } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { login } from '../../lib/auth.js';
import { EMERGENCY_TYPE_ID, ASYNC_WAIT } from '../../config.js';

export default function(data) {
  const adminToken   = data.admin.accessToken;
  const fireToken    = data.fire.accessToken;
  const policeToken  = data.police.accessToken;
  const medicalToken = data.medical.accessToken;

  // 1. Login all 5 users (ensures active sessions for notification targeting)
  const sessions = {
    admin:      login('admin@test.com',      'TestPass123!'),
    dispatcher: login('dispatcher@test.com', 'TestPass123!'),
    fire:       login('fire@test.com',       'TestPass123!'),
    police:     login('police@test.com',     'TestPass123!'),
    medical:    login('medical@test.com',    'TestPass123!'),
  };
  console.log('[notifications] all 5 users logged in');

  // 2. Create emergency
  const emRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `k6-notification-test-${Date.now()}`,
    address: '1 Notification Blvd',
  });
  check(emRes, { 'notifications: create emergency does not return 500': r => r.status !== 500 });
  if (emRes.status !== 201 && emRes.status !== 200) {
    console.error(`[notifications] create emergency failed: ${emRes.status}`);
    return;
  }
  const emergencyId = JSON.parse(emRes.body).id;
  console.log(`[notifications] emergencyId=${emergencyId}`);

  // 3. Assign to all 3 departments (triggers EmergencyAssigned → email to dept users)
  const assignRes = post(`/api/emergencies/${emergencyId}/assign`, adminToken, {
    departments: ['Fire', 'Police', 'Medical'],
  });
  check(assignRes, { 'notifications: assign does not return 500': r => r.status !== 500 });

  // 4. Give Kafka time to process EmergencyCreated + EmergencyAssigned notifications
  sleep(ASYNC_WAIT / 1000);

  // 5. System still responsive
  const healthCheck = get('/api/emergencies', adminToken);
  check(healthCheck, { 'notifications: system responsive after assign + notifications': r => r.status === 200 });

  // Wait for fire case
  pollUntil(() => get(`/api/fire/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, 500);

  // 6. Update fire case to IN_PROGRESS (triggers DepartmentCaseUpdated notification)
  const fireProgressRes = put(`/api/fire/cases/${emergencyId}`, sessions.fire.accessToken, { status: 'IN_PROGRESS' });
  check(fireProgressRes, { 'notifications: fire case IN_PROGRESS does not return 500': r => r.status !== 500 });

  // 7. Brief pause
  sleep(1);

  // 8. Close fire case
  const fireCloseRes = put(`/api/fire/cases/${emergencyId}`, sessions.fire.accessToken, { status: 'CLOSED' });
  check(fireCloseRes, { 'notifications: fire case CLOSED does not return 500': r => r.status !== 500 });

  // 9. Give Kafka time to process DepartmentCaseUpdated notifications
  sleep(ASYNC_WAIT / 1000);

  // 10. Fire cases endpoint still healthy
  const fireCasesRes = get('/api/fire/cases', sessions.fire.accessToken);
  check(fireCasesRes, { 'notifications: GET /api/fire/cases healthy after notifications': r => r.status === 200 });

  // 11. Close police and medical cases
  pollUntil(() => get(`/api/police/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, 500);
  pollUntil(() => get(`/api/medical/cases/${emergencyId}`, adminToken).status === 200, ASYNC_WAIT, 500);

  put(`/api/police/cases/${emergencyId}`, sessions.police.accessToken, { status: 'IN_PROGRESS' });
  const policeCloseRes = put(`/api/police/cases/${emergencyId}`, sessions.police.accessToken, { status: 'CLOSED' });
  check(policeCloseRes, { 'notifications: police case CLOSED does not return 500': r => r.status !== 500 });

  put(`/api/medical/cases/${emergencyId}`, sessions.medical.accessToken, { status: 'IN_PROGRESS' });
  const medicalCloseRes = put(`/api/medical/cases/${emergencyId}`, sessions.medical.accessToken, { status: 'CLOSED' });
  check(medicalCloseRes, { 'notifications: medical case CLOSED does not return 500': r => r.status !== 500 });

  // 12. Poll until emergency RESOLVED
  const resolved = pollUntil(
    () => {
      const r = get(`/api/emergencies/${emergencyId}`, adminToken);
      return r.status === 200 && JSON.parse(r.body).status === 'RESOLVED';
    },
    ASYNC_WAIT, POLL_INTERVAL
  );
  check({ resolved }, { 'notifications: emergency eventually resolves': o => o.resolved === true });

  // 13. Give Kafka time to process EmergencyStatusUpdated notification (reporter email)
  sleep(ASYNC_WAIT / 1000);

  // 14. Emergency endpoint still healthy
  const finalRes = get(`/api/emergencies/${emergencyId}`, adminToken);
  check(finalRes, { 'notifications: GET emergency healthy after all notifications': r => r.status === 200 });

  // 15. Final assertion: no 500 from any response
  const checks = [emRes, assignRes, healthCheck, fireProgressRes, fireCloseRes, fireCasesRes,
                  policeCloseRes, medicalCloseRes, finalRes];
  for (const r of checks) {
    check(r, { 'notifications: no response is 500': res => res.status !== 500 });
  }
}
