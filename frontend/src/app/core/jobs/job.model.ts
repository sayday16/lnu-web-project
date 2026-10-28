export type JobStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export interface JobResult {
  firstValues: number[];
  maxError: number;
  elapsedMs: number;
}

export interface Job {
  id: string;
  size: number;
  seed: number;
  nodeId: string | null;
  status: JobStatus;
  progress: number;
  cancelRequested: boolean;
  stage: string;
  result: JobResult | null;
  errorMessage: string | null;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
}

export interface JobLimits {
  minMatrixSize: number;
  maxMatrixSize: number;
  maxActiveJobsPerUser: number;
  maxStoredJobsPerUser: number;
  maxComputeSeconds: number;
}

export function isActive(job: Job): boolean {
  return job.status === 'Pending' || job.status === 'Running';
}
