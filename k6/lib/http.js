import http from 'k6/http';
import { BASE_URL } from '../config.js';

http.setResponseCallback(http.expectedStatuses({ min: 200, max: 599 }));

function headers(token, extra = {}) {
  return { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}`, ...extra };
}

export function get(path, token, params = {}) {
  return http.get(`${BASE_URL}${path}`, { headers: headers(token), tags: { type: 'functional' }, ...params });
}

export function post(path, token, body, params = {}) {
  return http.post(`${BASE_URL}${path}`, JSON.stringify(body), { headers: headers(token), tags: { type: 'functional' }, ...params });
}

export function put(path, token, body, params = {}) {
  return http.put(`${BASE_URL}${path}`, JSON.stringify(body), { headers: headers(token), tags: { type: 'functional' }, ...params });
}

export function patch(path, token, body, params = {}) {
  return http.patch(`${BASE_URL}${path}`, JSON.stringify(body), { headers: headers(token), tags: { type: 'functional' }, ...params });
}

export function del(path, token, params = {}) {
  return http.del(`${BASE_URL}${path}`, null, { headers: headers(token), tags: { type: 'functional' }, ...params });
}

export function postNoAuth(path, body) {
  return http.post(`${BASE_URL}${path}`, JSON.stringify(body), { headers: { 'Content-Type': 'application/json' }, tags: { type: 'functional' } });
}

export function getNoAuth(path) {
  return http.get(`${BASE_URL}${path}`, { tags: { type: 'functional' } });
}
