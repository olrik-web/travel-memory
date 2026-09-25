import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NewTripPage } from './NewTripPage';

describe('new trip flow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('validates the date range and navigates after a successful create', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);

    render(
      <MemoryRouter initialEntries={['/trips/new']}>
        <Routes>
          <Route path="/trips/new" element={<NewTripPage />} />
          <Route path="/trips/:tripId" element={<p>Detail route opened</p>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText('Title'), 'Summer in Tuscany');
    fireEvent.change(screen.getByLabelText('Start date'), {
      target: { value: '2026-07-14' },
    });
    fireEvent.change(screen.getByLabelText('End date'), {
      target: { value: '2026-07-04' },
    });
    await user.click(screen.getByRole('button', { name: 'Create trip' }));

    expect(
      await screen.findByText('The end date cannot be before the start date.'),
    ).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('End date'), {
      target: { value: '2026-07-24' },
    });
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          id: 'b7eac69b-f395-4284-b77d-dfbccad43f67',
          title: 'Summer in Tuscany',
          startDate: '2026-07-14',
          endDate: '2026-07-24',
          createdAtUtc: '2026-09-01T10:00:00Z',
        }),
        {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    );
    await user.click(screen.getByRole('button', { name: 'Create trip' }));

    expect(await screen.findByText('Detail route opened')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/trips/',
      expect.objectContaining({ method: 'POST' }),
    );
  });
});
