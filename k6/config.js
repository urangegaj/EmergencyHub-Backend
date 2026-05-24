export const BASE_URL      = __ENV.BASE_URL       || 'http://localhost:5158';
export const CITY_ID       = __ENV.CITY_ID        || '10000000-0000-0000-0000-000000000001';
export const ASYNC_WAIT    = parseInt(__ENV.ASYNC_WAIT    || '8000');
export const POLL_INTERVAL = parseInt(__ENV.POLL_INTERVAL || '500');

export const EMERGENCY_TYPE_ID = __ENV.EMERGENCY_TYPE_ID || '20000000-0000-0000-0000-000000000001';

export const globalThresholds = {
  http_req_failed:                        ['rate<0.01'],
  'http_req_duration{type:functional}':   ['p(95)<800'],
  'http_req_duration{type:load}':         ['p(95)<1500'],
  'http_req_duration{scenario:s02_login}':['p(99)<300'],
};
