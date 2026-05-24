import { check } from 'k6';
import { register } from '../../lib/auth.js';
import { CITY_ID } from '../../config.js';

export default function(data) {
  const ts = Date.now();
  const vu = __VU;

  const res1 = register({
    email: `k6_reg_${ts}_${vu}_dispatcher@test.com`,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'K6',
    lastName: 'Dispatcher',
    cityId: CITY_ID,
  });
  check(res1, { 'register: valid Dispatcher returns 200': r => r.status === 200 || r.status === 201 });
  const body1 = JSON.parse(res1.body);
  check(body1, { 'register: valid Dispatcher has userId': b => typeof b.userId === 'string' && b.userId.length > 0 });

  const res2 = register({
    email: `k6_reg_${ts}_${vu}_fire@test.com`,
    password: 'TestPass123!',
    role: 'Responder',
    firstName: 'K6',
    lastName: 'Fire',
    cityId: CITY_ID,
    department: 'Fire',
  });
  check(res2, { 'register: valid Responder/Fire returns 200': r => r.status === 200 || r.status === 201 });

  const dupEmail = `k6_reg_${ts}_${vu}_dup@test.com`;
  const resDup1 = register({
    email: dupEmail,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'Dup',
    lastName: 'One',
    cityId: CITY_ID,
  });
  check(resDup1, { 'register: first registration of duplicate email returns 200': r => r.status === 200 || r.status === 201 });

  const resDup2 = register({
    email: dupEmail,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'Dup',
    lastName: 'Two',
    cityId: CITY_ID,
  });
  check(resDup2, { 'register: duplicate email returns 409': r => r.status === 409 });

  const resNoDept = register({
    email: `k6_reg_${ts}_${vu}_nodept@test.com`,
    password: 'TestPass123!',
    role: 'Responder',
    firstName: 'No',
    lastName: 'Dept',
    cityId: CITY_ID,
  });
  check(resNoDept, { 'register: Responder without department returns 400': r => r.status === 400 });

  const resInvalidRole = register({
    email: `k6_reg_${ts}_${vu}_badrole@test.com`,
    password: 'TestPass123!',
    role: 'SuperAdmin',
    firstName: 'Bad',
    lastName: 'Role',
    cityId: CITY_ID,
  });
  check(resInvalidRole, { 'register: invalid role returns 400': r => r.status === 400 });

  const resMissingEmail = register({
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'No',
    lastName: 'Email',
    cityId: CITY_ID,
  });
  check(resMissingEmail, { 'register: missing email returns 400': r => r.status === 400 });

  const resMissingPassword = register({
    email: `k6_reg_${ts}_${vu}_nopw@test.com`,
    role: 'Dispatcher',
    firstName: 'No',
    lastName: 'Password',
    cityId: CITY_ID,
  });
  check(resMissingPassword, { 'register: missing password returns 400': r => r.status === 400 });

  const resMissingFirstName = register({
    email: `k6_reg_${ts}_${vu}_nofn@test.com`,
    password: 'TestPass123!',
    role: 'Dispatcher',
    lastName: 'NoFirst',
    cityId: CITY_ID,
  });
  check(resMissingFirstName, { 'register: missing firstName returns 400': r => r.status === 400 });

  const resMissingCityId = register({
    email: `k6_reg_${ts}_${vu}_nocity@test.com`,
    password: 'TestPass123!',
    role: 'Dispatcher',
    firstName: 'No',
    lastName: 'City',
  });
  check(resMissingCityId, { 'register: missing cityId returns 400': r => r.status === 400 });
}
