import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ROLE_LABEL } from '../auth/roles';
import Bell from './Bell';

export default function Layout() {
  const { user, logout, can } = useAuth();
  const nav = useNavigate();
  const initials = (user?.fullName ?? '').split(/\s+/).filter((w) => /^[A-Z]/.test(w)).map((w) => w[0]).join('').slice(0, 2);
  return (
    <div className="shell">
      <aside className="side" aria-label="Main navigation">
        <div className="brand"><b>DAM MASTER AI</b><small>Dam safety monitoring · v7.0</small></div>
        <nav className="nav">
          <div className="nav-sec">Monitor</div>
          <NavLink to="/" end>Portfolio dashboard</NavLink>
          <NavLink to="/dams">Dams</NavLink>
          <NavLink to="/alerts">Alerts</NavLink>
          <NavLink to="/map">Map</NavLink>
          {can('viewPortfolio') && <NavLink to="/portfolio">Federated portfolio</NavLink>}
          {can('fieldInspect') && <NavLink to="/field">Field app (mobile)</NavLink>}
          <NavLink to="/inspections">Inspections</NavLink>
          <div className="nav-sec">Engineering</div>
          <NavLink to="/design" end>Design workspace</NavLink>
          <NavLink to="/design/validation">Design validation</NavLink>
          <div className="nav-sec">Data</div>
          <NavLink to="/import">Import data</NavLink>
          <NavLink to="/reports">Data availability</NavLink>
          <NavLink to="/gateways">Gateways</NavLink>
          <NavLink to="/surveys">Remote sensing &amp; surveys</NavLink>
          <div className="nav-sec">Knowledge</div>
          <NavLink to="/library" end>Reference library</NavLink>
          <NavLink to="/library/search">Document search</NavLink>
          {(can('manageUsers') || can('viewAudit') || can('viewNotificationLog') || can('viewSystem')) && <div className="nav-sec">Administration</div>}
          {can('manageUsers') && <NavLink to="/users">Users &amp; roles</NavLink>}
          {can('viewNotificationLog') && <NavLink to="/notifications">Notifications</NavLink>}
          {can('viewAudit') && <NavLink to="/audit">Audit trail</NavLink>}
          {can('viewSystem') && <NavLink to="/system">System status</NavLink>}
        </nav>
      </aside>
      <div className="main">
        <header className="top">
          <span className="muted small">Rodic Consultants · Digital vertical</span>
          <div className="who">
            <Bell />
            <div className="avatar" aria-hidden="true">{initials || 'U'}</div>
            <div><b>{user?.fullName}</b><small>{user ? ROLE_LABEL[user.role] : ''}</small></div>
            <button className="btn sm" onClick={() => nav('/password')}>Password</button>
            <button className="btn sm" onClick={async () => { await logout(); nav('/login'); }}>Sign out</button>
          </div>
        </header>
        <main className="page"><Outlet /></main>
      </div>
    </div>
  );
}
