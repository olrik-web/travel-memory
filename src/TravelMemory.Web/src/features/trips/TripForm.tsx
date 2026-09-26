import { type FormEvent, useState } from 'react';
import { ApiError } from '../../api/http';
import { maxTitleLength, type TripFieldErrors, validateTrip } from './tripValidation';
import type { CreateTripRequest } from './types';

export interface TripFormValues {
  title: string;
  startDate: string;
  endDate: string;
}

const emptyValues: TripFormValues = { title: '', startDate: '', endDate: '' };

interface TripFormProps {
  initialValues?: TripFormValues;
  submitLabel: string;
  isSubmitting: boolean;
  // Rejects with an ApiError when the API refuses the trip; its field errors are shown
  // next to the fields.
  onSubmit: (request: CreateTripRequest) => Promise<void>;
}

// Used both to create and to edit a trip; the page owns the request and what happens next.
export function TripForm({
  initialValues = emptyValues,
  submitLabel,
  isSubmitting,
  onSubmit,
}: TripFormProps) {
  const [title, setTitle] = useState(initialValues.title);
  const [startDate, setStartDate] = useState(initialValues.startDate);
  const [endDate, setEndDate] = useState(initialValues.endDate);
  const [fieldErrors, setFieldErrors] = useState<TripFieldErrors>({});
  const [submitError, setSubmitError] = useState<string>();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitError(undefined);

    const request: CreateTripRequest = {
      title,
      startDate: startDate || null,
      endDate: endDate || null,
    };
    const validationErrors = validateTrip(request);
    setFieldErrors(validationErrors);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    try {
      await onSubmit(request);
    } catch (requestError: unknown) {
      if (requestError instanceof ApiError && requestError.problem?.errors) {
        setFieldErrors(requestError.problem.errors);
      } else {
        setSubmitError(
          requestError instanceof Error
            ? requestError.message
            : 'The trip could not be saved. Try again.',
        );
      }
    }
  }

  return (
    <form className="trip-form" onSubmit={handleSubmit} noValidate>
      <div className="field">
        <label htmlFor="title">Title</label>
        <input
          id="title"
          name="title"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          maxLength={maxTitleLength}
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
          <label htmlFor="startDate">Start date</label>
          <input
            id="startDate"
            name="startDate"
            type="date"
            value={startDate}
            onChange={(event) => setStartDate(event.target.value)}
          />
        </div>

        <div className="field">
          <label htmlFor="endDate">End date</label>
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

      <p className="form-hint">Dates are optional and can be added later.</p>

      {submitError && (
        <div className="form-error" role="alert">
          {submitError}
        </div>
      )}

      <div className="form-actions">
        <button className="button button-primary" type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving...' : submitLabel}
        </button>
      </div>
    </form>
  );
}
