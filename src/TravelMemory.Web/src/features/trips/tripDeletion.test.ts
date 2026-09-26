import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/http';
import { confirmsTripTitle, describeTripDeletionError } from './tripDeletion';

describe('confirmsTripTitle', () => {
  it('requires the exact title, ignoring surrounding spaces', () => {
    expect(confirmsTripTitle(' Poland ', 'Poland')).toBe(true);
    expect(confirmsTripTitle('poland', 'Poland')).toBe(false);
    expect(confirmsTripTitle('', 'Poland')).toBe(false);
  });
});

describe('describeTripDeletionError', () => {
  it('explains that a trip cannot be deleted while photos are processing', () => {
    expect(describeTripDeletionError(new ApiError(409))).toContain('still being processed');
    expect(describeTripDeletionError(new Error('offline'))).toBe(
      'The trip could not be deleted. Try again.',
    );
  });
});
