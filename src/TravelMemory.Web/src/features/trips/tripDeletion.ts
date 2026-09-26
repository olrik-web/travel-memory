import { ApiError } from '../../api/http';

// Typing the title guards against deleting the wrong trip; surrounding spaces are ignored.
export function confirmsTripTitle(typedTitle: string, title: string) {
  return typedTitle.trim() === title.trim();
}

export function describeTripDeletionError(error: unknown) {
  return error instanceof ApiError && error.status === 409
    ? 'Photos of this trip are still being processed. Try again when the import has finished.'
    : 'The trip could not be deleted. Try again.';
}
