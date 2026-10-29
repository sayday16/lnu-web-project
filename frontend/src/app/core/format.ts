export function formatDuration(seconds: number): string {
  if (seconds < 1) {
    return 'менше 1 с';
  }
  const rounded = Math.round(seconds);
  const minutes = Math.floor(rounded / 60);
  const rest = rounded % 60;
  return minutes > 0 ? `${minutes} хв ${rest} с` : `${rest} с`;
}
