import { sleep } from 'k6';
import { globalThresholds } from './config.js';
import { register, login, refresh, logout } from './lib/auth.js';
import { CITY_ID } from './config.js';

import registerScenario      from './scenarios/01_auth/register.js';
import loginScenario         from './scenarios/01_auth/login.js';
import tokenLifecycleScenario from './scenarios/01_auth/token_lifecycle.js';
import authzMatrixScenario   from './scenarios/01_auth/authz_matrix.js';
import emergencyCrudScenario from './scenarios/02_emergency/crud.js';
import emergencyAssignScenario from './scenarios/02_emergency/assign.js';
import emergencyPollScenario from './scenarios/02_emergency/poll.js';
import fullLifecycleScenario from './scenarios/02_emergency/full_lifecycle.js';
import fireCasesScenario     from './scenarios/03_fire/cases.js';
import fireUnitsScenario     from './scenarios/03_fire/units.js';
import policeCasesScenario   from './scenarios/04_police/cases.js';
import policeUnitsScenario   from './scenarios/04_police/units.js';
import medicalCasesScenario  from './scenarios/05_medical/cases.js';
import medicalUnitsScenario  from './scenarios/05_medical/units.js';
import assessmentScenario    from './scenarios/06_assessment/reports.js';
import dispatcherScenario    from './scenarios/07_dispatcher/units_aggregated.js';
import adminUsersScenario    from './scenarios/08_admin/user_management.js';
import notificationsScenario from './scenarios/09_notifications/side_effects.js';

export const options = {
  thresholds: globalThresholds,
  scenarios: {
    s01_register:        { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'registerFn',        startTime: '0s'   },
    s02_login:           { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'loginFn',           startTime: '10s'  },
    s03_token_lifecycle: { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'tokenLifecycleFn',  startTime: '20s'  },
    s04_authz_matrix:    { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'authzMatrixFn',     startTime: '35s'  },
    s05_emergency_crud:  { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'emergencyCrudFn',   startTime: '70s'  },
    s06_emergency_assign:{ executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'emergencyAssignFn', startTime: '90s'  },
    s07_emergency_poll:  { executor: 'shared-iterations', vus: 2, iterations: 4, exec: 'emergencyPollFn',   startTime: '120s' },
    s08_full_lifecycle:  { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'fullLifecycleFn',   startTime: '145s' },
    s09_fire_cases:      { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'fireCasesFn',       startTime: '220s' },
    s10_fire_units:      { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'fireUnitsFn',       startTime: '240s' },
    s11_police_cases:    { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'policeCasesFn',     startTime: '260s' },
    s12_police_units:    { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'policeUnitsFn',     startTime: '280s' },
    s13_medical_cases:   { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'medicalCasesFn',    startTime: '300s' },
    s14_medical_units:   { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'medicalUnitsFn',    startTime: '320s' },
    s15_assessment:      { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'assessmentFn',      startTime: '340s' },
    s16_dispatcher:      { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'dispatcherFn',      startTime: '410s' },
    s17_admin:           { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'adminUsersFn',      startTime: '420s' },
    s18_notifications:   { executor: 'shared-iterations', vus: 1, iterations: 1, exec: 'notificationsFn',   startTime: '460s' },
  },
};

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
      throw new Error(`suite setup: register failed for ${u.email}: ${reg.status} ${reg.body}`);
    }

    const session = login(u.email, u.password);
    result[u.key] = {
      accessToken:  session.accessToken,
      refreshToken: session.refreshToken,
      userId:       session.userId,
    };
    console.log(`[suite setup] ${u.key} userId=${session.userId}`);
  }
  return result;
}

export function registerFn(data)        { return registerScenario(data); }
export function loginFn(data)           { return loginScenario(data); }
export function tokenLifecycleFn(data)  { return tokenLifecycleScenario(data); }
export function authzMatrixFn(data)     { return authzMatrixScenario(data); }
export function emergencyCrudFn(data)   { return emergencyCrudScenario(data); }
export function emergencyAssignFn(data) { return emergencyAssignScenario(data); }
export function emergencyPollFn(data)   { return emergencyPollScenario(data); }
export function fullLifecycleFn(data)   { return fullLifecycleScenario(data); }
export function fireCasesFn(data)       { return fireCasesScenario(data); }
export function fireUnitsFn(data)       { return fireUnitsScenario(data); }
export function policeCasesFn(data)     { return policeCasesScenario(data); }
export function policeUnitsFn(data)     { return policeUnitsScenario(data); }
export function medicalCasesFn(data)    { return medicalCasesScenario(data); }
export function medicalUnitsFn(data)    { return medicalUnitsScenario(data); }
export function assessmentFn(data)      { return assessmentScenario(data); }
export function dispatcherFn(data)      { return dispatcherScenario(data); }
export function adminUsersFn(data)      { return adminUsersScenario(data); }
export function notificationsFn(data)   { return notificationsScenario(data); }
