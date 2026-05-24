import { sleep } from 'k6';

export function pollUntil(fn, maxWaitMs, intervalMs) {
  maxWaitMs  = maxWaitMs  || 8000;
  intervalMs = intervalMs || 500;
  const deadline = Date.now() + maxWaitMs;
  while (Date.now() < deadline) {
    if (fn()) return true;
    sleep(intervalMs / 1000);
  }
  return false;
}
