import { describe, expect, it } from 'vitest';
import { maxTitleLength, validateTrip } from './tripValidation';
import type { CreateTripRequest } from './types';

function trip(title: string, startDate: string | null = null, endDate: string | null = null) {
  return { title, startDate, endDate } satisfies CreateTripRequest;
}

describe('validateTrip', () => {
  it('accepts a title without dates', () => {
    expect(validateTrip(trip('Summer in Tuscany'))).toEqual({});
  });

  it('requires a non-blank title', () => {
    expect(validateTrip(trip('   ')).title).toEqual(['Enter a title for the trip.']);
  });

  it('limits the trimmed title length', () => {
    const title = ` ${'a'.repeat(maxTitleLength)} `;

    expect(validateTrip(trip(title))).toEqual({});
    expect(validateTrip(trip(`${title}a`)).title).toHaveLength(1);
  });

  it('rejects an end date before the start date', () => {
    const errors = validateTrip(trip('Reversed trip', '2026-08-20', '2026-08-10'));

    expect(errors.endDate).toEqual(['The end date cannot be before the start date.']);
  });
});
