import { useCurrentUser } from './authQueries';

export function UserMenu() {
  const currentUser = useCurrentUser();

  if (!currentUser.data) {
    return null;
  }

  // A form POST rather than fetch: signing out ends the provider session through a
  // top-level redirect, and the SameSite cookie keeps other sites from posting here.
  return (
    <form className="user-menu" method="post" action="/api/auth/logout">
      <span className="user-name">{currentUser.data.name}</span>
      <button className="button button-secondary" type="submit">
        Sign out
      </button>
    </form>
  );
}
