import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from '../api.config';
import { Job, JobEstimate, JobLimits } from './job.model';

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${API_URL}/jobs`;

  list(): Observable<Job[]> {
    return this.http.get<Job[]>(this.baseUrl);
  }

  limits(): Observable<JobLimits> {
    return this.http.get<JobLimits>(`${this.baseUrl}/limits`);
  }

  estimate(size: number): Observable<JobEstimate> {
    return this.http.get<JobEstimate>(`${this.baseUrl}/estimate`, { params: { size } });
  }

  create(size: number, seed: number | null): Observable<Job> {
    return this.http.post<Job>(this.baseUrl, { size, seed });
  }

  cancel(id: string): Observable<Job> {
    return this.http.post<Job>(`${this.baseUrl}/${id}/cancel`, {});
  }
}
