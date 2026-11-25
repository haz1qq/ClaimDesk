import { Routes } from '@angular/router';
import { ClaimDetailPage } from './claimant/claim-detail';
import { MyClaims } from './claimant/my-claims';
import { NewClaim } from './claimant/new-claim';
import { DashboardPage } from './dashboard/dashboard';
import { Home } from './home';
import { WorkClaim } from './officer/work-claim';
import { WorkQueue } from './officer/work-queue';

export const routes: Routes = [
  { path: '', pathMatch: 'full', component: Home, title: 'ClaimDesk' },
  { path: 'claims', component: MyClaims, title: 'My claims · ClaimDesk' },
  { path: 'claims/new', component: NewClaim, title: 'Report an incident · ClaimDesk' },
  { path: 'claims/:id', component: ClaimDetailPage, title: 'Claim · ClaimDesk' },
  { path: 'work', component: WorkQueue, title: 'Work queue · ClaimDesk' },
  { path: 'work/:id', component: WorkClaim, title: 'Claim · ClaimDesk' },
  { path: 'dashboard', component: DashboardPage, title: 'Dashboard · ClaimDesk' },
  { path: '**', redirectTo: '' },
];
