import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getCurrentUser } from './authApi';
import { clearSignInAttempt } from './startSignIn';

export function useCurrentUser() {
  const query = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: ({ signal }) => getCurrentUser(signal),
    staleTime: Infinity,
  });
  const signedIn = query.isSuccess;

  useEffect(() => {
    if (signedIn) {
      clearSignInAttempt();
    }
  }, [signedIn]);

  return query;
}
