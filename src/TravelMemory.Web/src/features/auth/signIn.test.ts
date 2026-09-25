import { describe, expect, it } from 'vitest';
import { buildSignInUrl, shouldStartSignIn, signInRetryWindowMs } from './signIn';

describe('buildSignInUrl', () => {
  it('returns to the current page after signing in', () => {
    expect(
      buildSignInUrl({ pathname: '/trips/42', search: '?tab=photos', hash: '#top' }),
    ).toBe('/api/auth/login?returnUrl=%2Ftrips%2F42%3Ftab%3Dphotos%23top');
  });
});

describe('shouldStartSignIn', () => {
  it('starts a sign-in when none was attempted', () => {
    expect(shouldStartSignIn(null, 1_000)).toBe(true);
  });

  it('does not start another sign-in right after the previous one', () => {
    expect(shouldStartSignIn(1_000, 1_000 + signInRetryWindowMs - 1)).toBe(false);
  });

  it('starts a sign-in again once the retry window has passed', () => {
    expect(shouldStartSignIn(1_000, 1_000 + signInRetryWindowMs)).toBe(true);
  });
});
