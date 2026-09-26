import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/http';
import { startSignIn } from '../features/auth/startSignIn';

// Any 401 means the session cookie is missing or expired, so sign in again and come back.
function signInOnUnauthorized(error: Error) {
  if (error instanceof ApiError && error.status === 401) {
    startSignIn();
  }
}

export function createQueryClient() {
  return new QueryClient({
    queryCache: new QueryCache({ onError: signInOnUnauthorized }),
    mutationCache: new MutationCache({ onError: signInOnUnauthorized }),
    defaultOptions: {
      queries: {
        // A 4xx such as 404 will not change on retry, so show it immediately.
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  });
}
