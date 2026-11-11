/** Leaving the app for another site (an identity provider): one place, so tests can see where it would go. */
export const browser = {
  leave(url: string): void {
    window.location.assign(url);
  },
};
