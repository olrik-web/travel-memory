import { useQuery } from '@tanstack/react-query';
import { getCurrentUser } from './authApi';

export function useCurrentUser() {
  return useQuery({
    queryKey: ['auth', 'me'],
    queryFn: ({ signal }) => getCurrentUser(signal),
    staleTime: Infinity,
  });
}
