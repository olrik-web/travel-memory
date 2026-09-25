import type { CreateTripRequest } from './types';

export type TripFieldErrors = Partial<Record<keyof CreateTripRequest, string[]>>;

export const maxTitleLength = 200;

// Mirrors the API's validation so the form can respond without a round trip; the API
// remains authoritative and its errors are shown the same way.
export function validateTrip(request: CreateTripRequest): TripFieldErrors {
  const errors: TripFieldErrors = {};

  const title = request.title?.trim() ?? '';
  if (!title) {
    errors.title = ['Enter a title for the trip.'];
  } else if (title.length > maxTitleLength) {
    errors.title = [`The title can be at most ${maxTitleLength} characters.`];
  }

  // Dates are ISO calendar dates (YYYY-MM-DD), so string comparison orders them correctly.
  if (request.startDate && request.endDate && request.endDate < request.startDate) {
    errors.endDate = ['The end date cannot be before the start date.'];
  }

  return errors;
}
