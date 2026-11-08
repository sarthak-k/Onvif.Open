import { fromSeconds, toSeconds } from './timespan';

describe('TimeSpan strings', () => {
  it.each([
    ['00:00:05', 5],
    ['00:10:00', 600],
    ['1.02:03:04', 93784],
    ['00:00:01.5000000', 1.5],
    ['-00:00:02', -2],
  ])('reads %s', (text, seconds) => expect(toSeconds(text)).toBe(seconds));

  it('rejects other text', () => {
    expect(toSeconds('5s')).toBeNull();
    expect(toSeconds(null)).toBeNull();
  });

  it.each([
    [5, '00:00:05'],
    [600, '00:10:00'],
    [93784, '1.02:03:04'],
  ])('writes %s', (seconds, text) => expect(fromSeconds(seconds)).toBe(text));
});
