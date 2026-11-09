import { Link } from 'react-router-dom';
import { useCreateExports } from '../../data/exports';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

interface Props {
  cameraIds: string[];
  from: number;
  to: number;
  caseId?: string | null;
}

/** Exports a stretch of time as watermarked MP4s, one per camera, made in the background. */
export function ExportButton({ cameraIds, from, to, caseId }: Props) {
  const create = useCreateExports();
  const tooLong = to - from > 60 * 60_000;

  return (
    <>
      <button
        type="button"
        disabled={create.isPending || tooLong}
        title={tooLong ? 'An export lasts at most an hour.' : undefined}
        onClick={() => create.mutate({ cameraIds, from, to, caseId })}
      >
        {create.isPending ? 'Exporting…' : 'Export MP4'}
      </button>
      {create.isSuccess && (
        <span className="small" role="status">
          Preparing {create.data.length} video{create.data.length === 1 ? '' : 's'}: <Link to="/exports">see Exports</Link>.
        </span>
      )}
      <ErrorMessage error={create.error} />
    </>
  );
}
