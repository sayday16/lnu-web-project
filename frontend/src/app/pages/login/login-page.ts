import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { describeHttpError } from '../../core/http-error';

type Mode = 'login' | 'register';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly mode = signal<Mode>('login');
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(6)],
    }),
  });

  switchMode(mode: Mode): void {
    this.mode.set(mode);
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();
    const request =
      this.mode() === 'register'
        ? this.auth.register(email, password).pipe(switchMap(() => this.auth.login(email, password)))
        : this.auth.login(email, password);

    this.submitting.set(true);
    this.error.set(null);

    request.subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigate(['/jobs']);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.error.set(describeHttpError(error));
      },
    });
  }
}
