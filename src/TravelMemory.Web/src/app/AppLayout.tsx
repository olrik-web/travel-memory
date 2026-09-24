import { Link, Outlet } from 'react-router-dom';

export function AppLayout() {
  return (
    <div className="site-shell">
      <header className="site-header">
        <Link className="brand" to="/trips" aria-label="Travel Memory forside">
          <span className="brand-mark" aria-hidden="true">
            TM
          </span>
          <span>
            <strong>Travel Memory</strong>
            <small>Dine rejser som minder</small>
          </span>
        </Link>
      </header>
      <Outlet />
    </div>
  );
}
