export interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  public readonly status: number;
  public readonly problem?: ProblemDetails;

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.detail ?? problem?.title ?? `API request failed with status ${status}.`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

export async function requestJson<T>(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(input, init);

  if (!response.ok) {
    const contentType = response.headers.get('content-type');
    const problem = contentType?.includes('application/problem+json')
      ? ((await response.json()) as ProblemDetails)
      : undefined;

    throw new ApiError(response.status, problem);
  }

  return (await response.json()) as T;
}

export async function requestVoid(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<void> {
  const response = await fetch(input, init);

  if (!response.ok) {
    const contentType = response.headers.get('content-type');
    const problem = contentType?.includes('application/problem+json')
      ? ((await response.json()) as ProblemDetails)
      : undefined;

    throw new ApiError(response.status, problem);
  }
}
