import { requestJson } from '../../api/http';
import type { CurrentUser } from './types';

export function getCurrentUser(signal?: AbortSignal) {
  return requestJson<CurrentUser>('/api/auth/me', { signal });
}
