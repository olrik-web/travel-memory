import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/http';

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // A 4xx such as 404 will not change on retry, so show it immediately.
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  });
}
