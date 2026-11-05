import { parseUtc } from '../core/api.service';

/** `datetime-local` inputs use the browser's local time; the API stores UTC (zone-less strings are UTC). */
export function toLocalInput(iso: string | null): string {
  if (!iso) {
    return '';
  }

  const date = parseUtc(iso);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}

export const toIso = (local: string): string | null => (local ? new Date(local).toISOString() : null);

export const slugify = (value: string): string =>
  value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

/** Short local date-time for table cells. */
export const when = (iso: string | null): string => (iso ? parseUtc(iso).toLocaleString() : '—');
