/** Amounts are integer cents with a currency (api-contract.md). */
export function formatMoney(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(cents / 100);
}

/** Cents as a price field shows them: whole currency units, with cents only when there are any. */
export function centsToPrice(cents: number): string {
  return cents % 100 === 0 ? String(cents / 100) : (cents / 100).toFixed(2);
}

/** A price field as integer cents (api-contract.md), or `null` when it is empty or not a number. */
export function priceToCents(price: string): number | null {
  const trimmed = price.trim();
  if (trimmed === '') return null;
  if (!/^\d+(?:\.\d{1,2})?$/.test(trimmed)) return null;
  return Math.round(Number(trimmed) * 100);
}

/** A number input's model is a number, or `null` when it is emptied; a draft keeps it as typed. */
export function typed(value: string | number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}

/** What Square says the price is, once it holds the record (ADR 0032): shown, never edited here. */
export function squarePrice(cents: number | null, currency: string): string {
  return cents === null
    ? 'Square has not reported a price yet.'
    : `Price in Square: ${formatMoney(cents, currency)}`;
}
