import { createLivePlayer } from './livePlayer';
import { createMsePlayer, liveEdgeTarget, maxLagSeconds, rateFor } from './msePlayer';
import type { PlayerState } from './types';

const source = { url: 'http://recorder/api/cameras/c1/live.m3u8', lowLatencyUrl: 'http://recorder/api/cameras/c1/live.mp4', token: () => 'access-1' };

afterEach(() => vi.unstubAllGlobals());

describe('low-latency live player', () => {
  it('keeps the playhead at the live edge', () => {
    expect(liveEdgeTarget(null, 0, null)).toBeNull();
    // Starting: jump to just before the newest frame.
    expect(liveEdgeTarget(12.5, 0, 10)).toBeCloseTo(12.2);
    // Close enough: leave it.
    expect(liveEdgeTarget(12.5, 12.5 - maxLagSeconds / 2, 10)).toBeNull();
    // Fell behind (a hidden tab, a slow decoder): catch up.
    expect(liveEdgeTarget(20, 18, 10)).toBeCloseTo(19.7);
    // In between, the speed keeps a small cushion: slower when frames arrive just in time, faster when behind.
    expect([rateFor(0.05), rateFor(0.3), rateFor(0.8)]).toEqual([0.95, 1, 1.05]);
  });

  it('asks for the token in a header and says what is wrong', async () => {
    const fetch = vi.fn(async () => new Response(null, { status: 503 }));
    vi.stubGlobal('fetch', fetch);
    const states: PlayerState[] = [];
    const player = createMsePlayer(document.createElement('video'), source, (s) => states.push(s), () => undefined);
    await vi.waitFor(() => expect(states).toContainEqual({ status: 'error', message: 'No video from the camera yet.' }));
    expect(fetch).toHaveBeenCalledWith(source.lowLatencyUrl, expect.objectContaining({ headers: { Authorization: 'Bearer access-1' } }));
    player.destroy();
  });

  it('hands over to HLS for cameras the recorder cannot stream with low latency', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 415 })));
    const needsHls = vi.fn();
    const player = createMsePlayer(document.createElement('video'), source, () => undefined, needsHls);
    await vi.waitFor(() => expect(needsHls).toHaveBeenCalledTimes(1));
    player.destroy();
  });

  it('hands over to HLS when the browser cannot play the codec', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(new ReadableStream(), { status: 200, headers: { 'X-Video-Codec': 'avc1.640033' } })));
    const isTypeSupported = vi.fn(() => false);
    vi.stubGlobal('MediaSource', Object.assign(function MediaSource() {}, { isTypeSupported }));
    const needsHls = vi.fn();
    const player = createMsePlayer(document.createElement('video'), source, () => undefined, needsHls);
    await vi.waitFor(() => expect(needsHls).toHaveBeenCalled());
    expect(isTypeSupported).toHaveBeenCalledWith('video/mp4; codecs="avc1.640033"');
    player.destroy();
  });

  it('uses HLS in browsers without Media Source Extensions', () => {
    // jsdom has none: no low-latency request is made.
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const player = createLivePlayer(document.createElement('video'), source, () => undefined);
    expect(fetch).not.toHaveBeenCalled();
    player.destroy();
  });
});
