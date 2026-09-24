import { describe, expect, it } from 'vitest';
import { runWithConcurrency } from './boundedConcurrency';

describe('bounded photo uploads', () => {
  it('never exceeds the configured concurrency and processes every item', async () => {
    let active = 0;
    let maximumActive = 0;
    const completed: number[] = [];

    await runWithConcurrency(
      Array.from({ length: 20 }, (_, index) => index),
      4,
      async (item) => {
        active++;
        maximumActive = Math.max(maximumActive, active);
        await new Promise((resolve) => setTimeout(resolve, 2));
        completed.push(item);
        active--;
      },
    );

    expect(maximumActive).toBe(4);
    expect(completed).toHaveLength(20);
    expect([...completed].sort((left, right) => left - right)).toEqual(
      Array.from({ length: 20 }, (_, index) => index),
    );
  });
});
