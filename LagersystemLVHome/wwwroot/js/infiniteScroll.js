// Infinite Scroll - Intersection Observer
let observer = null;
let dotNetRef = null;
let isLoading = false;

export function initialize(element, dotNetReference, threshold) {
    dotNetRef = dotNetReference;

    // Cleanup previous observer
    if (observer) {
        observer.disconnect();
    }
    observer = new IntersectionObserver(
  async (entries) => {
   const entry = entries[0];
            
 // Trigger load when element becomes visible
            if (entry.isIntersecting && !isLoading) {
         isLoading = true;
         
   try {
     await dotNetRef.invokeMethodAsync('LoadMoreItems');
    } catch (error) {
  console.error('Error loading more items:', error);
       } finally {
       setTimeout(() => {
    isLoading = false;
  }, 500);
                }
        }
     },
        {
          root: null, // viewport
       rootMargin: `${threshold}px`,
        threshold: 0.01
      }
    );
    if (element) {
        observer.observe(element);
    }
}

export function dispose() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
    dotNetRef = null;
isLoading = false;
}
