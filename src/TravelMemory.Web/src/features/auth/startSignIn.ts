import { buildSignInUrl, shouldStartSignIn } from './signIn';

const lastAttemptKey = 'travel-memory.sign-in-attempted-at';

// Returns false when a sign-in was just attempted, so the caller shows the error instead.
export function startSignIn() {
  const now = Date.now();
  if (!shouldStartSignIn(readLastAttempt(), now)) {
    return false;
  }

  try {
    sessionStorage.setItem(lastAttemptKey, String(now));
  } catch {
    // Without storage the loop guard is lost, but signing in still works.
  }

  window.location.assign(buildSignInUrl(window.location));
  return true;
}

function readLastAttempt() {
  try {
    const value = sessionStorage.getItem(lastAttemptKey);
    return value === null ? null : Number(value);
  } catch {
    return null;
  }
}
