const dateFormatter = new Intl.DateTimeFormat('da-DK', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

function formatCalendarDate(value: string) {
  return dateFormatter.format(new Date(`${value}T00:00:00Z`));
}

export function formatTripDates(startDate?: string, endDate?: string) {
  if (startDate && endDate) {
    return `${formatCalendarDate(startDate)} - ${formatCalendarDate(endDate)}`;
  }

  if (startDate) {
    return `Fra ${formatCalendarDate(startDate)}`;
  }

  if (endDate) {
    return `Til ${formatCalendarDate(endDate)}`;
  }

  return 'Datoer ikke angivet';
}
