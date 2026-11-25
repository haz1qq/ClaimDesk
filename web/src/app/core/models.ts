// Mirrors the Claims API and Notifications service contracts (src/ClaimsApi/Api/Dtos.cs).

export type Role = 'Claimant' | 'Officer' | 'Manager';
export type Market = 'SG' | 'MY' | 'HK' | 'TH' | 'ID' | 'AU';
export type ClaimType = 'Motor' | 'Property';
export type ClaimStatus = 'Submitted' | 'UnderReview' | 'AwaitingInformation' | 'Settled' | 'Rejected';
export type ClaimEventType =
  | 'ClaimSubmitted'
  | 'ClaimAssigned'
  | 'InformationRequested'
  | 'InformationProvided'
  | 'ReserveAdjusted'
  | 'ClaimSettled'
  | 'ClaimRejected';

export interface User {
  id: string;
  name: string;
  role: Role;
  market: Market | null;
}

export interface ClaimSummary {
  id: string;
  number: string;
  status: ClaimStatus;
  type: ClaimType;
  market: Market;
  currency: string;
  claimedAmount: number;
  reserve: number;
  claimantId: string;
  claimantName: string;
  assignedOfficerId: string | null;
  assignedOfficerName: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface InformationRequest {
  id: string;
  question: string;
  response: string | null;
  requestedAt: string;
  respondedAt: string | null;
}

export interface TimelineEntry {
  type: ClaimEventType;
  actorId: string;
  actorName: string;
  occurredAt: string;
}

export interface ClaimDetail {
  summary: ClaimSummary;
  incidentDate: string;
  description: string;
  settlementAmount: number | null;
  rejectionReason: string | null;
  decidedAt: string | null;
  informationRequests: InformationRequest[];
  timeline: TimelineEntry[];
}

export interface SubmitClaimRequest {
  type: ClaimType;
  incidentDate: string; // yyyy-MM-dd
  description: string;
  claimedAmount: number;
}

export interface Dashboard {
  openByStatus: { status: ClaimStatus; count: number }[];
  unassigned: number;
  officers: { officerId: string; name: string; open: number; closedLast30Days: number; avgDaysToDecision: number | null }[];
  exposure: { currency: string; totalReserve: number; openClaims: number }[];
  openOver7Days: number;
  openOver30Days: number;
}

export interface AppNotification {
  id: string;
  claimId: string;
  claimNumber: string;
  message: string;
  createdAt: string;
}
