import { type FormEvent, useState } from 'react';
import { ApiError } from '../../api/http';
import { createTrip } from './tripApi';
import type { CreateTripRequest } from './types';

type FieldErrors = Partial<Record<keyof CreateTripRequest, string[]>>;

interface TripFormProps {
  onCreated: (tripId: string) => void;
}

function validate(request: CreateTripRequest): FieldErrors {
  const errors: FieldErrors = {};

  if (!request.title.trim()) {
    errors.title = ['Angiv en titel for rejsen.'];
  } else if (request.title.trim().length > 200) {
    errors.title = ['Titlen må højst være 200 tegn.'];
  }

  if (request.startDate && request.endDate && request.endDate < request.startDate) {
    errors.endDate = ['Slutdatoen må ikke ligge før startdatoen.'];
  }

  return errors;
}

export function TripForm({ onCreated }: TripFormProps) {
  const [title, setTitle] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitError(undefined);

    const request: CreateTripRequest = {
      title,
      startDate: startDate || undefined,
      endDate: endDate || undefined,
    };
    const validationErrors = validate(request);
    setFieldErrors(validationErrors);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    setIsSubmitting(true);

    try {
      const trip = await createTrip(request);
      onCreated(trip.id);
    } catch (requestError: unknown) {
      if (requestError instanceof ApiError && requestError.problem?.errors) {
        setFieldErrors(requestError.problem.errors);
      } else {
        setSubmitError(
          requestError instanceof Error
            ? requestError.message
            : 'Rejsen kunne ikke oprettes. Prøv igen.',
        );
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <form className="trip-form" onSubmit={handleSubmit} noValidate>
      <div className="field">
        <label htmlFor="title">Titel</label>
        <input
          id="title"
          name="title"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          maxLength={200}
          aria-describedby={fieldErrors.title ? 'title-error' : undefined}
          aria-invalid={fieldErrors.title ? true : undefined}
          autoFocus
        />
        {fieldErrors.title && (
          <p className="field-error" id="title-error">
            {fieldErrors.title[0]}
          </p>
        )}
      </div>

      <div className="date-fields">
        <div className="field">
          <label htmlFor="startDate">Startdato</label>
          <input
            id="startDate"
            name="startDate"
            type="date"
            value={startDate}
            onChange={(event) => setStartDate(event.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="endDate">Slutdato</label>
          <input
            id="endDate"
            name="endDate"
            type="date"
            value={endDate}
            onChange={(event) => setEndDate(event.target.value)}
            aria-describedby={fieldErrors.endDate ? 'endDate-error' : undefined}
            aria-invalid={fieldErrors.endDate ? true : undefined}
          />
          {fieldErrors.endDate && (
            <p className="field-error" id="endDate-error">
              {fieldErrors.endDate[0]}
            </p>
          )}
        </div>
      </div>

      <p className="form-hint">Datoerne er valgfrie og kan tilføjes senere.</p>

      {submitError && (
        <div className="form-error" role="alert">
          {submitError}
        </div>
      )}

      <div className="form-actions">
        <button className="button button-primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Gemmer...' : 'Opret rejse'}
        </button>
      </div>
    </form>
  );
}
