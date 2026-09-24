export async function runWithConcurrency<T>(
  items: T[],
  maximumConcurrency: number,
  operation: (item: T) => Promise<void>,
) {
  let nextIndex = 0;
  const workers = Array.from(
    { length: Math.min(maximumConcurrency, items.length) },
    async () => {
      while (nextIndex < items.length) {
        const item = items[nextIndex];
        nextIndex++;
        if (item !== undefined) {
          await operation(item);
        }
      }
    },
  );
  await Promise.all(workers);
}
