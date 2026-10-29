export interface AdminNode {
  id: string;
  url: string;
  reachable: boolean;
  runningJobs: number;
  maxConcurrentJobs: number;
  freeSlots: number;
  lastSeenAt: string | null;
}

export interface AdminRunningJob {
  id: string;
  email: string;
  size: number;
  nodeId: string | null;
  progress: number;
  stage: string;
  startedAt: string | null;
  cancelRequested: boolean;
  estimatedRemainingSeconds: number | null;
}

export interface AdminQueuedJob {
  id: string;
  email: string | null;
  size: number;
  position: number;
  estimatedWaitSeconds: number | null;
}

export interface AdminStats {
  users: number;
  reachableNodes: number;
  totalNodes: number;
  busySlots: number;
  totalSlots: number;
  jobsToday: number;
  completedToday: number;
  failedToday: number;
  cancelledToday: number;
}

export interface AdminOverview {
  nodes: AdminNode[];
  running: AdminRunningJob[];
  queue: AdminQueuedJob[];
  stats: AdminStats;
}
