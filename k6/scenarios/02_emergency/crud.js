import { check } from 'k6';
import { get, post } from '../../lib/http.js';
import { EMERGENCY_TYPE_ID } from '../../config.js';

export default function(data) {
  const adminToken = data.admin.accessToken;
  const ts = Date.now();
  const sentinelDesc = `k6-crud-sentinel-${ts}`;

  const createRes = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: sentinelDesc,
    address: '42 CRUD Lane',
  });
  check(createRes, { 'emergency crud: valid create returns 201': r => r.status === 201 });
  let emergencyId = null;
  if (createRes.status === 201 || createRes.status === 200) {
    const body = JSON.parse(createRes.body);
    emergencyId = body.id;
    console.log(`[emergency_crud] emergencyId=${emergencyId}`);
    check(body, {
      'emergency crud: created emergency has valid GUID id': b => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(b.id),
    });
    const loc = createRes.headers['Location'] || createRes.headers['location'] || '';
    check({ loc, id: emergencyId }, {
      'emergency crud: Location header contains emergency id': o => o.loc.includes(o.id),
    });
  }

  const resMissingType = post('/api/emergencies', adminToken, {
    description: 'no type',
    address: 'nowhere',
  });
  check(resMissingType, { 'emergency crud: missing emergencyTypeId returns 400': r => r.status === 400 });

  const resMissingDesc = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    address: 'somewhere',
  });
  check(resMissingDesc, { 'emergency crud: missing description returns 400': r => r.status === 400 });

  const resMissingAddr = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: 'no address',
  });
  check(resMissingAddr, { 'emergency crud: missing address returns 400': r => r.status === 400 });

  const resEmptyDesc = post('/api/emergencies', adminToken, {
    emergencyTypeId: EMERGENCY_TYPE_ID,
    description: '',
    address: 'somewhere',
  });
  check(resEmptyDesc, { 'emergency crud: empty description returns 400': r => r.status === 400 });

  const listRes = get('/api/emergencies', adminToken);
  check(listRes, { 'emergency crud: list without filters returns 200': r => r.status === 200 });
  if (listRes.status === 200) {
    const listBody = JSON.parse(listRes.body);
    check(listBody, {
      'emergency crud: list items is array':          b => Array.isArray(b.items),
      'emergency crud: list total is non-negative integer': b => Number.isInteger(b.total) && b.total >= 0,
      'emergency crud: list page equals 1 by default': b => b.page === 1,
      'emergency crud: list pageSize equals 20 by default': b => b.pageSize === 20,
    });
  }

  const listOpenRes = get('/api/emergencies?status=OPEN', adminToken);
  check(listOpenRes, { 'emergency crud: list with status=OPEN returns 200': r => r.status === 200 });
  if (listOpenRes.status === 200) {
    const openBody = JSON.parse(listOpenRes.body);
    if (openBody.items && openBody.items.length > 0) {
      check(openBody, { 'emergency crud: all items in status=OPEN response have status OPEN': b => b.items.every(i => i.status === 'OPEN') });
    }
  }

  const listPageRes = get('/api/emergencies?page=1&pageSize=2', adminToken);
  check(listPageRes, { 'emergency crud: list page=1&pageSize=2 returns 200': r => r.status === 200 });
  if (listPageRes.status === 200) {
    const pageBody = JSON.parse(listPageRes.body);
    check(pageBody, { 'emergency crud: page=1&pageSize=2 returns at most 2 items': b => b.items.length <= 2 });
  }

  const listAscRes = get('/api/emergencies?sortBy=created_at&order=asc&pageSize=50', adminToken);
  check(listAscRes, { 'emergency crud: sort asc returns 200': r => r.status === 200 });
  if (listAscRes.status === 200) {
    const ascBody = JSON.parse(listAscRes.body);
    if (ascBody.items && ascBody.items.length >= 2) {
      const first = ascBody.items[0];
      const last  = ascBody.items[ascBody.items.length - 1];
      check({ first, last }, {
        'emergency crud: sort asc: first createdAt <= last createdAt': o =>
          new Date(o.first.createdAt || o.first.created_at) <= new Date(o.last.createdAt || o.last.created_at),
      });
    }
  }

  const listDescRes = get('/api/emergencies?sortBy=created_at&order=desc&pageSize=50', adminToken);
  check(listDescRes, { 'emergency crud: sort desc returns 200': r => r.status === 200 });
  if (listDescRes.status === 200) {
    const descBody = JSON.parse(listDescRes.body);
    if (descBody.items && descBody.items.length >= 2) {
      const first = descBody.items[0];
      const last  = descBody.items[descBody.items.length - 1];
      check({ first, last }, {
        'emergency crud: sort desc: first createdAt >= last createdAt': o =>
          new Date(o.first.createdAt || o.first.created_at) >= new Date(o.last.createdAt || o.last.created_at),
      });
    }
  }

  if (emergencyId) {
    const listQRes = get(`/api/emergencies?q=${encodeURIComponent(sentinelDesc)}`, adminToken);
    check(listQRes, { 'emergency crud: q search returns 200': r => r.status === 200 });
    if (listQRes.status === 200) {
      const qBody = JSON.parse(listQRes.body);
      check(qBody, {
        'emergency crud: q search returns sentinel emergency in results': b =>
          b.items && b.items.some(i => i.id === emergencyId),
      });
    }
  }

  const futureTs = Date.now() + 86400000;
  const listFutureRes = get(`/api/emergencies?fromTs=${futureTs}&toTs=${futureTs + 3600000}`, adminToken);
  check(listFutureRes, { 'emergency crud: future time range returns 200': r => r.status === 200 });
  if (listFutureRes.status === 200) {
    const futureBody = JSON.parse(listFutureRes.body);
    check(futureBody, { 'emergency crud: future time range returns 0 results': b => b.total === 0 || (b.items && b.items.length === 0) });
  }

  if (emergencyId) {
    const getRes = get(`/api/emergencies/${emergencyId}`, adminToken);
    check(getRes, { 'emergency crud: GET by valid id returns 200': r => r.status === 200 });
    if (getRes.status === 200) {
      const getBody = JSON.parse(getRes.body);
      check(getBody, { 'emergency crud: GET by id returns matching id': b => b.id === emergencyId });
    }
  }

  const get404Res = get('/api/emergencies/00000000-0000-0000-0000-000000000001', adminToken);
  check(get404Res, { 'emergency crud: GET nonexistent GUID returns 404': r => r.status === 404 });

  const get400Res = get('/api/emergencies/notanid', adminToken);
  check(get400Res, { 'emergency crud: GET non-GUID string returns 400': r => r.status === 400 });

  const getEmptyRes = get('/api/emergencies/', adminToken);
  check(getEmptyRes, { 'emergency crud: GET /api/emergencies/ returns 404 or 400': r => r.status === 404 || r.status === 400 });
}
