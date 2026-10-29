import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  return inject(AuthService).getToken() ? true : inject(Router).createUrlTree(['/login']);
};

export const guestGuard: CanActivateFn = () => {
  return inject(AuthService).getToken() ? inject(Router).createUrlTree(['/jobs']) : true;
};

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.getToken()) {
    return inject(Router).createUrlTree(['/login']);
  }
  return auth.isAdmin() ? true : inject(Router).createUrlTree(['/jobs']);
};
