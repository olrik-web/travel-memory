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
          <Route path="/trips/:tripId" element={<p>Detaljerute åbnet</p>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText('Titel'), 'Sommer i Toscana');
    fireEvent.change(screen.getByLabelText('Startdato'), {
      target: { value: '2026-07-14' },
    });
    fireEvent.change(screen.getByLabelText('Slutdato'), {
      target: { value: '2026-07-04' },
    });
    await user.click(screen.getByRole('button', { name: 'Opret rejse' }));

    expect(
      await screen.findByText('Slutdatoen må ikke ligge før startdatoen.'),
    ).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Slutdato'), {
      target: { value: '2026-07-24' },
    });
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          id: 'b7eac69b-f395-4284-b77d-dfbccad43f67',
          title: 'Sommer i Toscana',
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
    await user.click(screen.getByRole('button', { name: 'Opret rejse' }));

    expect(await screen.findByText('Detaljerute åbnet')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/trips/',
      expect.objectContaining({ method: 'POST' }),
    );
  });
});
