const UNIT_MB: Record<string, number> = { KB: 1 / 1024, MB: 1, GB: 1024, TB: 1024 * 1024 };

/** Size in MB for values like "8GB" or "1 TB"; null when the text is not a size. */
export function sizeInMb(value: string): number | null {
  const match = /^\s*(\d+(?:\.\d+)?)\s*(KB|MB|GB|TB)\b/i.exec(value);
  return match ? parseFloat(match[1]) * UNIT_MB[match[2].toUpperCase()] : null;
}

/** Sorts memory and storage options by real size: 4GB, 8GB, 128GB, 256GB, 1TB. */
export function compareSizes(a: string, b: string): number {
  const x = sizeInMb(a);
  const y = sizeInMb(b);

  if (x !== null && y !== null) {
    return x - y || a.localeCompare(b);
  }

  return x !== null ? -1 : y !== null ? 1 : a.localeCompare(b);
}
