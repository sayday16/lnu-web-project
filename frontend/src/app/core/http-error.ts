import { HttpErrorResponse } from '@angular/common/http';

interface ApiError {
  message?: string;
  title?: string;
  errors?: Record<string, string[]>;
}

export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Сталася невідома помилка.';
  }

  if (error.status === 0) {
    return 'Сервер недоступний. Перевірте, що бекенд запущений.';
  }

  const body = error.error as ApiError | null;
  if (body?.message) {
    return body.message;
  }

  if (body?.errors) {
    return Object.values(body.errors).flat().join(' ');
  }

  return body?.title ?? `Помилка сервера (${error.status}).`;
}
