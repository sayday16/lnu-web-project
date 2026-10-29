import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';
import { AdminPage } from './pages/admin/admin-page';
import { JobsPage } from './pages/jobs/jobs-page';
import { LoginPage } from './pages/login/login-page';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [guestGuard] },
  { path: 'jobs', component: JobsPage, canActivate: [authGuard] },
  { path: 'admin', component: AdminPage, canActivate: [adminGuard] },
  { path: '', pathMatch: 'full', redirectTo: 'jobs' },
  { path: '**', redirectTo: 'jobs' },
];
