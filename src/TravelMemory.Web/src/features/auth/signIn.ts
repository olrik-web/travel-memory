interface PageLocation {
  pathname: string;
  search: string;
  hash: string;
}

// If the API still answers 401 right after a sign-in, redirecting again would bounce
// between the app and the identity provider forever, because the provider signs the user
// in without a prompt. A second attempt within this window shows the error instead.
export const signInRetryWindowMs = 30_000;

export function buildSignInUrl(location: PageLocation) {
  const returnUrl = `${location.pathname}${location.search}${location.hash}`;
  return `/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export function shouldStartSignIn(lastAttemptAt: number | null, now: number) {
  return lastAttemptAt === null || now - lastAttemptAt >= signInRetryWindowMs;
}
