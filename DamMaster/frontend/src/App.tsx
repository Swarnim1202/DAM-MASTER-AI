import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from './auth/AuthContext';
import type { Policy } from './auth/roles';
import Layout from './components/Layout';
import Login from './pages/Login';
import ChangePassword from './pages/ChangePassword';
import Dashboard from './pages/Dashboard';
import Dams from './pages/Dams';
import DamDetail from './pages/DamDetail';
import SensorDetail from './pages/SensorDetail';
import ImportPage from './pages/ImportPage';
import Alerts from './pages/Alerts';
import Library from './pages/Library';
import Users from './pages/Users';
import Audit from './pages/Audit';
import Inspections from './pages/Inspections';
import InspectionDetail from './pages/InspectionDetail';
import NotificationsLog from './pages/NotificationsLog';
import Reports from './pages/Reports';
import Gateways from './pages/Gateways';
import GatewayDetail from './pages/GatewayDetail';
import SystemStatus from './pages/SystemStatus';
import DesignProjects from './pages/DesignProjects';
import DesignWorkspace from './pages/DesignWorkspace';
import DesignValidation from './pages/DesignValidation';
import MapPage from './pages/MapPage';
import Surveys from './pages/Surveys';
import LibrarySearch from './pages/LibrarySearch';
import { FieldHome, FieldInspection } from './pages/Field';
import Portfolio from './pages/Portfolio';

function Guard({ children, policy }: { children: ReactNode; policy?: Policy }) {
  const { user, loading, can } = useAuth(); const loc = useLocation();
  if (loading) return <div className="empty">Loading…</div>;
  if (!user) return <Navigate to="/login" replace />;
  if (user.mustChangePassword && loc.pathname !== '/password') return <Navigate to="/password" replace />;
  if (policy && !can(policy)) return <div className="error">Your role does not have access to this page.</div>;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/field" element={<Guard><FieldHome /></Guard>} />
      <Route path="/field/:id" element={<Guard><FieldInspection /></Guard>} />
      <Route element={<Guard><Layout /></Guard>}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/password" element={<ChangePassword />} />
        <Route path="/dams" element={<Dams />} />
        <Route path="/dams/:id" element={<DamDetail />} />
        <Route path="/sensors/:id" element={<SensorDetail />} />
        <Route path="/alerts" element={<Alerts />} />
        <Route path="/import" element={<ImportPage />} />
        <Route path="/library" element={<Library />} />
        <Route path="/library/search" element={<LibrarySearch />} />
        <Route path="/map" element={<MapPage />} />
        <Route path="/portfolio" element={<Guard policy="viewPortfolio"><Portfolio /></Guard>} />
        <Route path="/surveys" element={<Surveys />} />
        <Route path="/inspections" element={<Inspections />} />
        <Route path="/reports" element={<Reports />} />
        <Route path="/gateways" element={<Gateways />} />
        <Route path="/design" element={<DesignProjects />} />
        <Route path="/design/validation" element={<DesignValidation />} />
        <Route path="/design/:id" element={<DesignWorkspace />} />
        <Route path="/gateways/:id" element={<GatewayDetail />} />
        <Route path="/system" element={<Guard policy="viewSystem"><SystemStatus /></Guard>} />
        <Route path="/inspections/:id" element={<InspectionDetail />} />
        <Route path="/notifications" element={<Guard policy="viewNotificationLog"><NotificationsLog /></Guard>} />
        <Route path="/users" element={<Guard policy="manageUsers"><Users /></Guard>} />
        <Route path="/audit" element={<Guard policy="viewAudit"><Audit /></Guard>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
