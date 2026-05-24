import http from 'k6/http';
import { BASE_URL } from '../config.js';

export function login(email, password) {
  const res = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (res.status !== 200) throw new Error(`Login failed for ${email}: ${res.status} ${res.body}`);
  return JSON.parse(res.body);
}

export function register(payload) {
  return http.post(
    `${BASE_URL}/api/auth/register`,
    JSON.stringify(payload),
    { headers: { 'Content-Type': 'application/json' } }
  );
}

export function refresh(refreshToken) {
  return http.post(
    `${BASE_URL}/api/auth/refresh`,
    JSON.stringify({ refreshToken }),
    { headers: { 'Content-Type': 'application/json' } }
  );
}

export function logout(refreshToken, accessToken) {
  return http.post(
    `${BASE_URL}/api/auth/logout`,
    JSON.stringify({ refreshToken, accessToken }),
    { headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${accessToken}` } }
  );
}
