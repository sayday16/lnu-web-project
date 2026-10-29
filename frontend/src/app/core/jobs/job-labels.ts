import { JobStatus } from './job.model';

const STATUS_LABELS: Record<JobStatus, string> = {
  Pending: 'У черзі',
  Running: 'Виконується',
  Completed: 'Готово',
  Failed: 'Помилка',
  Cancelled: 'Скасовано',
};

const STAGE_LABELS: Record<string, string> = {
  Queued: 'Очікує на вільний вузол',
  Starting: 'Підготовка',
  'Forward elimination': 'Прямий хід',
  'Back substitution': 'Зворотний хід',
  Done: 'Завершено',
  Cancelling: 'Скасування…',
  Cancelled: 'Скасовано',
  Interrupted: 'Перервано',
  Failed: 'Помилка',
  'Time limit exceeded': 'Перевищено ліміт часу',
};

export function statusLabel(status: JobStatus): string {
  return STATUS_LABELS[status];
}

export function stageLabel(stage: string): string {
  return STAGE_LABELS[stage] ?? stage;
}
