import { register, login } from './lib/auth.js';
import { CITY_ID } from './config.js';

const USERS = [
  { key: 'admin',      email: 'admin@test.com',      password: 'TestPass123!', role: 'Admin',      department: null },
  { key: 'dispatcher', email: 'dispatcher@test.com', password: 'TestPass123!', role: 'Dispatcher', department: null },
  { key: 'fire',       email: 'fire@test.com',       password: 'TestPass123!', role: 'Responder',  department: 'Fire' },
  { key: 'police',     email: 'police@test.com',     password: 'TestPass123!', role: 'Responder',  department: 'Police' },
  { key: 'medical',    email: 'medical@test.com',    password: 'TestPass123!', role: 'Responder',  department: 'Medical' },
];

export function setup() {
  const result = {};

  for (const u of USERS) {
    const payload = {
      email: u.email,
      password: u.password,
      role: u.role,
      firstName: u.key.charAt(0).toUpperCase() + u.key.slice(1),
      lastName: 'User',
      cityId: CITY_ID,
    };
    if (u.department) payload.department = u.department;

    const reg = register(payload);
    if (reg.status !== 200 && reg.status !== 201 && reg.status !== 409) {
      throw new Error(`Unexpected register status for ${u.email}: ${reg.status} ${reg.body}`);
    }

    const session = login(u.email, u.password);
    result[u.key] = {
      accessToken:  session.accessToken,
      refreshToken: session.refreshToken,
      userId:       session.userId,
    };
    console.log(`[setup] ${u.key} userId=${session.userId}`);
  }

  return result;
}

export default function() {}
