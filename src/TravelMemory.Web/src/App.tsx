import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from './app/AppLayout';
import { PhotoImportPage } from './features/photos/PhotoImportPage';
import { NewTripPage } from './features/trips/NewTripPage';
import { TripDetailPage } from './features/trips/TripDetailPage';
import { TripListPage } from './features/trips/TripListPage';
import './App.css';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppLayout />}>
          <Route index element={<Navigate replace to="/trips" />} />
          <Route path="/trips" element={<TripListPage />} />
          <Route path="/trips/new" element={<NewTripPage />} />
          <Route path="/trips/:tripId" element={<TripDetailPage />} />
          <Route path="/trips/:tripId/import" element={<PhotoImportPage />} />
          <Route path="*" element={<Navigate replace to="/trips" />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
