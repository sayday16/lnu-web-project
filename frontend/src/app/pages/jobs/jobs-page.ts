import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { describeHttpError } from '../../core/http-error';
import { Job, JobLimits, JobStatus, isActive } from '../../core/jobs/job.model';
import { JobsService } from '../../core/jobs/jobs.service';

const POLL_INTERVAL_MS = 1000;
const MAX_SEED = 2147483647;
const SECONDS_PER_CUBED_SIZE = 9e-10;

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

@Component({
  selector: 'app-jobs-page',
  imports: [ReactiveFormsModule, DatePipe, DecimalPipe],
  templateUrl: './jobs-page.html',
  styleUrl: './jobs-page.scss',
})
export class JobsPage implements OnInit, OnDestroy {
  private readonly jobsService = inject(JobsService);
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  readonly maxSeed = MAX_SEED;
  readonly jobs = signal<Job[]>([]);
  readonly limits = signal<JobLimits | null>(null);
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly activeCount = computed(() => this.jobs().filter(isActive).length);
  readonly limitReached = computed(() => {
    const limits = this.limits();
    return limits !== null && this.activeCount() >= limits.maxActiveJobsPerUser;
  });

  readonly form = new FormGroup({
    size: new FormControl<number | null>(1000, [Validators.required]),
    seed: new FormControl<number | null>(null, [
      Validators.min(0),
      Validators.max(MAX_SEED),
      Validators.pattern(/^\d+$/),
    ]),
  });

  private readonly sizeValue = toSignal(this.form.controls.size.valueChanges, {
    initialValue: this.form.controls.size.value,
  });

  readonly estimatedSeconds = computed(() => {
    const size = Number(this.sizeValue());
    return Number.isFinite(size) && size > 0 ? size ** 3 * SECONDS_PER_CUBED_SIZE : null;
  });

  readonly exceedsTimeLimit = computed(() => {
    const limits = this.limits();
    const estimate = this.estimatedSeconds();
    return limits !== null && estimate !== null && estimate > limits.maxComputeSeconds;
  });

  ngOnInit(): void {
    this.jobsService.limits().subscribe({
      next: (limits) => {
        this.limits.set(limits);
        this.form.controls.size.setValidators([
          Validators.required,
          Validators.min(limits.minMatrixSize),
          Validators.max(limits.maxMatrixSize),
          Validators.pattern(/^\d+$/),
        ]);
        this.form.controls.size.updateValueAndValidity();
      },
      error: (error: unknown) => this.error.set(describeHttpError(error)),
    });

    this.refresh();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopPolling();
  }

  submit(): void {
    if (this.form.invalid || this.limitReached()) {
      this.form.markAllAsTouched();
      return;
    }

    const { size, seed } = this.form.getRawValue();
    this.submitting.set(true);
    this.error.set(null);

    this.jobsService.create(size!, seed).subscribe({
      next: () => {
        this.submitting.set(false);
        this.refresh();
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(describeHttpError(error));
      },
    });
  }

  cancel(job: Job): void {
    this.jobsService.cancel(job.id).subscribe({
      next: () => this.refresh(),
      error: (error: unknown) => {
        this.error.set(describeHttpError(error));
        this.refresh();
      },
    });
  }

  statusLabel(status: JobStatus): string {
    return STATUS_LABELS[status];
  }

  stageLabel(stage: string): string {
    return STAGE_LABELS[stage] ?? stage;
  }

  isActive(job: Job): boolean {
    return isActive(job);
  }

  formatDuration(seconds: number): string {
    if (seconds < 1) {
      return 'менше 1 с';
    }
    const rounded = Math.round(seconds);
    const minutes = Math.floor(rounded / 60);
    const rest = rounded % 60;
    return minutes > 0 ? `${minutes} хв ${rest} с` : `${rest} с`;
  }

  formatValues(values: number[]): string {
    return values.map((value) => value.toFixed(6)).join(', ');
  }

  durationSeconds(job: Job): number | null {
    if (!job.startedAt) {
      return null;
    }
    const end = job.finishedAt ? new Date(job.finishedAt).getTime() : Date.now();
    return (end - new Date(job.startedAt).getTime()) / 1000;
  }

  private refresh(): void {
    this.stopPolling();

    this.jobsService.list().subscribe({
      next: (jobs) => {
        this.jobs.set(jobs);
        this.loading.set(false);
        if (jobs.some(isActive)) {
          this.schedulePoll();
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeHttpError(error));
        if (this.jobs().some(isActive)) {
          this.schedulePoll();
        }
      },
    });
  }

  private schedulePoll(): void {
    if (this.destroyed) {
      return;
    }
    this.pollTimer = setTimeout(() => this.refresh(), POLL_INTERVAL_MS);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
