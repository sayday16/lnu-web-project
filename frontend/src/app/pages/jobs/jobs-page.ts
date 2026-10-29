import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, combineLatest, debounceTime, of, startWith, switchMap, timer } from 'rxjs';
import { formatDuration } from '../../core/format';
import { describeHttpError } from '../../core/http-error';
import { stageLabel, statusLabel } from '../../core/jobs/job-labels';
import { Job, JobEstimate, JobLimits, JobStatus, isActive } from '../../core/jobs/job.model';
import { JobsService } from '../../core/jobs/jobs.service';

const POLL_INTERVAL_MS = 1000;
const MAX_SEED = 2147483647;
const ESTIMATE_REFRESH_MS = 5000;

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
  readonly notice = signal<string | null>(null);
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

  readonly estimate = toSignal(
    combineLatest([
      this.form.controls.size.valueChanges.pipe(startWith(this.form.controls.size.value)),
      timer(0, ESTIMATE_REFRESH_MS),
    ]).pipe(
      debounceTime(300),
      switchMap(([size]) => {
        const value = Number(size);
        return Number.isInteger(value) && value > 0
          ? this.jobsService.estimate(value).pipe(catchError(() => of(null)))
          : of(null);
      }),
    ),
    { initialValue: null as JobEstimate | null },
  );

  readonly exceedsTimeLimit = computed(() => {
    const limits = this.limits();
    const estimate = this.estimate();
    return limits !== null && estimate !== null && estimate.durationSeconds > limits.maxComputeSeconds;
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
    this.notice.set(null);

    this.jobsService.create(size!, seed).subscribe({
      next: (job) => {
        this.submitting.set(false);
        this.notice.set(this.describeQueuing(job));
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
    return statusLabel(status);
  }

  stageLabel(stage: string): string {
    return stageLabel(stage);
  }

  isActive(job: Job): boolean {
    return isActive(job);
  }

  describeQueuing(job: Job): string | null {
    if (job.status !== 'Pending' || job.queuePosition === null) {
      return null;
    }
    if (job.estimatedWaitSeconds === null) {
      return 'Немає доступних обчислювальних вузлів — задачу поставлено в чергу.';
    }
    if (job.estimatedWaitSeconds <= 0) {
      return null;
    }
    return `Усі вузли зайняті — задачу поставлено в чергу (позиція ${job.queuePosition} з ${job.queueLength}, орієнтовне очікування ≈ ${this.formatDuration(job.estimatedWaitSeconds)}).`;
  }

  formatDuration(seconds: number): string {
    return formatDuration(seconds);
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
