import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { ClaimDetail, ClaimSummary, Dashboard, SubmitClaimRequest } from './models';

/** One method per Claims API route (DESIGN.md §7). */
@Injectable({ providedIn: 'root' })
export class ClaimsApi {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<ClaimSummary[]>('/api/claims');
  }

  get(id: string) {
    return this.http.get<ClaimDetail>(`/api/claims/${id}`);
  }

  submit(body: SubmitClaimRequest) {
    return this.http.post<ClaimDetail>('/api/claims', body);
  }

  assign(id: string) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/assign`, null);
  }

  requestInformation(id: string, question: string) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests`, { question });
  }

  respond(id: string, requestId: string, response: string) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests/${requestId}/response`, { response });
  }

  adjustReserve(id: string, reserve: number) {
    return this.http.put<ClaimDetail>(`/api/claims/${id}/reserve`, { reserve });
  }

  settle(id: string, amount: number) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/settle`, { amount });
  }

  reject(id: string, reason: string) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/reject`, { reason });
  }

  dashboard() {
    return this.http.get<Dashboard>('/api/dashboard');
  }
}

/** The human-readable message from a ProblemDetails error response. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the server. Is the API running?';
    return error.error?.detail ?? error.error?.title ?? `Request failed (${error.status}).`;
  }
  return 'Something went wrong. Please try again.';
}
