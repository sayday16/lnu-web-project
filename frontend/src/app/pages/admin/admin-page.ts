import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, tap, timer } from 'rxjs';
import { AdminOverview } from '../../core/admin/admin.model';
import { AdminService } from '../../core/admin/admin.service';
import { formatDuration } from '../../core/format';
import { describeHttpError } from '../../core/http-error';
import { stageLabel } from '../../core/jobs/job-labels';

const REFRESH_INTERVAL_MS = 2000;

@Component({
  selector: 'app-admin-page',
  imports: [DatePipe],
  templateUrl: './admin-page.html',
  styleUrl: './admin-page.scss',
})
export class AdminPage {
  private readonly adminService = inject(AdminService);

  readonly error = signal<string | null>(null);
  readonly updatedAt = signal<Date | null>(null);

  readonly overview = toSignal(
    timer(0, REFRESH_INTERVAL_MS).pipe(
      switchMap(() =>
        this.adminService.overview().pipe(
          tap(() => {
            this.error.set(null);
            this.updatedAt.set(new Date());
          }),
          catchError((error: unknown) => {
            this.error.set(describeHttpError(error));
            return of(null);
          }),
        ),
      ),
    ),
    { initialValue: null as AdminOverview | null },
  );

  nodeLoadPercent(running: number, max: number): number {
    return max > 0 ? Math.min(100, Math.round((running / max) * 100)) : 0;
  }

  formatDuration(seconds: number): string {
    return formatDuration(seconds);
  }

  stageLabel(stage: string): string {
    return stageLabel(stage);
  }
}
