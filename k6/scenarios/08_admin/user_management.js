import { check } from 'k6';
import http from 'k6/http';
import { get, post, patch, del } from '../../lib/http.js';
import { login } from '../../lib/auth.js';
import { BASE_URL } from '../../config.js';

export default function(data) {
  const adminToken = data.admin.accessToken;
  const ts = Date.now();
  const uniqueEmail = `k6_admin_mgmt_${ts}@test.com`;

  // Note starting user count
  const initListRes = get('/api/admin/users', adminToken);
  check(initListRes, { 'admin users: initial GET /api/admin/users returns 200': r => r.status === 200 });
  let startingTotal = 0;
  if (initListRes.status === 200) {
    startingTotal = JSON.parse(initListRes.body).total || 0;
  }

  // 1. Create Dispatcher
  const createRes = post('/api/admin/users', adminToken, {
    email: uniqueEmail,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'Admin',
    lastName: 'Managed',
  });
  check(createRes, { 'admin users: POST /api/admin/users returns 201': r => r.status === 201 });
  const locHeader = createRes.headers['Location'] || createRes.headers['location'] || '';
  check({ loc: locHeader }, { 'admin users: POST response includes Location header': o => o.loc.length > 0 });

  let userId = null;
  if (createRes.status === 201) {
    const createBody = JSON.parse(createRes.body);
    userId = createBody.userId || createBody.id;
    console.log(`[admin_users] created userId=${userId}`);
  }

  // 2. List — total >= startingTotal + 1
  const listRes = get('/api/admin/users', adminToken);
  check(listRes, { 'admin users: GET list after create returns 200': r => r.status === 200 });
  if (listRes.status === 200) {
    const listBody = JSON.parse(listRes.body);
    check(listBody, {
      'admin users: total increased by at least 1': b => b.total >= startingTotal + 1,
    });
  }

  // 3. Filter by role=Dispatcher
  const roleFilterRes = get('/api/admin/users?role=Dispatcher', adminToken);
  check(roleFilterRes, { 'admin users: filter by role=Dispatcher returns 200': r => r.status === 200 });
  if (roleFilterRes.status === 200) {
    const rfBody = JSON.parse(roleFilterRes.body);
    if (rfBody.users && rfBody.users.length > 0) {
      check(rfBody.users, {
        'admin users: role=Dispatcher filter — all returned users have role Dispatcher': u => u.every(x => x.role === 'Dispatcher'),
      });
    }
  }

  // 4. Pagination
  const pageRes = get('/api/admin/users?page=1&pageSize=2', adminToken);
  check(pageRes, { 'admin users: pagination page=1&pageSize=2 returns 200': r => r.status === 200 });
  if (pageRes.status === 200) {
    const pageBody = JSON.parse(pageRes.body);
    const items = pageBody.users || pageBody.items || [];
    check(items, { 'admin users: pagination returns at most 2 items': a => a.length <= 2 });
  }

  if (!userId) {
    console.warn('[admin_users] no userId from create — skipping PATCH/DELETE tests');
    return;
  }

  // 5. PATCH profile
  const patchRes = patch(`/api/admin/users/${userId}`, adminToken, { firstName: 'Updated' });
  check(patchRes, { 'admin users: PATCH profile returns 204': r => r.status === 204 });

  const patchedSession = login(uniqueEmail, 'TestPass123!');
  const meRes = http.get(`${BASE_URL}/api/me`, {
    headers: { 'Authorization': `Bearer ${patchedSession.accessToken}` },
    tags: { type: 'functional' },
  });
  if (meRes.status === 200) {
    const me = JSON.parse(meRes.body);
    check(me, { 'admin users: PATCH firstName verified via /api/me': b => b.firstName === 'Updated' });
  }

  // 6. PATCH role to Responder/Fire
  const roleRes = patch(`/api/admin/users/${userId}/role`, adminToken, { role: 'Responder', department: 'Fire' });
  check(roleRes, { 'admin users: PATCH role to Responder/Fire returns 204': r => r.status === 204 });

  const roleSession = login(uniqueEmail, 'TestPass123!');
  const meRoleRes = http.get(`${BASE_URL}/api/me`, {
    headers: { 'Authorization': `Bearer ${roleSession.accessToken}` },
    tags: { type: 'functional' },
  });
  if (meRoleRes.status === 200) {
    const meRole = JSON.parse(meRoleRes.body);
    check(meRole, {
      'admin users: PATCH role verified — role is Responder': b => b.role === 'Responder',
      'admin users: PATCH role verified — department is Fire': b => b.department === 'Fire',
    });
  }

  // 7. PATCH role Responder without department → 400
  const noDeptRes = patch(`/api/admin/users/${userId}/role`, adminToken, { role: 'Responder' });
  check(noDeptRes, { 'admin users: PATCH role Responder without department returns 400': r => r.status === 400 });

  // 8. PATCH invalid role → 400
  const badRoleRes = patch(`/api/admin/users/${userId}/role`, adminToken, { role: 'InvalidRole' });
  check(badRoleRes, { 'admin users: PATCH invalid role returns 400': r => r.status === 400 });

  // 9. DELETE user → 204
  const deleteRes = del(`/api/admin/users/${userId}`, adminToken);
  check(deleteRes, { 'admin users: DELETE user returns 204': r => r.status === 204 });

  // 10. Login after delete → 401
  const deletedLoginRes = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: uniqueEmail, password: 'TestPass123!' }),
    { headers: { 'Content-Type': 'application/json' }, tags: { type: 'functional' } }
  );
  check(deletedLoginRes, { 'admin users: login after delete returns 401': r => r.status === 401 });

  // 11. DELETE again → 404
  const deleteAgainRes = del(`/api/admin/users/${userId}`, adminToken);
  check(deleteAgainRes, { 'admin users: second DELETE returns 404': r => r.status === 404 });

  // 12. Duplicate email → 409
  const dupRes = post('/api/admin/users', adminToken, {
    email: uniqueEmail,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'Dup',
    lastName: 'User',
  });
  check(dupRes, { 'admin users: POST duplicate email returns 409': r => r.status === 409 });

  // 13. PATCH invalid GUID → 400
  const patchInvalidGuidRes = patch('/api/admin/users/notanid', adminToken, { firstName: 'X' });
  check(patchInvalidGuidRes, { 'admin users: PATCH invalid GUID returns 400': r => r.status === 400 });

  // 14. PATCH nonexistent GUID → 404
  const patchNotFoundRes = patch('/api/admin/users/00000000-0000-0000-0000-000000000001', adminToken, { firstName: 'X' });
  check(patchNotFoundRes, { 'admin users: PATCH nonexistent GUID returns 404': r => r.status === 404 });

  // 15. DELETE invalid GUID → 400
  const delInvalidGuidRes = del('/api/admin/users/notanid', adminToken);
  check(delInvalidGuidRes, { 'admin users: DELETE invalid GUID returns 400': r => r.status === 400 });
}
