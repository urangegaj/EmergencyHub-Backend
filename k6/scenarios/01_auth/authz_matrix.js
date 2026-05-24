import { check } from 'k6';
import http from 'k6/http';
import { get, post, put, getNoAuth, postNoAuth } from '../../lib/http.js';
import { pollUntil } from '../../lib/poll.js';
import { BASE_URL, EMERGENCY_TYPE_ID, ASYNC_WAIT, POLL_INTERVAL } from '../../config.js';

const FIRE_UNIT_ID = 'f0000001-0000-4000-8000-000000000001';

function makeEmergency(token) {
  const res = post('/api/emergencies', token, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: `authz-matrix sentinel ${Date.now()}`,
    address: '1 Matrix St',
  });
  if (res.status !== 201 && res.status !== 200) {
    throw new Error(`authz_matrix: failed to create sentinel emergency: ${res.status} ${res.body}`);
  }
  const id = JSON.parse(res.body).id;
  console.log(`[authz_matrix] sentinel emergency id=${id}`);
  return id;
}

function callNoToken(method, path, body) {
  if (method === 'GET') return getNoAuth(path);
  return http.post(
    `${BASE_URL}${path}`,
    body ? JSON.stringify(body) : null,
    { headers: { 'Content-Type': 'application/json' }, tags: { type: 'functional' } }
  );
}

function callWithToken(method, path, token, body) {
  switch (method) {
    case 'GET':  return get(path, token);
    case 'POST': return post(path, token, body || {});
    case 'PUT':  return put(path, token, body || {});
    default:     return get(path, token);
  }
}

export default function(data) {
  const adminToken      = data.admin.accessToken;
  const dispatcherToken = data.dispatcher.accessToken;
  const fireToken       = data.fire.accessToken;
  const policeToken     = data.police.accessToken;
  const medicalToken    = data.medical.accessToken;

  const sentinelEmergencyId = makeEmergency(adminToken);
  const sentinelId          = makeEmergency(adminToken);
  const sentinelId2         = makeEmergency(adminToken);
  const sentinelId3         = makeEmergency(adminToken);

  post(`/api/emergencies/${sentinelEmergencyId}/assign`, adminToken, { departments: ['Fire', 'Police', 'Medical'] });

  pollUntil(() => get(`/api/fire/cases/${sentinelEmergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);
  pollUntil(() => get(`/api/police/cases/${sentinelEmergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);
  pollUntil(() => get(`/api/medical/cases/${sentinelEmergencyId}`, adminToken).status === 200, ASYNC_WAIT, POLL_INTERVAL);

  const sentinelUnitId = FIRE_UNIT_ID;

  const testCases = [
    { label: 'no-token: GET /api/me → 401',             method: 'GET',  path: '/api/me',             body: null,                                                                      token: null, expectedStatus: 401 },
    { label: 'no-token: GET /api/emergencies → 401',    method: 'GET',  path: '/api/emergencies',    body: null,                                                                      token: null, expectedStatus: 401 },
    { label: 'no-token: POST /api/emergencies → 401',   method: 'POST', path: '/api/emergencies',    body: { emergencyTypeId: EMERGENCY_TYPE_ID, description: 'test', address: 'test' }, token: null, expectedStatus: 401 },
    { label: 'no-token: GET /api/fire/cases → 401',     method: 'GET',  path: '/api/fire/cases',     body: null,                                                                      token: null, expectedStatus: 401 },
    { label: 'no-token: GET /api/admin/users → 401',    method: 'GET',  path: '/api/admin/users',    body: null,                                                                      token: null, expectedStatus: 401 },

    { label: 'admin: POST /api/emergencies/{id}/assign → 200',         method: 'POST', path: `/api/emergencies/${sentinelId}/assign`,        body: { departments: ['Fire'] },                                                                               token: adminToken,      expectedStatus: 200 },
    { label: 'admin: GET /api/fire/cases → 200',                       method: 'GET',  path: '/api/fire/cases',                              body: null,                                                                                                    token: adminToken,      expectedStatus: 200 },
    { label: 'admin: PUT /api/fire/cases/{id} → 200',                  method: 'PUT',  path: `/api/fire/cases/${sentinelEmergencyId}`,       body: { status: 'IN_PROGRESS' },                                                                               token: adminToken,      expectedStatus: 200 },
    { label: 'admin: GET /api/fire/units → 200',                       method: 'GET',  path: '/api/fire/units',                              body: null,                                                                                                    token: adminToken,      expectedStatus: 200 },
    { label: 'admin: PUT /api/fire/units/{id}/status → 200',           method: 'PUT',  path: `/api/fire/units/${sentinelUnitId}/status`,     body: { status: 'AVAILABLE' },                                                                                 token: adminToken,      expectedStatus: 200 },
    { label: 'admin: GET /api/dispatcher/units → 200',                 method: 'GET',  path: '/api/dispatcher/units',                        body: null,                                                                                                    token: adminToken,      expectedStatus: 200 },
    { label: 'admin: GET /api/assessments/{id} → 200 or 404',          method: 'GET',  path: `/api/assessments/${sentinelEmergencyId}`,      body: null,                                                                                                    token: adminToken,      expectedStatus: [200, 404] },
    { label: 'admin: GET /api/admin/users → 200',                      method: 'GET',  path: '/api/admin/users',                             body: null,                                                                                                    token: adminToken,      expectedStatus: 200 },
    { label: 'admin: POST /api/admin/users → 201',                     method: 'POST', path: '/api/admin/users',                             body: { email: `authz_admin_${Date.now()}@test.com`, password: 'Test123!', role: 'Dispatcher', firstName: 'A', lastName: 'B' }, token: adminToken, expectedStatus: 201 },

    { label: 'dispatcher: POST /api/emergencies/{id}/assign → 200',    method: 'POST', path: `/api/emergencies/${sentinelId2}/assign`,       body: { departments: ['Fire'] },                                                                               token: dispatcherToken, expectedStatus: 200 },
    { label: 'dispatcher: GET /api/fire/cases → 200',                  method: 'GET',  path: '/api/fire/cases',                              body: null,                                                                                                    token: dispatcherToken, expectedStatus: 200 },
    { label: 'dispatcher: PUT /api/fire/cases/{id} → 403',             method: 'PUT',  path: `/api/fire/cases/${sentinelEmergencyId}`,       body: { status: 'CLOSED' },                                                                                    token: dispatcherToken, expectedStatus: 403 },
    { label: 'dispatcher: GET /api/fire/units → 200',                  method: 'GET',  path: '/api/fire/units',                              body: null,                                                                                                    token: dispatcherToken, expectedStatus: 200 },
    { label: 'dispatcher: PUT /api/fire/units/{id}/status → 403',      method: 'PUT',  path: `/api/fire/units/${sentinelUnitId}/status`,     body: { status: 'AVAILABLE' },                                                                                 token: dispatcherToken, expectedStatus: 403 },
    { label: 'dispatcher: GET /api/police/cases → 200',                method: 'GET',  path: '/api/police/cases',                            body: null,                                                                                                    token: dispatcherToken, expectedStatus: 200 },
    { label: 'dispatcher: PUT /api/police/cases/{id} → 403',           method: 'PUT',  path: `/api/police/cases/${sentinelEmergencyId}`,     body: { status: 'IN_PROGRESS' },                                                                               token: dispatcherToken, expectedStatus: 403 },
    { label: 'dispatcher: GET /api/dispatcher/units → 200',            method: 'GET',  path: '/api/dispatcher/units',                        body: null,                                                                                                    token: dispatcherToken, expectedStatus: 200 },
    { label: 'dispatcher: GET /api/assessments/{id} → 200 or 404',     method: 'GET',  path: `/api/assessments/${sentinelEmergencyId}`,      body: null,                                                                                                    token: dispatcherToken, expectedStatus: [200, 404] },
    { label: 'dispatcher: POST /api/assessments/{id}/retry → 403',     method: 'POST', path: `/api/assessments/${sentinelEmergencyId}/retry`, body: null,                                                                                                   token: dispatcherToken, expectedStatus: 403 },
    { label: 'dispatcher: GET /api/admin/users → 403',                 method: 'GET',  path: '/api/admin/users',                             body: null,                                                                                                    token: dispatcherToken, expectedStatus: 403 },

    { label: 'fire: GET /api/fire/cases → 200',                        method: 'GET',  path: '/api/fire/cases',                              body: null,                                                                                                    token: fireToken,       expectedStatus: 200 },
    { label: 'fire: PUT /api/fire/cases/{id} → 200',                   method: 'PUT',  path: `/api/fire/cases/${sentinelEmergencyId}`,       body: { status: 'CLOSED' },                                                                                    token: fireToken,       expectedStatus: 200 },
    { label: 'fire: GET /api/fire/units → 200',                        method: 'GET',  path: '/api/fire/units',                              body: null,                                                                                                    token: fireToken,       expectedStatus: 200 },
    { label: 'fire: PUT /api/fire/units/{id}/status → 200',            method: 'PUT',  path: `/api/fire/units/${sentinelUnitId}/status`,     body: { status: 'AVAILABLE' },                                                                                 token: fireToken,       expectedStatus: 200 },
    { label: 'fire: GET /api/police/cases → 403',                      method: 'GET',  path: '/api/police/cases',                            body: null,                                                                                                    token: fireToken,       expectedStatus: 403 },
    { label: 'fire: PUT /api/police/cases/{id} → 403',                 method: 'PUT',  path: `/api/police/cases/${sentinelEmergencyId}`,     body: { status: 'IN_PROGRESS' },                                                                               token: fireToken,       expectedStatus: 403 },
    { label: 'fire: GET /api/medical/cases → 403',                     method: 'GET',  path: '/api/medical/cases',                           body: null,                                                                                                    token: fireToken,       expectedStatus: 403 },
    { label: 'fire: GET /api/dispatcher/units → 403',                  method: 'GET',  path: '/api/dispatcher/units',                        body: null,                                                                                                    token: fireToken,       expectedStatus: 403 },
    { label: 'fire: POST /api/emergencies/{id}/assign → 403',          method: 'POST', path: `/api/emergencies/${sentinelId3}/assign`,       body: { departments: ['Fire'] },                                                                               token: fireToken,       expectedStatus: 403 },
    { label: 'fire: GET /api/admin/users → 403',                       method: 'GET',  path: '/api/admin/users',                             body: null,                                                                                                    token: fireToken,       expectedStatus: 403 },
    { label: 'fire: GET /api/assessments/{id} → 403',                  method: 'GET',  path: `/api/assessments/${sentinelEmergencyId}`,      body: null,                                                                                                    token: fireToken,       expectedStatus: 403 },

    { label: 'police: GET /api/police/cases → 200',                    method: 'GET',  path: '/api/police/cases',                            body: null,                                                                                                    token: policeToken,     expectedStatus: 200 },
    { label: 'police: PUT /api/police/cases/{id} → 200',               method: 'PUT',  path: `/api/police/cases/${sentinelEmergencyId}`,     body: { status: 'IN_PROGRESS' },                                                                               token: policeToken,     expectedStatus: 200 },
    { label: 'police: GET /api/police/units → 200',                    method: 'GET',  path: '/api/police/units',                            body: null,                                                                                                    token: policeToken,     expectedStatus: 200 },
    { label: 'police: GET /api/fire/cases → 403',                      method: 'GET',  path: '/api/fire/cases',                              body: null,                                                                                                    token: policeToken,     expectedStatus: 403 },
    { label: 'police: PUT /api/fire/cases/{id} → 403',                 method: 'PUT',  path: `/api/fire/cases/${sentinelEmergencyId}`,       body: { status: 'IN_PROGRESS' },                                                                               token: policeToken,     expectedStatus: 403 },
    { label: 'police: GET /api/medical/cases → 403',                   method: 'GET',  path: '/api/medical/cases',                           body: null,                                                                                                    token: policeToken,     expectedStatus: 403 },
    { label: 'police: GET /api/dispatcher/units → 403',                method: 'GET',  path: '/api/dispatcher/units',                        body: null,                                                                                                    token: policeToken,     expectedStatus: 403 },
    { label: 'police: GET /api/admin/users → 403',                     method: 'GET',  path: '/api/admin/users',                             body: null,                                                                                                    token: policeToken,     expectedStatus: 403 },
    { label: 'police: GET /api/assessments/{id} → 403',                method: 'GET',  path: `/api/assessments/${sentinelEmergencyId}`,      body: null,                                                                                                    token: policeToken,     expectedStatus: 403 },

    { label: 'medical: GET /api/medical/cases → 200',                  method: 'GET',  path: '/api/medical/cases',                           body: null,                                                                                                    token: medicalToken,    expectedStatus: 200 },
    { label: 'medical: PUT /api/medical/cases/{id} → 200',             method: 'PUT',  path: `/api/medical/cases/${sentinelEmergencyId}`,    body: { status: 'IN_PROGRESS' },                                                                               token: medicalToken,    expectedStatus: 200 },
    { label: 'medical: GET /api/medical/units → 200',                  method: 'GET',  path: '/api/medical/units',                           body: null,                                                                                                    token: medicalToken,    expectedStatus: 200 },
    { label: 'medical: GET /api/fire/cases → 403',                     method: 'GET',  path: '/api/fire/cases',                              body: null,                                                                                                    token: medicalToken,    expectedStatus: 403 },
    { label: 'medical: PUT /api/fire/cases/{id} → 403',                method: 'PUT',  path: `/api/fire/cases/${sentinelEmergencyId}`,       body: { status: 'IN_PROGRESS' },                                                                               token: medicalToken,    expectedStatus: 403 },
    { label: 'medical: GET /api/police/cases → 403',                   method: 'GET',  path: '/api/police/cases',                            body: null,                                                                                                    token: medicalToken,    expectedStatus: 403 },
    { label: 'medical: GET /api/dispatcher/units → 403',               method: 'GET',  path: '/api/dispatcher/units',                        body: null,                                                                                                    token: medicalToken,    expectedStatus: 403 },
    { label: 'medical: GET /api/admin/users → 403',                    method: 'GET',  path: '/api/admin/users',                             body: null,                                                                                                    token: medicalToken,    expectedStatus: 403 },
    { label: 'medical: GET /api/assessments/{id} → 403',               method: 'GET',  path: `/api/assessments/${sentinelEmergencyId}`,      body: null,                                                                                                    token: medicalToken,    expectedStatus: 403 },
  ];

  for (const tc of testCases) {
    const res = tc.token === null
      ? callNoToken(tc.method, tc.path, tc.body)
      : callWithToken(tc.method, tc.path, tc.token, tc.body);
    check(res, { [tc.label]: r => [].concat(tc.expectedStatus).includes(r.status) });
  }
}
