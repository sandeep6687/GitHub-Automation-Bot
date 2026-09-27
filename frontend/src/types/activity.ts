export interface ActivityAction {
  actionType: string;
  status: string;
  executedAt: string;
  durationMs?: number;
  errorMessage?: string;
}

export interface ActivityEvent {
  eventId: string;
  deliveryId: string;
  eventType: string;
  action?: string;
  status: 'Pending' | 'Processing' | 'Retrying' | 'Success' | 'Failed';
  createdAt: string;
  processedAt?: string;
  attemptCount: number;
  lastError?: string;
  parsedData?: string;
  actions: ActivityAction[];
}

export interface ActivityResponse {
  items: ActivityEvent[];
}
