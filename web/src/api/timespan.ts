// .NET TimeSpan strings ("[-][d.]hh:mm:ss[.fffffff]") to seconds and back.

export function toSeconds(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value.trim());
  if (!match) return null;
  const [, sign, days, hours, minutes, seconds, fraction] = match;
  const total = Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds) + (fraction ? Number(`0.${fraction}`) : 0);
  return sign ? -total : total;
}

export function fromSeconds(total: number): string {
  const whole = Math.floor(Math.abs(total));
  const days = Math.floor(whole / 86400);
  const pad = (n: number) => String(n).padStart(2, '0');
  const time = `${pad(Math.floor((whole % 86400) / 3600))}:${pad(Math.floor((whole % 3600) / 60))}:${pad(whole % 60)}`;
  return `${total < 0 ? '-' : ''}${days ? `${days}.` : ''}${time}`;
}
