import { Link, Outlet } from 'react-router-dom';

export function AppLayout() {
  return (
    <div className="site-shell">
      <header className="site-header">
        <Link className="brand" to="/trips" aria-label="Travel Memory home">
          <span className="brand-mark" aria-hidden="true">
            TM
          </span>
          <span>
            <strong>Travel Memory</strong>
            <small>Your trips as memories</small>
          </span>
        </Link>
      </header>
      <Outlet />
    </div>
  );
}
