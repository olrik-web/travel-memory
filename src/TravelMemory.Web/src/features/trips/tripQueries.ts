import { skipToken, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createTrip, getTrip, listTrips, updateTrip } from './tripApi';
import type { CreateTripRequest, UpdateTripRequest } from './types';

export const tripKeys = {
  all: ['trips'] as const,
  detail: (tripId: string) => ['trips', tripId] as const,
};

export function useTrips() {
  return useQuery({
    queryKey: tripKeys.all,
    queryFn: ({ signal }) => listTrips(signal),
  });
}

export function useTrip(tripId: string | undefined) {
  return useQuery({
    queryKey: tripKeys.detail(tripId ?? ''),
    queryFn: tripId ? ({ signal }) => getTrip(tripId, signal) : skipToken,
  });
}

export function useCreateTrip() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateTripRequest) => createTrip(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tripKeys.all }),
  });
}

export function useUpdateTrip(tripId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: UpdateTripRequest) => updateTrip(tripId, request),
    onSuccess: (trip) => {
      queryClient.setQueryData(tripKeys.detail(trip.id), trip);
      return queryClient.invalidateQueries({ queryKey: tripKeys.all });
    },
  });
}
