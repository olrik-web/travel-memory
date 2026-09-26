import type { components } from '../../api/schema';

type Schemas = components['schemas'];

export type Trip = Schemas['TripResponse'];
export type TripListResponse = Schemas['TripListResponse'];
export type CreateTripRequest = Schemas['CreateTripRequest'];
export type UpdateTripRequest = Schemas['UpdateTripRequest'];
