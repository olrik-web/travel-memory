const dateFormatter = new Intl.DateTimeFormat('en-GB', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

function formatCalendarDate(value: string) {
  return dateFormatter.format(new Date(`${value}T00:00:00Z`));
}

export function formatTripDates(startDate: string | null, endDate: string | null) {
  if (startDate && endDate) {
    return `${formatCalendarDate(startDate)} - ${formatCalendarDate(endDate)}`;
  }

  if (startDate) {
    return `From ${formatCalendarDate(startDate)}`;
  }

  if (endDate) {
    return `Until ${formatCalendarDate(endDate)}`;
  }

  return 'No dates set';
}
