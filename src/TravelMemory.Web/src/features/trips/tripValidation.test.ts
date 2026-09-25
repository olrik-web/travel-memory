import { describe, expect, it } from 'vitest';
import { maxTitleLength, validateTrip } from './tripValidation';

describe('validateTrip', () => {
  it('accepts a title without dates', () => {
    expect(validateTrip({ title: 'Summer in Tuscany' })).toEqual({});
  });

  it('requires a non-blank title', () => {
    expect(validateTrip({ title: '   ' }).title).toEqual(['Enter a title for the trip.']);
  });

  it('limits the trimmed title length', () => {
    const title = ` ${'a'.repeat(maxTitleLength)} `;

    expect(validateTrip({ title })).toEqual({});
    expect(validateTrip({ title: `${title}a` }).title).toHaveLength(1);
  });

  it('rejects an end date before the start date', () => {
    const errors = validateTrip({
      title: 'Reversed trip',
      startDate: '2026-08-20',
      endDate: '2026-08-10',
    });

    expect(errors.endDate).toEqual(['The end date cannot be before the start date.']);
  });
});
