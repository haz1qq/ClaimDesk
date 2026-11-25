import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CurrentUserService } from './current-user.service';

/** Adds X-User-Id to every API call once a user has been chosen. */
export const userIdInterceptor: HttpInterceptorFn = (request, next) => {
  const userId = inject(CurrentUserService).userId();
  return next(userId ? request.clone({ setHeaders: { 'X-User-Id': userId } }) : request);
};
