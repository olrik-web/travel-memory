import { type FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { confirmsTripTitle, describeTripDeletionError } from './tripDeletion';
import { useDeleteTrip } from './tripQueries';
import type { Trip } from './types';

export function DeleteTripSection({ trip }: { trip: Trip }) {
  const navigate = useNavigate();
  const deleteTrip = useDeleteTrip();
  const [isConfirming, setIsConfirming] = useState(false);
  const [typedTitle, setTypedTitle] = useState('');

  function cancel() {
    setIsConfirming(false);
    setTypedTitle('');
    deleteTrip.reset();
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await deleteTrip.mutateAsync(trip.id);
    await navigate('/trips');
  }

  return (
    <section className="danger-zone" aria-labelledby="delete-trip-heading">
      <h2 id="delete-trip-heading">Delete trip</h2>
      <p>The trip, its photos, and its imports are deleted permanently.</p>

      {!isConfirming ? (
        <button
          className="button button-danger"
          type="button"
          onClick={() => setIsConfirming(true)}
        >
          Delete trip
        </button>
      ) : (
        <form
          onSubmit={(event) => {
            handleSubmit(event).catch(() => {
              // The mutation's error is shown below.
            });
          }}
        >
          <div className="field">
            <label htmlFor="confirm-trip-title">
              Type <strong>{trip.title}</strong> to confirm
            </label>
            <input
              id="confirm-trip-title"
              value={typedTitle}
              onChange={(event) => setTypedTitle(event.target.value)}
              autoComplete="off"
            />
          </div>

          {deleteTrip.error && (
            <div className="form-error" role="alert">
              {describeTripDeletionError(deleteTrip.error)}
            </div>
          )}

          <div className="form-actions">
            <button
              className="button button-danger"
              type="submit"
              disabled={!confirmsTripTitle(typedTitle, trip.title) || deleteTrip.isPending}
            >
              {deleteTrip.isPending ? 'Deleting...' : 'Delete permanently'}
            </button>
            <button className="button button-secondary" type="button" onClick={cancel}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
