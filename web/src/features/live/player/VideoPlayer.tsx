import { memo, useEffect, useRef, useState } from 'react';
import { useApi } from '../../../data/ApiContext';
import { usePlayerFactory } from './PlayerContext';
import type { PlayerState } from './types';
import { usePageVisible } from './usePageVisible';

/** A camera's live video; plays only while the page is visible. `small`: a grid tile, which plays the camera's smaller stream. */
export const VideoPlayer = memo(function VideoPlayer({ cameraId, label, small = false }: { cameraId: string; label: string; small?: boolean }) {
  const { media } = useApi();
  const factory = usePlayerFactory();
  const visible = usePageVisible();
  const video = useRef<HTMLVideoElement>(null);
  const [state, setState] = useState<PlayerState>({ status: 'connecting' });

  useEffect(() => {
    if (!visible || !video.current) return;
    const player = factory(video.current, { url: media.liveUrl(cameraId), lowLatencyUrl: media.liveMp4Url(cameraId, small), token: media.token }, setState);
    return () => player.destroy();
  }, [cameraId, factory, media, visible, small]);

  return (
    <div className="player">
      <video ref={video} muted playsInline aria-label={label} />
      {state.status !== 'playing' && (
        <div className={`player-state ${state.status}`} role={state.status === 'error' ? 'alert' : 'status'}>
          {state.status === 'connecting' ? 'Connecting…' : state.message}
        </div>
      )}
    </div>
  );
});
