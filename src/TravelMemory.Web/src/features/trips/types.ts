export interface Trip {
  id: string;
  title: string;
  startDate?: string;
  endDate?: string;
  createdAtUtc: string;
}

export interface TripListResponse {
  items: Trip[];
}

export interface CreateTripRequest {
  title: string;
  startDate?: string;
  endDate?: string;
}
