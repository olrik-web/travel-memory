import { requestJson } from '../../api/http';
import type { CreateTripRequest, Trip, TripListResponse } from './types';

export function listTrips(signal?: AbortSignal) {
  return requestJson<TripListResponse>('/api/trips/', { signal });
}

export function getTrip(id: string, signal?: AbortSignal) {
  return requestJson<Trip>(`/api/trips/${encodeURIComponent(id)}`, { signal });
}

export function createTrip(request: CreateTripRequest) {
  return requestJson<Trip>('/api/trips/', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });
}
